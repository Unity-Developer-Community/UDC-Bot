using Discord.Commands;
using DiscordBot.Attributes;
using DiscordBot.Modules.Base;
using Discord.WebSocket;
using DiscordBot.Attributes;
using DiscordBot.Policies;
using DiscordBot.Services;

namespace DiscordBot.Modules;

[RequireComponentEnabled("unity-help")]
public class UnityHelpModule : BotCommandModuleBase
{
    #region Dependency Injection

    public UnityHelpService HelpService { get; set; }
    public UserService UserService { get; set; }
    public IUnityHelpPolicy UnityHelpPolicy { get; set; }

    #endregion // Dependency Injection

    [Command("resolve"), Alias("complete")]
    [Summary("When a question is answered, use this command to mark it as resolved.")]
    public async Task ResolveAsync()
    {
        if (!UnityHelpPolicy.IsAvailable)
            return;
        if (!IsValidUser() || !IsInHelpChannel())
            await Context.Message.DeleteAsync();
        await HelpService.OnUserRequestChannelClose(Context.User, Context.Channel as SocketThreadChannel);
    }
    
    [Command("pending-questions")]
    [Summary("Moderation only command, announces the number of pending questions in the help channel.")]
    [RequireModerator, HideFromHelp, IgnoreBots]
    public async Task PendingQuestionsAsync()
    {
        if (!UnityHelpPolicy.IsAvailable)
        {
            await ReplyAsync("UnityHelp Service currently disabled.").DeleteAfterSeconds(15);
            return;
        }
        var trackedQuestionCount = HelpService.GetTrackedQuestionCount();
        await ReplyAsync($"There are {trackedQuestionCount} pending questions in the help channel.");
    }

    #region Utility

    private bool IsInHelpChannel() => UnityHelpPolicy.IsHelpThread(Context.Channel);
    private bool IsValidUser() => !Context.User.IsUserBotOrWebhook();

    #endregion // Utility
}
