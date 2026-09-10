using System.Globalization;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Attributes;
using DiscordBot.Services;
using DiscordBot.Utils;

namespace DiscordBot.Modules.Profiles;

[Group("UserModule"), Alias("")]
public class BirthdayModule : ModuleBase
{
    public DatabaseService DatabaseService { get; set; } = null!;
    public ILoggingService LoggingService { get; set; } = null!;

    [Command("Birthday"), HideFromHelp]
    [Summary("Display next member birthday.")]
    [Alias("bday")]
    public async Task Birthday()
    {
        try
        {
            var nextBirthday = await DatabaseService.Query.GetNextBirthday();

            if (nextBirthday?.Birthday == null)
            {
                await ReplyAsync("**No upcoming birthdays found!**").DeleteAfterTime(minutes: 3);
                await Context.Message.DeleteAfterTime(minutes: 3);
                return;
            }

            var nextBirthdayDate = nextBirthday.Birthday.Value;
            var allBirthdaysOnDate = await DatabaseService.Query.GetBirthdaysOnDate(nextBirthdayDate.Month, nextBirthdayDate.Day);

            var today = DateTime.Today;
            var nextOccurrence = new DateTime(today.Year, nextBirthdayDate.Month, nextBirthdayDate.Day);
            if (nextOccurrence < today)
                nextOccurrence = new DateTime(today.Year + 1, nextBirthdayDate.Month, nextBirthdayDate.Day);

            var daysUntil = (nextOccurrence - today).Days;

            string message;
            if (daysUntil == 0)
            {
                var names = await GetDisplayNames(allBirthdaysOnDate);
                message = allBirthdaysOnDate.Count == 1
                    ? $"**{names[0]}'s birthday is today! 🎉**"
                    : $"**{string.Join(" and ", names)} have birthdays today! 🎉**";
            }
            else if (daysUntil == 1)
            {
                var names = await GetDisplayNames(allBirthdaysOnDate);
                message = allBirthdaysOnDate.Count == 1
                    ? $"**{names[0]}'s birthday is tomorrow! ({nextOccurrence:MMMM dd})**"
                    : $"**{string.Join(" and ", names)} have birthdays tomorrow! ({nextOccurrence:MMMM dd})**";
            }
            else
            {
                var nameWithAges = new List<string>();
                foreach (var bday in allBirthdaysOnDate)
                {
                    var user = await Context.Guild.GetUserAsync(ulong.Parse(bday.UserID));
                    var displayName = user?.DisplayName ?? user?.Username ?? "Unknown User";
                    var age = CalculateAge(bday.Birthday!.Value, nextOccurrence);
                    var ageString = age.HasValue ? $" (turns {age})" : "";
                    nameWithAges.Add($"{displayName}{ageString}");
                }
                message = allBirthdaysOnDate.Count == 1
                    ? $"**{nameWithAges[0]}'s birthday is in {daysUntil} days! ({nextOccurrence:MMMM dd})**"
                    : $"**{string.Join(" and ", nameWithAges)} have birthdays in {daysUntil} days! ({nextOccurrence:MMMM dd})**";
            }

            await (ReplyAsync(message).DeleteAfterTime(minutes: 3) ?? Task.CompletedTask);
            await (Context.Message.DeleteAfterTime(minutes: 3) ?? Task.CompletedTask);
        }
        catch (Exception e)
        {
            await LoggingService.LogAction($"Error getting next birthday: {e.Message}", ExtendedLogSeverity.Warning);
            await (ReplyAsync("Sorry, I couldn't retrieve upcoming birthday information.").DeleteAfterSeconds(30) ?? Task.CompletedTask);
        }
    }

    [Command("Birthday"), Priority(27)]
    [Summary("Display birthday of mentioned user. Syntax : !birthday @user")]
    [Alias("bday")]
    public async Task Birthday(IUser user)
    {
        try
        {
            var searchUser = await DatabaseService.GetOrAddUser(user as SocketGuildUser);
            if (searchUser == null)
            {
                await (ReplyAsync($"Sorry, I couldn't access **{user.Username}**'s data.").DeleteAfterSeconds(30) ?? Task.CompletedTask);
                await (Context.Message.DeleteAfterTime(minutes: 3) ?? Task.CompletedTask);
                return;
            }

            var birthday = await DatabaseService.Query.GetBirthday(searchUser.UserID);

            var guildUser = await Context.Guild.GetUserAsync(user.Id);
            var displayName = guildUser?.DisplayName ?? user.Username;

            if (birthday == null)
            {
                await (ReplyAsync($"Sorry, **{displayName}** hasn't set their birthday yet. They can use `/bday set` to add it!")
                    .DeleteAfterSeconds(30) ?? Task.CompletedTask);
            }
            else
            {
                string birthdayString;
                string ageString = "";

                if (birthday.Value.Year != 1900)
                {
                    birthdayString = birthday.Value.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);
                    var age = CalculateAge(birthday.Value, DateTime.Today);
                    if (age.HasValue)
                        ageString = $" ({age}yo)";
                }
                else
                {
                    birthdayString = birthday.Value.ToString("dd MMMM", CultureInfo.InvariantCulture);
                }

                var message = $"**{displayName}**'s birthdate: __**{birthdayString}**__{ageString}";
                await (ReplyAsync(message).DeleteAfterTime(minutes: 3) ?? Task.CompletedTask);
            }

            await (Context.Message.DeleteAfterTime(minutes: 3) ?? Task.CompletedTask);
        }
        catch (Exception e)
        {
            await LoggingService.LogAction($"Error getting birthday for user {user.Id}: {e.Message}", ExtendedLogSeverity.Warning);
            var guildUser = await Context.Guild.GetUserAsync(user.Id);
            var displayName = guildUser?.DisplayName ?? user.Username;
            await (ReplyAsync($"Sorry, I couldn't retrieve **{displayName}**'s birthday.").DeleteAfterSeconds(30) ?? Task.CompletedTask);
            await (Context.Message.DeleteAfterTime(minutes: 3) ?? Task.CompletedTask);
        }
    }

    private async Task<List<string>> GetDisplayNames(IList<Extensions.ServerUser> users)
    {
        var names = new List<string>();
        foreach (var u in users)
        {
            var guildUser = await Context.Guild.GetUserAsync(ulong.Parse(u.UserID));
            names.Add(guildUser?.DisplayName ?? guildUser?.Username ?? "Unknown User");
        }
        return names;
    }

    private static int? CalculateAge(DateTime birthDate, DateTime today)
    {
        if (birthDate.Year == 1900 || birthDate.Year == today.Year)
            return null;

        var age = today.Year - birthDate.Year;
        if (today.Month < birthDate.Month || (today.Month == birthDate.Month && today.Day < birthDate.Day))
            age--;

        return age;
    }
}

