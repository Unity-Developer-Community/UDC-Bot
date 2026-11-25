using Discord.Commands;
using DiscordBot.Services;
using DiscordBot.Services.Code.Unity;
using DiscordBot.Services.Moderation;

namespace DiscordBot.Modules.Code.Unity;

[Group("UserModule"), Alias("")]
public class UnityNewsModule : ModuleBase
{
    public BotDeletionRegistry BotDeletions { get; set; } = null!;
    #region Dependency Injection

    public FeedService FeedService { get; set; } = null!;
    public UpdateService UpdateService { get; set; } = null!;

    #endregion // Dependency Injection

    [Command("Blog"), Priority(25)]
    [Summary("Where the Unity blog posts are, with a link to the latest one. In the discussion channel, shows the latest post's card again.")]
    public async Task Blog()
    {
        await Context.Message.DeleteCommandMessageAsync(BotDeletions);
        await FeedService.BlogCommandAsync(UpdateService.GetFeedData(), Context.Channel);
    }
}
