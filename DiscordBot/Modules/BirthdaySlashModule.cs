using System.Globalization;
using System.Text;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Extensions;
using DiscordBot.Services;

namespace DiscordBot.Modules;

[Group("bday", "Birthday-related commands")]
public class BirthdaySlashModule : InteractionModuleBase
{
    public DatabaseService DatabaseService { get; set; }
    public ILoggingService LoggingService { get; set; }

    [SlashCommand("show", "Shows the next upcoming birthday date(s)")]
    public async Task ShowNextBirthday(
        [Summary(description: "Number of upcoming birthday dates to show (min 1, max 10)")]
        [MinValue(1)]
        [MaxValue(10)]
        int count = 3)
    {
        await Context.Interaction.DeferAsync();

        try
        {
            var upcomingBirthdayGroups = await GetUpcomingBirthdayGroups(count);

            if (upcomingBirthdayGroups.Count == 0)
            {
                await Context.Interaction.FollowupAsync("**No upcoming birthdays found!**");
                return;
            }

            var embed = new EmbedBuilder()
                .WithColor(Color.Orange)
                .WithTitle("🎂 Upcoming Birthdays");

            var today = DateTime.Today;
            var descriptionBuilder = new StringBuilder();

            for (var groupIndex = 0; groupIndex < upcomingBirthdayGroups.Count; groupIndex++)
            {
                var birthdayGroup = upcomingBirthdayGroups[groupIndex];

                if (groupIndex > 0)
                {
                    descriptionBuilder.AppendLine();
                    descriptionBuilder.AppendLine();
                }

                descriptionBuilder.AppendLine($"**{FormatUpcomingTimeframe(today, birthdayGroup.NextDate)}**");

                foreach (var userBirthday in birthdayGroup.Users)
                {
                    var displayName = await ResolveDisplayName(userBirthday.UserID);
                    var age = userBirthday.Birthday.HasValue
                        ? CalculateAge(userBirthday.Birthday.Value, birthdayGroup.NextDate)
                        : null;
                    var ageString = age.HasValue ? $" (turns {age.Value})" : "";

                    descriptionBuilder.AppendLine($"🎂 **{displayName}**{ageString}");
                }
            }

            embed.WithDescription(descriptionBuilder.ToString());
            await Context.Interaction.FollowupAsync(embed: embed.Build());
        }
        catch (Exception e)
        {
            await LoggingService.LogAction($"Error getting next birthdays: {e.Message}", ExtendedLogSeverity.Warning);
            await Context.Interaction.FollowupAsync("**Error fetching upcoming birthdays.**");
        }
    }

    [SlashCommand("user", "Shows a specific user's birthday")]
    public async Task ShowUserBirthday(
        [Summary(description: "The user whose birthday you want to see")] IUser user)
    {
        await Context.Interaction.DeferAsync();

        try
        {
            var searchUser = await DatabaseService.GetOrAddUser(user as SocketGuildUser);
            if (searchUser == null)
            {
                await Context.Interaction.FollowupAsync($"Sorry, I couldn't access **{user.Username}**'s data.");
                return;
            }

            var birthday = await DatabaseService.Query.GetBirthday(searchUser.UserID);

            if (birthday == null)
            {
                await Context.Interaction.FollowupAsync(
                    $"Sorry, **{user.Username}** hasn't set their birthday yet. They can use `/bday set` to add it!");
                return;
            }

            var guildUser = await Context.Guild.GetUserAsync(user.Id);
            var displayName = guildUser?.DisplayName ?? user.Username;

            var provider = CultureInfo.InvariantCulture;
            string birthdayString;
            string ageString = "";

            // Check if year is meaningful (not 1900 which indicates no year specified)
            if (birthday.Value.Year != 1900)
            {
                birthdayString = birthday.Value.ToString("dd MMMM yyyy", provider);
                var age = CalculateAge(birthday.Value, DateTime.Today);
                if (age.HasValue)
                {
                    ageString = $" ({age}yo)";
                }
            }
            else
            {
                birthdayString = birthday.Value.ToString("dd MMMM", provider);
            }

            var embed = new EmbedBuilder()
                .WithColor(Color.Blue)
                .WithTitle($"🎂 {displayName}'s Birthday")
                .WithDescription($"**{birthdayString}**{ageString}")
                .WithFooter($"Use `/bday set` to add or update your birthday")
                .Build();

            await Context.Interaction.FollowupAsync(embed: embed);
        }
        catch (Exception e)
        {
            await LoggingService.LogAction($"Error getting birthday for user {user.Id}: {e.Message}", ExtendedLogSeverity.Warning);
            await Context.Interaction.FollowupAsync($"Sorry, I couldn't retrieve **{user.Username}**'s birthday.");
        }
    }

    [SlashCommand("set", "Set your birthday")]
    public async Task SetBirthday(
        [Summary(description: "Your birthday in DD/MM/YYYY or DD/MM format (e.g., 15/03/1990 or 15/03)")] string date)
    {
        await Context.Interaction.DeferAsync(ephemeral: true);

        if (!TryParseBirthdayInput(date, out var birthday))
        {
            await Context.Interaction.FollowupAsync("Invalid date format. Please use DD/MM/YYYY or DD/MM format (e.g., 15/03/1990 or 15/03).", ephemeral: true);
            return;
        }

        try
        {
            var user = await DatabaseService.GetOrAddUser(Context.User as SocketGuildUser);
            if (user == null)
            {
                await Context.Interaction.FollowupAsync("Failed to access your user data.", ephemeral: true);
                return;
            }

            await DatabaseService.Query.UpdateBirthday(user.UserID, birthday);
            await Context.Interaction.FollowupAsync($"Your birthday has been set to **{FormatBirthday(birthday)}**! 🎂", ephemeral: true);
        }
        catch (Exception e)
        {
            await LoggingService.LogAction($"Error setting birthday for user {Context.User.Id}: {e.Message}", ExtendedLogSeverity.Warning);
            await Context.Interaction.FollowupAsync("An error occurred while setting your birthday.", ephemeral: true);
        }
    }

    [SlashCommand("del", "Remove your birthday")]
    public async Task RemoveBirthday()
    {
        await Context.Interaction.DeferAsync(ephemeral: true);

        try
        {
            var user = await DatabaseService.GetOrAddUser(Context.User as SocketGuildUser);
            if (user == null)
            {
                await Context.Interaction.FollowupAsync("Failed to access your user data.", ephemeral: true);
                return;
            }

            var currentBirthday = await DatabaseService.Query.GetBirthday(user.UserID);
            if (currentBirthday == null)
            {
                await Context.Interaction.FollowupAsync("You don't have a birthday set.", ephemeral: true);
                return;
            }

            await DatabaseService.Query.UpdateBirthday(user.UserID, null);
            await Context.Interaction.FollowupAsync("Your birthday has been removed.", ephemeral: true);
        }
        catch (Exception e)
        {
            await LoggingService.LogAction($"Error removing birthday for user {Context.User.Id}: {e.Message}", ExtendedLogSeverity.Warning);
            await Context.Interaction.FollowupAsync("An error occurred while removing your birthday.", ephemeral: true);
        }
    }


    private static string FormatBirthday(DateTime birthday)
    {
        var provider = CultureInfo.InvariantCulture;
        var birthdayString = birthday.ToString("MMMM dd", provider);
        if (birthday.Year != 1900)
            birthdayString += $", {birthday.Year}";
        return birthdayString;
    }

    private static DateTime GetNextBirthdayOccurrence(DateTime birthday, DateTime today)
    {
        var nextDate = new DateTime(today.Year, birthday.Month, birthday.Day);
        if (nextDate < today)
        {
            nextDate = new DateTime(today.Year + 1, birthday.Month, birthday.Day);
        }

        return nextDate;
    }

    private static string FormatUpcomingTimeframe(DateTime today, DateTime nextOccurrence)
    {
        var daysUntil = (nextOccurrence - today).Days;
        if (daysUntil == 0)
        {
            return "Today! 🎉";
        }

        if (daysUntil == 1)
        {
            return "Tomorrow!";
        }

        return $"In {daysUntil} days ({nextOccurrence:MMMM dd})";
    }

    private async Task<string> ResolveDisplayName(string userId)
    {
        if (!ulong.TryParse(userId, out var discordUserId))
        {
            return $"Unknown User ({userId})";
        }

        var user = await Context.Guild.GetUserAsync(discordUserId);
        return user?.DisplayName ?? user?.Username ?? $"Unknown User ({userId})";
    }

    private async Task<List<UpcomingBirthdayGroup>> GetUpcomingBirthdayGroups(int numberOfDates)
    {
        var allBirthdays = await DatabaseService.Query.GetAllBirthdays();
        var today = DateTime.Today;

        return allBirthdays
            .Where(u => u.Birthday.HasValue)
            .GroupBy(u => GetNextBirthdayOccurrence(u.Birthday!.Value, today))
            .OrderBy(g => g.Key)
            .Take(numberOfDates)
            .Select(g => new UpcomingBirthdayGroup
            {
                NextDate = g.Key,
                Users = g.OrderBy(u => u.UserID).ToList()
            })
            .ToList();
    }

    private int? CalculateAge(DateTime birthDate, DateTime referenceDate)
    {
        if (birthDate.Year == 1900 || birthDate.Year == referenceDate.Year)
        {
            return null; // No year information available or invalid year
        }

        var age = referenceDate.Year - birthDate.Year;
        if (referenceDate.Month < birthDate.Month || (referenceDate.Month == birthDate.Month && referenceDate.Day < birthDate.Day))
        {
            age--;
        }

        return age;
    }

    private bool TryParseBirthdayInput(string input, out DateTime birthday)
    {
        birthday = default;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var provider = CultureInfo.InvariantCulture;

        // Parse as a date-only value, then force UTC kind for PostgreSQL timestamptz writes.
        if (DateTime.TryParseExact(input, "d/M/yyyy", provider, DateTimeStyles.None, out birthday))
        {
            birthday = DateTime.SpecifyKind(birthday, DateTimeKind.Utc);
            return true;
        }

        // Try parsing without year (DD/MM) - use 1900 as sentinel value
        if (DateTime.TryParseExact(input, "d/M", provider, DateTimeStyles.None, out var tempDate))
        {
            birthday = DateTime.SpecifyKind(new DateTime(1900, tempDate.Month, tempDate.Day), DateTimeKind.Utc);
            return true;
        }

        return false;
    }

    private sealed class UpcomingBirthdayGroup
    {
        public DateTime NextDate { get; init; }
        public List<ServerUser> Users { get; init; } = new();
    }
}