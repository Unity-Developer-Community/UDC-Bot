using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Modules.Base;
using DiscordBot.Policies;
using DiscordBot.Services;
using DiscordBot.Services.Recruitment;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Modules.Recruitment;

// No component-enabled precondition: staff must be able to preview while setup is degraded or stopped.
[Group("recruitment", "Review and publish recruitment forum guidelines")]
public sealed class RecruitmentSetupModule(RecruitService service, RecruitmentGuidelinePublisher publisher,
    IBotAuthorizationPolicy authorization, IOptions<DiscordGuildOptions> guild,
    IOptions<RecruitmentOptions> options) : BotInteractionModuleBase
{
    [SlashCommand("preview", "Privately preview guidelines and the current topic/tag fingerprints")]
    public async Task Preview(RecruitmentForumKind forum)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            RequireStaff();
            var preview = await publisher.PreviewAsync(Forum(forum), CancellationToken.None);
            var embed = new EmbedBuilder().WithTitle("Guidelines preview · ABCDE is a placeholder")
                .WithDescription(preview.Topic)
                .AddField("Current topic fingerprint", preview.CurrentTopicHash)
                .AddField("Current tag fingerprint", preview.CurrentTagHash)
                .WithFooter("Review the existing Discord Guidelines too. Publish explicitly adopts that exact topic; tag repair is optional.").Build();
            await FollowupAsync(embed: embed, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        catch (Exception error) { await FollowupAsync(RecruitmentOwnerModule.Failure(error), ephemeral: true); }
    }

    [SlashCommand("publish", "Adopt the previewed topic and publish guidelines; optionally repair a renamed Closed binding")]
    public async Task Publish(RecruitmentForumKind forum,
        [Summary("expected-topic-hash", "Current topic fingerprint from the preview")] string expectedTopicHash,
        [Summary("repair-tag-hash", "Optional current tag fingerprint to release a renamed Closed binding")] string? repairTagHash = null)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            RequireStaff();
            await service.ExecuteAdvisoryAsync(async token =>
            {
                RequireStaff();
                await publisher.EnsureAsync(Forum(forum), token, expectedTopicHash, repairTagHash);
                return true;
            });
            await FollowupAsync("Guidelines and Closed tag confirmed for this forum. Advisory recovery will refresh affected posts.", ephemeral: true);
        }
        catch (Exception error) { await FollowupAsync(RecruitmentOwnerModule.Failure(error), ephemeral: true); }
    }

    private RecruitmentForum Forum(RecruitmentForumKind kind) => RecruitmentForumClassifier.GetForums(options.Value.Forums).Single(forum => forum.Kind == kind);

    private void RequireStaff()
    {
        if (Context.Guild?.Id != guild.Value.GuildId || Context.User is not SocketGuildUser user ||
            !(user.GuildPermissions.Administrator || authorization.IsModerator(user)))
            throw new InvalidOperationException("Only configured moderators or administrators in this guild can manage recruitment setup.");
    }
}
