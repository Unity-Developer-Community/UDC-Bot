using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentOverviewTests
{
    [Fact]
    public async Task OverviewStaysFixedWhileListingsAndHealthChange()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Engine.RefreshOverviewAsync(default);
        var first = rig.State.Overview.MessageId;
        var listing = await rig.Post();
        await rig.Engine.RefreshOverviewAsync(default);
        rig.Clock.Advance(TimeSpan.FromMinutes(6)); await rig.Engine.RefreshOverviewAsync(default);
        Assert.Equal(first, rig.State.Overview.MessageId); Assert.Empty(rig.Discord.DeletedMessages);
        var overview = Assert.Single(rig.Discord.Messages.Values, m => m.Message.Marker.StartsWith("recruitment:overview:"));
        Assert.Contains("Open: 1", overview.Message.Embed!.Description);
        Assert.NotNull(listing.StaffMessageId);
        var upserts = rig.Discord.Upserts;
        rig.Clock.Advance(TimeSpan.FromMinutes(1)); await rig.Engine.RefreshOverviewAsync(default);
        Assert.Equal(upserts, rig.Discord.Upserts);
    }

    [Fact]
    public async Task LostInitialOverviewResponseRecoversSameMessageAfterRestart()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        rig.Discord.LoseNextSendResponse = true;
        await rig.Engine.RefreshOverviewAsync(default);
        Assert.True(rig.State.Overview.SendPending); Assert.Null(rig.State.Overview.MessageId);
        var sent = Assert.Single(rig.Discord.Messages.Values).Id;
        await rig.Restart(); await rig.Staff("reconcile");
        await rig.Engine.RefreshOverviewAsync(default);
        Assert.Equal(sent, rig.State.Overview.MessageId);
        Assert.Single(rig.Discord.Messages.Values); Assert.False(rig.State.Overview.SendPending);
    }

    [Fact]
    public async Task LegacyRetirementFinishesWithoutMovingOverviewOrBlockingAcceptance()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Engine.RefreshOverviewAsync(default);
        var current = rig.State.Overview.MessageId;
        rig.State.Overview.RetiredMessageId = "888888";
        rig.State.Overview.RetiredMarker = $"recruitment:overview:{rig.State.InstanceId}:legacy";
        rig.Discord.FailCleanup = true;
        await rig.Engine.RefreshOverviewAsync(default);
        Assert.NotNull(rig.State.Overview.Fault);
        await rig.Post(); Assert.Contains("accepted", await rig.Ack());
        rig.Discord.FailCleanup = false;
        rig.Clock.Now = rig.State.Overview.Fault!.RetryAt!.Value;
        await rig.Engine.RefreshOverviewAsync(default);
        Assert.Null(rig.State.Overview.RetiredMessageId); Assert.Equal(current, rig.State.Overview.MessageId);
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
        rig.Clock.Advance(TimeSpan.FromMinutes(6));
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
