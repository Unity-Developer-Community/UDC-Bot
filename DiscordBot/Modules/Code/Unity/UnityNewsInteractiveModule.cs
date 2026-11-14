using Discord.Interactions;
using DiscordBot.Services.Code.Unity;

namespace DiscordBot.Modules.Code.Unity;

public class UnityNewsInteractiveModule : InteractionModuleBase
{
    public FeedService FeedService { get; set; } = null!;

    /// <summary>The "Subscribe / Unsubscribe" button of a blog card: toggles the news role and tells only the member.</summary>
    [ComponentInteraction(UnityNewsCards.SubscribeButtonId)]
    public async Task ToggleNewsRole()
    {
        if (Context.User is not IGuildUser user)
        {
            await RespondAsync("This only works in the server.", ephemeral: true);
            return;
        }

        await RespondAsync(await FeedService.ToggleNewsRoleAsync(user), ephemeral: true);
    }
}
