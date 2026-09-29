using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentWorkflowTests
{
    [Fact]
    public async Task CorrectCodeAcceptsImmediately_UsingOriginalCreationTime()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        var post = await rig.Post(); rig.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Contains("accepted", await rig.Ack());
        Assert.Equal(rig.Clock.Now, post.AcceptedAt);
        Assert.Equal(post.CreatedAt, rig.State.Authors[post.OwnerId].LastAcceptedCreation);
    }

    [Theory]
    [InlineData(2, 28)]
    [InlineData(40, 0)]
    public async Task AcceptedDeletionKeepsCreationCooldown(int elapsedDays, int remainingDays)
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        var post = await rig.Post(); await rig.Ack();
        rig.Clock.Advance(TimeSpan.FromDays(elapsedDays));
        rig.Discord.Threads.Remove(post.Id); await rig.Sweep();
        var replacement = await rig.Post("101", forum: "14");
        var policy = RecruitmentPolicy.Evaluate(rig.State, replacement, rig.Clock.Now, 30);
        Assert.Equal(remainingDays == 0, policy.Allowed);
        Assert.Equal(post.CreatedAt, rig.State.Authors[post.OwnerId].LastAcceptedCreation);
        if (remainingDays > 0) Assert.Equal(rig.Clock.Now.AddDays(remainingDays), policy.NextAllowedAt);
    }

    [Fact]
    public async Task UnacceptedRemovalNeverCreatesCooldown()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post();
        var confirmation = rig.Engine.PrepareRemoval("1", "100", "200");
        await rig.Engine.ConfirmRemovalAsync("1", "100", "200", confirmation.Token!, default);
        Assert.Null(rig.State.Authors["200"].LastAcceptedCreation);
        await rig.Post("101"); Assert.Contains("accepted", await rig.Ack("101"));
    }

    [Fact]
    public async Task AllForumsShareOneSlot_EvenAfterThirtyDaysOrNaturalArchive()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack();
        rig.Discord.Replies.Add("100"); rig.Clock.Advance(TimeSpan.FromDays(40));
        rig.Discord.Threads["100"] = rig.Discord.Threads["100"] with { Archived = true };
        var second = await rig.Post("101", forum: "14"); await rig.Ack("101");
        Assert.Null(second.AcceptedAt); Assert.NotNull(second.AcknowledgedAt);
        Assert.Equal("100", RecruitmentPolicy.Evaluate(rig.State, second, rig.Clock.Now, 30).BlockingListingId);
    }

    [Fact]
    public async Task EarlierPendingAttemptWinsAcrossForums_AndAcknowledgedLoserCloses()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        await rig.Post(); var second = await rig.Post("101", forum: "12");
        await rig.Ack("101"); Assert.Null(second.AcceptedAt);
        await rig.Ack(); Assert.NotNull(rig.State.Listings["100"].AcceptedAt);
        rig.Clock.Advance(TimeSpan.FromMinutes(31)); await rig.Sweep();
        Assert.Equal(ListingStatus.Closed, second.Status);
        Assert.DoesNotContain(("101", "delete"), rig.Discord.Actions);
    }

    [Fact]
    public async Task AcknowledgedAttemptAcceptsWhenPendingBlockerDisappears()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post();
        var second = await rig.Post("101", forum: "13"); await rig.Ack("101");
        rig.Discord.Threads.Remove("100"); await rig.Sweep();
        Assert.NotNull(second.AcceptedAt);
    }

    [Fact]
    public async Task TimeoutPersistsIntentAndStaffEvidenceBeforeDelete_WithoutCooldown()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        rig.Discord.BeforeDelete = () =>
        {
            Assert.NotNull(post.PendingAction); Assert.NotNull(post.StaffMessageId);
            var json = File.ReadAllText(Path.Combine(rig.Root, "recruitment/recruitment-state.json"));
            Assert.Contains("Guidelines acknowledgement timed out", json);
            Assert.Contains(post.PendingAction!.Id, json);
        };
        rig.Clock.Advance(TimeSpan.FromMinutes(31)); await rig.Sweep();
        Assert.Equal(ListingStatus.Deleted, post.Status);
        Assert.Equal(1, rig.State.Authors["200"].ConsecutiveTimeouts);
        Assert.Null(rig.State.Authors["200"].LastAcceptedCreation);
        await rig.Sweep(); Assert.Single(rig.Discord.Actions);
    }

    [Fact]
    public async Task FailedStaffDeliveryPreventsDeletion()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        rig.Clock.Advance(TimeSpan.FromMinutes(31)); rig.Discord.FailStaff = true; await rig.Sweep();
        Assert.Empty(rig.Discord.Actions); Assert.Equal(ListingStatus.Open, post.Status);
    }

    [Fact]
    public async Task LostDeleteResponseDoesNotInventDeletionActorOrTimeoutStreak()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post();
        rig.Discord.LoseNextDeleteResponse = true; rig.Clock.Advance(TimeSpan.FromMinutes(31));
        await rig.Sweep(); await rig.Restart();
        Assert.Equal(ListingStatus.Deleted, rig.State.Listings["100"].Status);
        Assert.Equal(0, rig.State.Authors["200"].ConsecutiveTimeouts);
        Assert.Single(rig.Discord.Actions);
    }

    [Fact]
    public async Task AmbiguousSendRecoversOneMessagePerDestination()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); rig.Discord.LoseNextSendResponse = true;
        await rig.Post(); await rig.Restart(); await rig.Sweep();
        Assert.Equal(2, rig.Discord.Sends);
        Assert.Equal(2, rig.Discord.Messages.Count);
        Assert.NotNull(rig.State.Listings["100"].Deadline);
    }

    [Fact]
    public async Task RestartGivesPendingAttemptFullWindow_AndHoldsIncompleteReplyHistory()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post();
        rig.Clock.Advance(TimeSpan.FromHours(3)); await rig.Restart();
        var post = rig.State.Listings["100"];
        Assert.Equal(rig.Clock.Now.AddMinutes(30), post.Deadline);
        Assert.False(post.ResponseCoverageComplete); Assert.Empty(rig.Discord.Actions);
    }

    [Fact]
    public async Task UnansweredListingClosesOnlyWithContinuousEvidence()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Ack();
        rig.Clock.Advance(TimeSpan.FromDays(31)); await rig.Sweep();
        Assert.Equal(ListingStatus.Closed, post.Status); Assert.Contains(("100", "close"), rig.Discord.Actions);
    }

    [Fact]
    public async Task KnownReplySurvivesDeletionAndRestart()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack();
        rig.Engine.ObserveReply(new ReplyObservation("100", "333", false, false, false));
        await rig.Restart(); rig.Clock.Advance(TimeSpan.FromDays(31)); await rig.Sweep();
        Assert.True(rig.State.Listings["100"].HasQualifyingReply); Assert.Empty(rig.Discord.Actions);
    }

    [Theory]
    [InlineData("200", false, false, false)]
    [InlineData("333", true, false, false)]
    [InlineData("333", false, true, false)]
    [InlineData("333", false, false, true)]
    public async Task OwnerBotWebhookAndModeratorDoNotCountAsCommunityReply(string author, bool bot, bool webhook, bool staff)
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        rig.Engine.ObserveReply(new ReplyObservation(post.Id, author, bot, webhook, staff));
        Assert.False(post.HasQualifyingReply);
    }

    [Fact]
    public async Task UnknownRolesHoldUnansweredClosure()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Ack();
        rig.Engine.ObserveReply(new ReplyObservation(post.Id, "333", false, false, null));
        rig.Clock.Advance(TimeSpan.FromDays(31)); await rig.Sweep();
        Assert.False(post.ResponseCoverageComplete); Assert.Equal(ListingStatus.Open, post.Status);
    }

    [Fact]
    public async Task AccessFailureDoesNotFreeAcceptedSlot()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack();
        rig.Discord.UnknownThreads.Add("100"); rig.Clock.Advance(TimeSpan.FromDays(40));
        var second = await rig.Post("101", forum: "14"); await rig.Ack("101");
        Assert.Null(second.AcceptedAt); Assert.True(rig.State.Listings["100"].AvailabilityUnknown);
    }

    [Fact]
    public async Task IncompleteInventoryPreventsAcceptanceAndAutomaticActions()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        rig.Discord.InventoryComplete = false; await rig.Ack(); rig.Clock.Advance(TimeSpan.FromHours(1)); await rig.Sweep();
        Assert.Null(post.AcceptedAt); Assert.Empty(rig.Discord.Actions);
    }

    [Fact]
    public async Task ReferenceDesignationRemovesProvisionalAdvisoryAndReservation()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var reference = await rig.Post();
        await rig.Staff("reference");
        Assert.Equal(ListingStatus.Reference, reference.Status); Assert.Null(reference.PublicMessageId);
        Assert.Null(rig.State.Authors["200"].LastAcceptedCreation);
        await rig.Post("101"); Assert.Contains("accepted", await rig.Ack("101"));
    }

    [Fact]
    public async Task PinProtectsFromTimeoutButDoesNotGrantSecondSlot()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var first = await rig.Post();
        rig.Discord.Threads["100"] = rig.Discord.Threads["100"] with { Pinned = true };
        rig.Clock.Advance(TimeSpan.FromHours(1)); await rig.Sweep();
        Assert.Equal(ListingStatus.Open, first.Status);
        var second = await rig.Post("101"); await rig.Ack("101"); Assert.Null(second.AcceptedAt);
    }

    [Fact]
    public async Task OwnerControlsAndSingleUseRemovalAreBoundToGuildThreadOwnerAndVersion()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        Assert.Null(rig.Engine.PrepareRemoval("2", post.Id, "200").Token);
        Assert.Null(rig.Engine.PrepareRemoval("1", post.Id, "201").Token);
        var token = rig.Engine.PrepareRemoval("1", post.Id, "200").Token!;
        Assert.Contains("does not belong", await rig.Engine.ConfirmRemovalAsync("1", post.Id, "201", token, default));
        await rig.Ack();
        Assert.Contains("listing changed", await rig.Engine.ConfirmRemovalAsync("1", post.Id, "200", token, default));
        Assert.Contains("does not belong", await rig.Engine.ConfirmRemovalAsync("1", post.Id, "200", token, default));
        Assert.Empty(rig.Discord.Actions);
    }

    [Fact]
    public async Task WrongCodeIsRateLimited_AndDoesNotIncreaseTimeoutStreak()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post();
        Assert.Contains("does not match", await rig.Ack(code: "00000"));
        Assert.Contains("wait", await rig.Ack()); Assert.Equal(0, rig.State.Authors["200"].ConsecutiveTimeouts);
        rig.Clock.Advance(TimeSpan.FromSeconds(5)); Assert.Contains("accepted", await rig.Ack());
    }

    [Fact]
    public async Task ThirdConfirmedTimeoutAlertsOnce_RealAcknowledgementResetsStreak()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        for (var i = 0; i < 3; i++) { await rig.Post((100 + i).ToString()); rig.Clock.Advance(TimeSpan.FromMinutes(31)); await rig.Sweep(); }
        Assert.Equal(3, rig.State.Authors["200"].ConsecutiveTimeouts); Assert.True(rig.State.Authors["200"].TimeoutAlerted);
        Assert.Contains("Moderator attention", rig.Discord.Messages["99:recruitment:102:staff"].Message.Content);
        await rig.Post("103"); await rig.Ack("103");
        Assert.Equal(0, rig.State.Authors["200"].ConsecutiveTimeouts);
    }

    [Fact]
    public async Task StaffAuthorizationIsEnforcedAtExecution_AndWaiverDoesNotFreeSlot()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack();
        Assert.Contains("Moderator access", await rig.Staff("close", actor: "201"));
        await rig.Staff("waive-cooldown", "200");
        Assert.Null(rig.State.Authors["200"].LastAcceptedCreation);
        var second = await rig.Post("101"); await rig.Ack("101"); Assert.Null(second.AcceptedAt);
    }

    [Fact]
    public async Task ReopenChecksCapacityAndSuppressesImmediateRepeatClosure()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack();
        rig.Clock.Advance(TimeSpan.FromDays(31)); await rig.Sweep(); await rig.Staff("reopen"); await rig.Sweep();
        Assert.Equal(ListingStatus.Open, rig.State.Listings["100"].Status);
        Assert.True(rig.State.Listings["100"].SuppressUnansweredClose);
    }
}
