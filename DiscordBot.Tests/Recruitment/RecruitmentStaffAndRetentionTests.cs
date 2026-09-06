using DiscordBot.Services.Recruitment;
using DiscordBot.Settings.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static DiscordBot.Tests.Recruitment.RecruitmentEnforcementTests;

namespace DiscordBot.Tests.Recruitment;

[TestClass]
public sealed class RecruitmentStaffAndRetentionTests
{
    [TestMethod]
    public async Task WaiverCoversCurrentAnchors_WithoutWaivingCapacityOrFutureDeletions()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        var previous = RecruitmentTestData.Post(20, created: f.Time.Now.AddDays(-1), accepted: true);
        previous.Lifecycle = RecruitmentLifecycle.Closed; previous.ClosedAtUtc = f.Time.Now;
        await f.Store.UpdateAsync(state => { state.Posts[20] = previous; return true; });
        var policy = new RecruitmentPolicyEvaluator(f.Options, f.Time);
        Assert.AreEqual(RecruitmentEligibilityKind.Cooldown, policy.EvaluateEligibility(await f.State(), 10).Kind);
        await f.Staff.WaiveCooldownAsync(10, 999, "Approved current wait waiver", default);
        Assert.IsTrue(policy.EvaluateEligibility(await f.State(), 10).IsEligible);
        await f.Store.UpdateAsync(state => { state.Posts[20].Lifecycle = RecruitmentLifecycle.Open; return true; });
        Assert.AreEqual(RecruitmentEligibilityKind.ActiveListing, policy.EvaluateEligibility(await f.State(), 10).Kind);
        f.Time.Advance(TimeSpan.FromDays(1));
        await f.Store.UpdateAsync(state =>
        {
            state.Posts[20].Lifecycle = RecruitmentLifecycle.Deleted;
            state.Posts[20].DeletedObservedAtUtc = f.Time.Now;
            return true;
        });
        Assert.AreEqual(RecruitmentEligibilityKind.Cooldown, policy.EvaluateEligibility(await f.State(), 10).Kind);
    }

    [TestMethod]
    public async Task ReopenChecksCapacity_ClearsClosed_AndAddsAnExemption()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        await f.Store.UpdateAsync(state =>
        {
            var post = state.Posts[10];
            post.AcceptedAtUtc = f.Time.Now; post.Acknowledgement = RecruitmentAcknowledgement.Passed;
            post.Lifecycle = RecruitmentLifecycle.Closed; post.ClosedAtUtc = f.Time.Now; post.ClosedRequested = true;
            state.Posts[20] = RecruitmentTestData.Post(20, accepted: true);
            return true;
        });
        f.Discord.Post = f.Discord.Post! with { Archived = true, Locked = true, Tags = [1101, 5000] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Staff.ChangeLifecycleAsync(10, RecruitmentActionKind.Reopen, 999, "Owner requested reopening", default));
        await f.Store.UpdateAsync(state => { state.Posts[20].Lifecycle = RecruitmentLifecycle.Closed; return true; });
        await f.Staff.ChangeLifecycleAsync(10, RecruitmentActionKind.Reopen, 999, "Owner requested reopening", default);
        var reopened = await f.Post();
        Assert.IsTrue(reopened.IsExempt); Assert.IsFalse(reopened.ClosedRequested);
        Assert.AreEqual(RecruitmentLifecycle.Open, reopened.Lifecycle);
        CollectionAssert.AreEqual(new ulong[] { 5000 }, f.Discord.Post!.Tags);
        f.Time.Advance(TimeSpan.FromDays(40)); await f.Enforcement.TickAsync(default);
        Assert.AreEqual(1, f.Discord.Actions);
        Assert.AreEqual(1, reopened.Audit.Count);
    }

    [TestMethod]
    public async Task StaffRemovalConfirmationIsActorAndVersionBound_AndDoesNotCountTimeouts()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        var confirmation = await f.Staff.PrepareRemovalAsync(10, 999, "Moderator reviewed prohibited listing", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Staff.ConfirmRemovalAsync(10, 998, confirmation.Token, default));
        await f.Staff.ExemptAsync(10, true, 999, "Hold pending review", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Staff.ConfirmRemovalAsync(10, 999, confirmation.Token, default));
        confirmation = await f.Staff.PrepareRemovalAsync(10, 999, "Review complete; remove listing", default);
        await f.Staff.ConfirmRemovalAsync(10, 999, confirmation.Token, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Staff.ConfirmRemovalAsync(10, 999, confirmation.Token, default));
        Assert.AreEqual(1, f.Discord.Actions);
        Assert.IsTrue((await f.State()).Authors.Values.All(author => author.ConsecutiveTimeouts == 0));
    }

    [TestMethod]
    public async Task ReviewCanAttestEarlierHistory_ButCannotEraseKnownReplies()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        await f.Store.UpdateAsync(state => { state.Posts[10].RequiresReview = true; state.Posts[10].Observation.HistoryUncertain = true; return true; });
        await f.Staff.ReviewAsync(10, RecruitmentResponseReview.NoQualifyingResponses, false, 999, "Inspected the earlier thread history", default);
        Assert.IsFalse((await f.Post()).RequiresReview); Assert.IsFalse((await f.Post()).Observation.HistoryUncertain);
        f.Time.Advance(TimeSpan.FromSeconds(1));
        await f.Staff.ReviewAsync(10, RecruitmentResponseReview.QualifyingResponsePresent, false, 999, "Ordinary applicant reply verified", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Staff.ReviewAsync(10, RecruitmentResponseReview.NoQualifyingResponses, false, 999, "Recheck", default));
        await f.Observations.RecordGapAsync(default);
        Assert.IsTrue((await f.Post()).Observation.HistoryUncertain);
        Assert.IsNotNull((await f.Post()).FirstQualifyingResponseAtUtc);
    }

    [TestMethod]
    public async Task AdoptionIsExplicit_AndUnavailableInAdvisory()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.StartAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Staff.AdoptAsync(10, 999, "Reviewed historical listing", default));
        f.Options.Mode = RecruitmentMode.Enforce;
        await f.Store.UpdateAsync(state => { state.Posts[10].Observation.Imported = true; state.Posts[10].RequiresReview = true; return true; });
        ulong closedId = (await f.State()).Forums[101].Publication.ClosedTagId!.Value;
        f.Discord.Post = f.Discord.Post! with { Tags = [closedId] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Staff.AdoptAsync(10, 999, "Reviewed historical listing", default));
        f.Discord.Post = f.Discord.Post! with { Tags = [] };
        await f.Staff.AdoptAsync(10, 999, "Reviewed historical listing", default);
        var post = await f.Post();
        Assert.IsNotNull(post.AcceptedAtUtc); Assert.IsTrue(post.EnforcementEnrolled);
        Assert.IsFalse(post.Observation.Imported); Assert.IsFalse(post.RequiresReview);
    }

    [TestMethod]
    public async Task MissingResolutionUsesVerifiedDeletionTime_AndStaffCanRequestFreshAcknowledgement()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        f.Time.Advance(TimeSpan.FromHours(1));
        await f.Staff.RestartAcknowledgementAsync(10, 999, "Resolved delivery issue", default);
        await f.Coordinator.RefreshPostAsync(10, default);
        Assert.AreEqual(f.Time.Now.AddMinutes(30), (await f.Post()).ChallengeDeadlineUtc);
        await f.Store.UpdateAsync(state =>
        {
            var post = state.Posts[10]; post.Lifecycle = RecruitmentLifecycle.Missing;
            post.Acknowledgement = RecruitmentAcknowledgement.Passed; post.AcceptedAtUtc = post.CreatedAtUtc;
            post.RequiresReview = true; post.DeletionTimeUncertain = true;
            return true;
        });
        f.Discord.Post = null;
        DateTimeOffset verified = f.Time.Now.AddMinutes(-10);
        await f.Staff.ResolveMissingAsync(10, verified, 999, "Deletion time verified from audit evidence", default);
        Assert.AreEqual(verified, (await f.Post()).DeletedObservedAtUtc);
        Assert.IsFalse((await f.Post()).DeletionTimeUncertain);
        Assert.AreEqual(verified, (await f.State()).Authors[123].Groups[RecruitmentListingGroup.Recruiting].LastAcceptedDeletedAtUtc);
    }

    [TestMethod]
    public async Task RetentionCompactsAtTwelveMonths_AndExpiresIdleHistoryAtTwentyFour()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.InitializeAsync();
        DateTimeOffset closed = f.Time.Now;
        await f.Store.UpdateAsync(state =>
        {
            var post = state.Posts[10]; post.AcceptedAtUtc = closed; post.Acknowledgement = RecruitmentAcknowledgement.Passed;
            post.Lifecycle = RecruitmentLifecycle.Closed; post.ClosedAtUtc = closed;
            return true;
        });
        f.Time.Now = closed.AddMonths(12).AddTicks(-1); await f.Retention.TickAsync(default);
        Assert.AreEqual(1, (await f.State()).Posts.Count);
        f.Time.Now = closed.AddMonths(12);
        await f.Store.UpdateAsync(state => { state.LastRetentionAtUtc = null; return true; });
        await f.Retention.TickAsync(default);
        var compacted = await f.State();
        Assert.AreEqual(0, compacted.Posts.Count);
        Assert.IsTrue(compacted.RetiredThreadIds.Contains(10));
        f.Discord.Post = f.Discord.Post! with { Archived = true, Locked = true };
        await f.Observations.HandleAsync(new(RecruitmentEventKind.Changed, 10, 101), default);
        await f.Observations.TickAsync(default);
        Assert.AreEqual(0, (await f.State()).Posts.Count);
        Assert.AreEqual(closed, compacted.Authors[123].Groups[RecruitmentListingGroup.Recruiting].LastAcceptedCreatedAtUtc);
        f.Time.Now = closed.AddMonths(24); await f.Retention.TickAsync(default);
        Assert.AreEqual(0, (await f.State()).Authors.Count);
        Assert.AreEqual(0, f.Discord.Actions);
    }

    [TestMethod]
    [DataRow("open")]
    [DataRow("review")]
    [DataRow("uncertain")]
    [DataRow("intent")]
    [DataRow("feed")]
    public async Task RetentionKeepsActiveOrUnresolvedDependencies(string dependency)
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.InitializeAsync();
        await f.Store.UpdateAsync(state =>
        {
            var post = state.Posts[10]; post.Lifecycle = RecruitmentLifecycle.Closed; post.ClosedAtUtc = f.Time.Now;
            if (dependency == "open") post.Lifecycle = RecruitmentLifecycle.Open;
            if (dependency == "review") post.RequiresReview = true;
            if (dependency == "uncertain") post.DeletionTimeUncertain = true;
            if (dependency == "feed") post.Observation.FeedError = "Retry pending";
            return true;
        });
        if (dependency == "intent") await f.Lifecycle.RequestAsync(10, RecruitmentActionKind.Delete, RecruitmentActionOrigin.Owner,
            RecruitmentCloseReason.OwnerRemoved, 123, "Confirmed owner removal", default);
        f.Time.Advance(TimeSpan.FromDays(800)); await f.Retention.TickAsync(default);
        Assert.AreEqual(1, (await f.State()).Posts.Count);
    }

    [TestMethod]
    public async Task StoppedLookupDoesNotClaimTheStateWriter()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.InitializeAsync();
        await f.Store.ReleaseAsync();
        StringAssert.Contains(await f.Staff.StatusAsync(10, default), "acknowledgement");
        Assert.IsFalse(f.Store.IsHealthy);
    }
}
