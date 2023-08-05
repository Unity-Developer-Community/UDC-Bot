using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Services;
using DiscordBot.Settings;

namespace DiscordBot.Modules;

public class UnityHelpModule : ModuleBase
{
    #region Dependency Injection

    public UnityHelpService HelpService { get; set; }
    public UserService UserService { get; set; }
    public BotSettings BotSettings { get; set; }

    #endregion // Dependency Injection

    [Command("resolve"), Alias("complete")]
    [Summary("When a question is answered, use this command to mark it as resolved.")]
    public async Task ResolveAsync()
    {
        if (!BotSettings.UnityHelpBabySitterEnabled)
            return;
        if (!IsValidUser() || !IsInHelpChannel())
            await Context.Message.DeleteAsync();
        await HelpService.OnUserRequestChannelClose(Context.User, Context.Channel as SocketThreadChannel);
    }

    #region Utility

    private bool IsInHelpChannel() => Context.Channel.IsThreadInChannel(BotSettings.GenericHelpChannel.Id);
    private bool IsValidUser() => !Context.User.IsUserBotOrWebhook();

    #endregion // Utility
}