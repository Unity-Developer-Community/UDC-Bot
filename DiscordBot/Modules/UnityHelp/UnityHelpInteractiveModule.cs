using Discord.Interactions;
using DiscordBot.Services;
using DiscordBot.Settings;
using Discord.WebSocket;

namespace DiscordBot.Modules;

public class UnityHelpInteractiveModule : InteractionModuleBase
{
    #region Dependency Injection

    public UnityHelpService HelpService { get; set; }
    public BotSettings BotSettings { get; set; }
    
    #endregion // Dependency Injection

    [SlashCommand("resolve-question", "If in unity-help forum channel, resolve the thread")]
    public async Task ResolveQuestion()
    {
        if (!BotSettings.UnityHelpBabySitterEnabled)
            return;
        
        await Context.Interaction.DeferAsync(ephemeral: true);

        if (!IsValidUser())
        {
            await Context.Interaction.RespondAsync("Invalid User", ephemeral: true);
            return;
        }

        if (!IsInHelpChannel())
        {
            await Context.Interaction.FollowupAsync(
                $"This command can only be used in <#{BotSettings.GenericHelpChannel.Id}> channels", ephemeral: true);
            return;
        }

        var response =
            await HelpService.OnUserRequestChannelClose(Context.User, Context.Channel as SocketThreadChannel);
        await Context.Interaction.FollowupAsync(response, ephemeral: true);
    }

    #region Message Commands

    [MessageCommand("Correct Answer")]
    public async Task MarkResponseAnswer(IMessage targetResponse)
    {
        if (!BotSettings.UnityHelpBabySitterEnabled)
            return;
        
        await Context.Interaction.DeferAsync(ephemeral: true);
        if (!IsValidUser())
        {
            await Context.Interaction.RespondAsync(string.Empty, ephemeral: true);
            return;
        }
        if (!IsInHelpChannel())
        {
            await Context.Interaction.FollowupAsync(
                $"This command can only be used in <#{BotSettings.GenericHelpChannel.Id}> channels", ephemeral: true);
            return;
        }

        if (targetResponse.Author == Context.User || targetResponse.Author.IsBot)
        {
            await Context.Interaction.FollowupAsync("You can't mark your own response as correct", ephemeral: true);
            return;
        }

        var response = await HelpService.MarkResponseAsAnswer(Context.User, targetResponse);
        await Context.Interaction.FollowupAsync( response, ephemeral: true);
    }

    #endregion // Context Commands
    

    #region Utility
    
    private bool IsInHelpChannel() => Context.Channel.IsThreadInChannel(BotSettings.GenericHelpChannel.Id);
    private bool IsValidUser() => !Context.User.IsUserBotOrWebhook();

    #endregion // Utility
}