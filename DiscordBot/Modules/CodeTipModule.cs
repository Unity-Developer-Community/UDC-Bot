using Discord.Commands;
using DiscordBot.Services;

namespace DiscordBot.Modules;

[Group("UserModule"), Alias("")]
public class CodeTipModule : ModuleBase
{
    public CodeCheckService CodeCheckService { get; set; } = null!;

    [Command("CodeTip"), Priority(20)]
    [Summary("Show code formatting example. Syntax: !codetip userToPing(optional)")]
    [Alias("codetips")]
    public async Task CodeTip(IUser? user = null)
    {
        var message = user != null ? user.Mention + ", " : "";
        message += "When posting code, format it like so:" + Environment.NewLine;
        message += CodeCheckService.CodeFormattingExample;
        await Context.Message.DeleteAsync();
        await ReplyAsync(message).DeleteAfterSeconds(seconds: 60);
    }

    [Command("DisableCodeTips"), Priority(91)]
    [Summary("Stops code formatting reminders.")]
    public async Task DisableCodeTips()
    {
        await Context.Message.DeleteAsync();
        if (!CodeCheckService.CodeReminderCooldown.IsPermanent(Context.User.Id))
        {
            CodeCheckService.CodeReminderCooldown.SetPermanent(Context.User.Id, true);
            var uname = Context.User.GetUserPreferredName();
            await ReplyAsync($"{uname}, you will no longer be reminded about correct code formatting.").DeleteAfterTime(20);
        }
    }
}
