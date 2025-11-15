using Discord.Commands;
using DiscordBot.Services;
using DiscordBot.Settings;
using DiscordBot.Attributes;
using System.Text.RegularExpressions;

namespace DiscordBot.Modules;

[Group("UserModule"), Alias("")]
public class ReminderModule : ModuleBase
{
    #region Dependency Injection

    public ReminderService ReminderService { get; set; }
    public BotSettings Settings { get; set; }

    #endregion

    // Command where user types !remindme followed by a time and a message
    // The time is human readable and is converted to a DateTime object
    [Command("remindme"), Alias("reminder"), Priority(80)]
    [Summary("Reminds users of a message based on time. Syntax : !remindme 1hour30min Watch a tutorial")]
    public async Task RemindMe(string time, [Remainder] string message)
    {
        if (Context.Message.MentionedEveryone || Context.Message.MentionedRoleIds.Count > 0)
        {
            await ReplyAsync("You can't mention groups or roles in a reminder.").DeleteAfterSeconds(seconds: 5);
            return;
        }

        if (Context.Message.MentionedUserIds.Count > 0)
        {
            message = Context.Message.GetType().ToString() + " " + message;
            // foreach (var user in Context.Message.MentionedUsers)
            // {
            //     message = message.Replace($"[<][@]{user.Id}[>]", user.GetPreferredUserName().ToBold());
            // }
            await ReplyAsync($"You can't mention users in a reminder.\n`{message}`").DeleteAfterSeconds(seconds: 25);
            return;
        }

        var reminderDate = Utils.Utils.ParseTimeFromString(time);
        if (reminderDate < DateTime.Now)
        {
            // There isn't really a way to add negative time
            await ReplyAsync(
"Invalid format for reminder.\\nCorrect Syntax: ``!remindme <1hour5m> <optional message>``")
                .DeleteAfterSeconds(seconds: 10);
            return;
        }

        // Add 1 second so we don't count the second we're on now as a second past
        reminderDate = reminderDate.AddSeconds(1);

        var dateInSeconds = (uint)(reminderDate - DateTime.Now).TotalSeconds;
        if (dateInSeconds < 30)
        {
            await ReplyAsync("Reminders must be more than 30 seconds away!");
            return;
        }

        // Check if user has to many reminders and tell them to delete some if so
        if (ReminderService.UserHasTooManyReminders(Context.User.Id))
        {
            await ReplyAsync("You have too many reminders! Please delete some before adding more.")
                .DeleteAfterSeconds(seconds: 10);
            return;
        }

        // if message is longer than 100 characters, truncate it
        if (message.Length > 100)
            message = message[..100];

        if (message == string.Empty)
            message = "No message specified";

        var reminder = new ReminderItem
        {
            UserId = Context.User.Id,
            MessageId = Context.Message.Id,
            ChannelId = Context.Channel.Id,
            Message = message,
            When = reminderDate
        };

        ReminderService.AddReminder(reminder);
        await Context.Message.AddReactionAsync(ReminderService.BotResponseEmoji);
        await ReplyAsync(
                $"Reminder set for {Utils.Utils.FormatTime((uint)(reminderDate - DateTime.Now).TotalSeconds)}")
            .DeleteAfterSeconds(seconds: 10);
    }

    [Command("remindme"), HideFromHelp]
    [Summary("Reminds users at a certain time. Syntax : !remindme 1hour30min")]
    public async Task RemindMe(string time)
    {
        await RemindMe(time, string.Empty);
    }

    // Command where user types !reminders to see all their reminders
    [Command("reminders"), Priority(81)]
    [Summary("Tell the user of their set reminders in bot channel.")]
    public async Task Reminders()
    {
        await Reminders(Context.User);
    }

    // Removes a users reminders themself
    [Command("removereminder"), Priority(81)]
    [Summary("Clears all reminders unless an Index is provided.")]
    [Alias("removereminders")]
    public async Task RemoveReminders(int index = 0)
    {
        await RemoveReminders(Context.User, index);
    }


    #region Moderator Commands

    // Moderators can use the commands in this space, other methods pass their calls through these to reduce duplicate code.

    // Allows a moderator to see reminders of a user
    [RequireModerator]
    [Command("reminders"), HideFromHelp]
    [Summary("Check user reminders.")]
    public async Task Reminders(IUser user)
    {
        await Context.Message.DeleteAsync();
        var reminders = ReminderService.GetUserReminders(user.Id);
        if (reminders.Count == 0)
        {
            await ReplyAsync($"{user.Username} has no reminders!").DeleteAfterSeconds(seconds: 5);
            return;
        }

        var embed = new EmbedBuilder();
        embed.WithTitle($"{user.Username} Reminders");
        embed.WithColor(new Color(0x89CFF0));
        int index = 1;
        foreach (var reminder in reminders)
        {
            var msgLink = Utils.Utils.MessageLinkBack(Context.Guild.Id, reminder.ChannelId, reminder.MessageId);
            embed.AddField(
                $"#{index++} | {Utils.Utils.FormatTime((uint)(reminder.When - DateTime.Now).TotalSeconds)}",
                $"[Link]({msgLink}) \"{reminder.Message}\"");
        }
        if (await Context.Guild.GetChannelAsync(Settings.BotCommandsChannel.Id)is IMessageChannel botCommands)
            await botCommands
                .SendMessageAsync(Context.User.Mention, false, embed.Build())
                .DeleteAfterSeconds(seconds: 30);
    }

    // Removes a users reminders for them
    [RequireModerator]
    [Command("removereminders"), HideFromHelp]
    [Summary("Clears user reminders.")]
    public async Task RemoveReminders(IUser user, int index = 0)
    {
        await Context.Message.DeleteAfterSeconds(seconds: 1);
        int removedReminders = ReminderService.RemoveReminders(user, index);
        if (removedReminders == 0)
            return;
        if (removedReminders == -1)
        {
            await ReplyAsync("Invalid index provided.");
            return;
        }

        await ReplyAsync($"{removedReminders.ToString()} Reminders removed.").DeleteAfterSeconds(seconds: 2);
    }

    [RequireModerator]
    [Command("remindersreboot"), HideFromHelp]
    [Summary("Used to restart the reminder service if it has crashed.")]
    public async Task RebootReminderService()
    {
        if (ReminderService.IsRunning)
        {
            await ReplyAsync("Reminder service is still running.").DeleteAfterSeconds(seconds: 5);
            return;
        }

        var result = ReminderService.RestartService();
        if (result)
            await ReplyAsync("Reminder service restarted.").DeleteAfterSeconds(seconds: 5);
        else
            await ReplyAsync("Reminder service failed to restart.").DeleteAfterSeconds(seconds: 5);
    }

    #endregion
}
