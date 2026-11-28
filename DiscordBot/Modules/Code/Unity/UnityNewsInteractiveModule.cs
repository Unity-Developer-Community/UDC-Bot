using Discord.Interactions;
using DiscordBot.Services;
using DiscordBot.Services.Code.Unity;

namespace DiscordBot.Modules.Code.Unity;

public class UnityNewsInteractiveModule : InteractionModuleBase
{
    public FeedService FeedService { get; set; } = null!;
    public UpdateService UpdateService { get; set; } = null!;

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

    /// <summary>The "Share to #unity-discussion" button of a blog card: posts the card to the discussion channel and tells only the member.</summary>
    [ComponentInteraction(UnityNewsCards.ShareButtonPrefix + "*")]
    public async Task ShareToDiscussion(string shareKey)
    {
        await DeferAsync(ephemeral: true);
        await FollowupAsync(await FeedService.ShareBlogAsync(UpdateService.GetFeedData(), shareKey, Context.User), ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }
}
