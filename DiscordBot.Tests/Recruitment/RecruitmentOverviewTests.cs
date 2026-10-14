using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentOverviewTests
{
    [Fact]
    public async Task FeedActivityReplacesOverviewOnlyAfterCooldownAndRetainsListingAudit()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var listing = await rig.Post();
        await rig.Engine.RefreshOverviewAsync(default);
        var first = rig.State.Overview.MessageId;
        Assert.NotNull(first);
        rig.Engine.FeedActivity("999999");
        rig.Clock.Advance(TimeSpan.FromMinutes(4)); await rig.Engine.RefreshOverviewAsync(default);
        Assert.Equal(first, rig.State.Overview.MessageId);
        rig.Clock.Advance(TimeSpan.FromMinutes(1)); await rig.Engine.RefreshOverviewAsync(default);
        Assert.NotEqual(first, rig.State.Overview.MessageId);
        Assert.Contains(first, rig.Discord.DeletedMessages);
        Assert.DoesNotContain(listing.StaffMessageId, rig.Discord.DeletedMessages);
        Assert.Single(rig.Discord.Messages.Values, m => m.Message.Marker.StartsWith("recruitment:overview:"));
        var upserts = rig.Discord.Upserts;
        rig.Engine.FeedActivity(rig.State.Overview.MessageId!, overview: true);
        rig.Clock.Advance(TimeSpan.FromMinutes(1)); await rig.Engine.RefreshOverviewAsync(default);
        Assert.Equal(upserts, rig.Discord.Upserts);
    }

    [Fact]
    public async Task ContinuousActivityCannotStarveReplacementAfterCooldown()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Engine.RefreshOverviewAsync(default);
        var first = rig.State.Overview.MessageId;
        for (var i = 0; i < 73; i++)
        {
            rig.Engine.FeedActivity((900000 + i).ToString());
            rig.Clock.Advance(TimeSpan.FromSeconds(5)); await rig.Engine.RefreshOverviewAsync(default);
            if (i < 59) Assert.Equal(first, rig.State.Overview.MessageId);
        }
        Assert.NotEqual(first, rig.State.Overview.MessageId);
        Assert.Single(rig.Discord.DeletedMessages);
    }

    [Fact]
    public async Task LostReplacementResponseRecoversSameMessageAfterRestartBeforeRetiringOldOne()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Engine.RefreshOverviewAsync(default);
        var first = rig.State.Overview.MessageId;
        rig.Engine.FeedActivity("999999"); rig.Clock.Advance(TimeSpan.FromMinutes(6));
        rig.Discord.LoseNextSendResponse = true;
        await rig.Engine.RefreshOverviewAsync(default);
        Assert.True(rig.State.Overview.SendPending); Assert.Equal(first, rig.State.Overview.MessageId);
        Assert.DoesNotContain(first, rig.Discord.DeletedMessages);
        await rig.Restart();
        await rig.Staff("reconcile");
        var sends = rig.Discord.Sends;
        await rig.Engine.RefreshOverviewAsync(default);
        Assert.Equal(sends, rig.Discord.Sends);
        Assert.NotEqual(first, rig.State.Overview.MessageId);
        Assert.Contains(first, rig.Discord.DeletedMessages);
        Assert.False(rig.State.Overview.SendPending);
    }

    [Fact]
    public async Task CleanupAndCosmeticFailureDoNotFreezeListingAcceptance()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Engine.RefreshOverviewAsync(default);
        rig.Discord.FailCleanup = true; rig.Engine.FeedActivity("999999"); rig.Clock.Advance(TimeSpan.FromMinutes(6));
        await rig.Engine.RefreshOverviewAsync(default);
        Assert.NotNull(rig.State.Overview.RetiredMessageId);
        var current = rig.State.Overview.MessageId;
        await rig.Post(); Assert.Contains("accepted", await rig.Ack());
        rig.Discord.FailCleanup = false;
        rig.Clock.Now = rig.State.Overview.Fault!.RetryAt!.Value;
        await rig.Engine.RefreshOverviewAsync(default);
        Assert.Null(rig.State.Overview.RetiredMessageId);
        Assert.Equal(current, rig.State.Overview.MessageId);
    }

    [Fact]
    public async Task MissingEmbedPermissionFallsBackToTextAndUnsafeFeedPublishesNothing()
    {
        using var rig = new RecruitmentRig(); rig.Discord.FeedEmbeds = false;
        await rig.Start(); await rig.Engine.RefreshOverviewAsync(default);
        var message = Assert.Single(rig.Discord.Messages.Values).Message;
        Assert.Null(message.Embed); Assert.Contains("Recruitment status", message.Content);
        rig.Discord.FailFeed = true; rig.Engine.InvalidateAccess(); await rig.Sweep();
        var sends = rig.Discord.Sends;
        rig.Engine.FeedActivity("999999"); rig.Clock.Advance(TimeSpan.FromMinutes(6));
        await rig.Engine.RefreshOverviewAsync(default);
        Assert.Equal(sends, rig.Discord.Sends);
        Assert.Contains("privacy invalid", rig.Engine.Health().Status);
    }

    [Fact]
    public async Task OverviewCanShowForumSetupHoldWithoutRevealingChallengeAnswers()
    {
        using var rig = new RecruitmentRig(); rig.Discord.FailForum = "11"; await rig.Start();
        await rig.Engine.RefreshOverviewAsync(default);
        var message = Assert.Single(rig.Discord.Messages.Values).Message;
        Assert.Contains("held", message.Embed!.Description);
        Assert.DoesNotContain(rig.State.RotationCode, message.Embed.Description);
        Assert.Contains("<#11>", message.Embed.Description);
    }
}
