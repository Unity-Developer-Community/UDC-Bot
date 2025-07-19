using System.Globalization;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Extensions;
using DiscordBot.Services;
using DiscordBot.Utils;

namespace DiscordBot.Modules;

[Group("bday", "Birthday-related commands")]
public class BirthdaySlashModule : InteractionModuleBase
{
    public DatabaseService DatabaseService { get; set; }
    public ILoggingService LoggingService { get; set; }

    [SlashCommand("show", "Shows the next upcoming birthday(s)")]
    public async Task ShowNextBirthday()
    {
        await Context.Interaction.DeferAsync();

        try
        {
            var upcomingBirthdays = await GetNextBirthdays();

            if (upcomingBirthdays.Count == 0)
            {
                await Context.Interaction.FollowupAsync("**No upcoming birthdays found!**");
                return;
            }

            var embed = new EmbedBuilder()
                .WithColor(Color.Orange)
                .WithTitle("🎂 Upcoming Birthdays");

            var birthday = upcomingBirthdays[0].Birthday.Value;
            var today = DateTime.Today;
            
            // Calculate next occurrence of birthday
            var nextOccurrence = new DateTime(today.Year, birthday.Month, birthday.Day);
            if (nextOccurrence < today)
            {
                nextOccurrence = new DateTime(today.Year + 1, birthday.Month, birthday.Day);
            }
            
            // Calculate days until birthday
            var daysUntil = (nextOccurrence - today).Days;
            
            string timeframe;
            if (daysUntil == 0)
            {
                timeframe = "Today! 🎉";
            }
            else if (daysUntil == 1)
            {
                timeframe = "Tomorrow!";
            }
            else
            {
                timeframe = $"In {daysUntil} days ({nextOccurrence:MMMM dd})";
            }

            var description = $"**{timeframe}**\n\n";

            foreach (var userBirthday in upcomingBirthdays)
            {
                var user = await Context.Guild.GetUserAsync(ulong.Parse(userBirthday.UserID));
                var displayName = user?.DisplayName ?? user?.Username ?? "Unknown User";
                
                var age = CalculateAge(userBirthday.Birthday.Value, nextOccurrence);
                var ageString = age.HasValue ? $" (turns {age.Value})" : "";
                
                description += $"🎂 **{displayName}**{ageString}\n";
            }

            embed.WithDescription(description);
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
        [Summary(description: "Your birthday in MM/DD/YYYY or MM/DD format (e.g., 03/15/1990 or 03/15)")] string date)
    {
        await Context.Interaction.DeferAsync(ephemeral: true);

        var provider = CultureInfo.InvariantCulture;

        if (!TryParseBirthdayInput(date, out var birthday))
        {
            await Context.Interaction.FollowupAsync("Invalid date format. Please use MM/DD/YYYY or MM/DD format (e.g., 03/15/1990 or 03/15).", ephemeral: true);
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
            var birthdayString = birthday.ToString("MMMM dd", provider);
            if (birthday.Year != 1900)
            {
                birthdayString += $", {birthday.Year}";
            }
            
            await Context.Interaction.FollowupAsync($"Your birthday has been set to **{birthdayString}**! 🎂", ephemeral: true);
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

    private async Task<List<ServerUser>> GetNextBirthdays()
    {
        // Get the next birthday to find the date, then get all users with birthdays on that date
        var nextBirthday = await DatabaseService.Query.GetNextBirthday();
        if (nextBirthday?.Birthday == null)
            return new List<ServerUser>();

        // Get all users who have birthdays on the same month/day as the next birthday
        var nextBirthdayDate = nextBirthday.Birthday.Value;
        var allUsersWithBirthdays = await GetUsersWithBirthdayOnDate(nextBirthdayDate.Month, nextBirthdayDate.Day);
        
        return allUsersWithBirthdays;
    }

    private async Task<List<ServerUser>> GetUsersWithBirthdayOnDate(int month, int day)
    {
        // Use the new database method to get all users with birthdays on a specific month/day
        return (await DatabaseService.Query.GetBirthdaysOnDate(month, day)).ToList();
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
        
        // Try parsing with year first (MM/DD/YYYY)
        if (DateTime.TryParseExact(input, "M/d/yyyy", provider, DateTimeStyles.None, out birthday))
        {
            return true;
        }
        
        // Try parsing without year (MM/DD) - use 1900 as sentinel value
        if (DateTime.TryParseExact(input, "M/d", provider, DateTimeStyles.None, out var tempDate))
        {
            birthday = new DateTime(1900, tempDate.Month, tempDate.Day);
            return true;
        }
        
        return false;
    }
}