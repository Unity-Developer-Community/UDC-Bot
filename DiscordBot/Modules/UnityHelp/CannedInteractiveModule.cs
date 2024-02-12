using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Service;
using DiscordBot.Services;
using DiscordBot.Settings;
using static DiscordBot.Service.CannedResponseService;

namespace DiscordBot.Modules;

public class CannedInteractiveModule : InteractionModuleBase
{
    #region Dependency Injection

    public UnityHelpService HelpService { get; set; }
    public BotSettings BotSettings { get; set; }
    public CannedResponseService CannedResponseService { get; set; }
    
    #endregion // Dependency Injection
    
    // Responses are any of the CannedResponseType enum
    [SlashCommand("can", "Prepared responses to help answer common questions")]
    public async Task CannedResponses(CannedHelp type)
    {
        if (Context.User.IsUserBotOrWebhook())
            return;

        var embed = CannedResponseService.GetCannedResponse((CannedResponseType)type);
        await Context.Interaction.RespondAsync(string.Empty, embed: embed.Build());
    }
    
    [SlashCommand("resources", "Links to resources to help answer common questions")]
    public async Task Resources(CannedResources type)
    {
        if (Context.User.IsUserBotOrWebhook())
            return;

        var embed = CannedResponseService.GetCannedResponse((CannedResponseType)type);
        await Context.Interaction.RespondAsync(string.Empty, embed: embed.Build());
    }
}