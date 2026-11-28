using Discord.Interactions;
using DiscordBot.Services.Moderation;

namespace DiscordBot.Modules.Server;

public class ImageSpamAlertInteractiveModule : InteractionModuleBase
{
    public ImageSpamAlertService AlertService { get; set; } = null!;

    /// <summary>The Ignore, Ban and False positive buttons of the AutoMod image spam alert.</summary>
    [ComponentInteraction(ImageSpamAlertIds.Prefix + ":*:*")]
    public async Task Act()
    {
        if (Context.Interaction is not IComponentInteraction component
            || !ImageSpamAlertIds.TryParse(component.Data.CustomId, out var action, out var userId))
        {
            await RespondAsync("I don't know that button.", ephemeral: true);
            return;
        }
        if (Context.User is not IGuildUser clicker)
        {
            await RespondAsync("This only works in the server.", ephemeral: true);
            return;
        }

        // Acknowledge within 3 seconds: the ban or timeout call can take longer.
        await DeferAsync();
        var outcome = await AlertService.HandleAsync(action, userId, clicker, component.Message);
        if (outcome.ClosedCard != null)
        {
            await ModifyOriginalResponseAsync(m =>
            {
                m.Components = outcome.ClosedCard;
                m.Flags = MessageFlags.ComponentsV2;
                m.AllowedMentions = AllowedMentions.None;
            });
        }
        else
        {
            await FollowupAsync(outcome.Error, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
    }
}
