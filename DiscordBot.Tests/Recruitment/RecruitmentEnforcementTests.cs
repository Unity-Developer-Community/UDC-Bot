using DiscordBot.Services.Recruitment.Policy;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Settings.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Recruitment;

[TestClass]
public sealed class RecruitmentEnforcementTests
{
    internal static async Task StartEnforce(RecruitmentAdvisoryFixture f)
    {
        f.Options.Mode = RecruitmentMode.Enforce;
        await f.InitializeAsync();
        await f.Store.UpdateAsync(state =>
        {
            state.LastMode = RecruitmentMode.Enforce;
            state.EnforcementStartedAtUtc = f.Time.Now.AddDays(-1);
            state.Posts[10].EnforcementEnrolled = true;
            return true;
        });
        await f.Coordinator.TickAsync(default);
    }

    [TestMethod]
    public async Task TimeoutAfterLostResponse_CountsOnceAfterRecovery_AndAlertUsesTheSameFeedEntry()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        await f.Store.UpdateAsync(state => { state.Authors[123] = new() { UserId = 123, ConsecutiveTimeouts = 2 }; return true; });
        f.Time.Advance(TimeSpan.FromMinutes(31)); f.Discord.FailActionAfterWrite = true;
        await f.Enforcement.TickAsync(default);
        Assert.AreEqual(1, f.Discord.Actions);
        Assert.AreEqual(2, (await f.State()).Authors[123].ConsecutiveTimeouts);
        await f.Store.ReleaseAsync();
        await f.Lifecycle.RecoverAsync(10, default);
        await f.Lifecycle.RecoverAsync(10, default);
        Assert.AreEqual(3, (await f.State()).Authors[123].ConsecutiveTimeouts);
        Assert.AreEqual(AcknowledgementStatus.TimedOut, (await f.Post()).Acknowledgement);
        Assert.IsTrue(await f.Observations.FlushFeedAsync(10, default));
        Assert.IsTrue(await f.Observations.FlushFeedAsync(10, default));
        Assert.AreEqual(1, f.Observer.FeedSends);
        StringAssert.Contains(f.Observer.Feed.Values.Single(), "3 consecutive confirmed timeouts");
    }

    [TestMethod]
    [DataRow("gate")]
    [DataRow("audit")]
    [DataRow("prompt")]
    [DataRow("guidelines")]
    [DataRow("pinned")]
    [DataRow("owner-unknown")]
    [DataRow("unenrolled")]
    [DataRow("gap")]
    public async Task TimeoutRequiresEverySafetyPrerequisite(string missing)
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        f.Time.Advance(TimeSpan.FromMinutes(31));
        if (missing == "gate") f.Options.EnforceGuidelineTimeouts = false;
        if (missing == "audit") f.Observer.FailFeed = true;
        if (missing == "prompt") f.Discord.Messages.Clear();
        if (missing == "guidelines") f.Discord.Forums[101] = f.Discord.Forums[101] with { Topic = "Staff editing" };
        if (missing == "pinned") f.Discord.Post = f.Discord.Post! with { Pinned = true };
        if (missing == "owner-unknown") f.Discord.Post = f.Discord.Post! with { OrdinaryAuthor = null };
        if (missing == "unenrolled") await f.Store.UpdateAsync(state => { state.Posts[10].EnforcementEnrolled = false; return true; });
        if (missing == "gap") await f.Coordinator.RecordGapAsync(default);
        await f.Enforcement.TickAsync(default);
        Assert.AreEqual(0, f.Discord.Actions);
        Assert.IsTrue((await f.State()).Authors.Values.All(author => author.ConsecutiveTimeouts == 0));
    }

    [TestMethod]
    public async Task RecoveredOutage_GrantsAFullWindow_AndCannotReuseTheExpiredModal()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        string old = await f.Generation();
        f.Time.Advance(TimeSpan.FromHours(2)); await f.Coordinator.RecordGapAsync(default);
        await f.Coordinator.TickAsync(default);
        Assert.AreEqual(f.Time.Now.AddMinutes(30), (await f.Post()).ChallengeDeadlineUtc);
        await f.Enforcement.TickAsync(default);
        Assert.AreEqual(0, f.Discord.Actions);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owners.SubmitCodeAsync(f.Owner, old, "ABCDE", default));
    }

    [TestMethod]
    public async Task SuccessfulEnforcedAnswer_ResetsTimeouts_ButAcceptanceWaitsForGraceDeadline()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        await f.Store.UpdateAsync(state => { state.Authors[123] = new() { UserId = 123, ConsecutiveTimeouts = 2 }; return true; });
        await f.Owners.SubmitCodeAsync(f.Owner, await f.Generation(), await f.Code(), default);
        await f.Enforcement.TickAsync(default);
        Assert.IsNull((await f.Post()).AcceptedAtUtc);
        Assert.AreEqual(0, (await f.State()).Authors[123].ConsecutiveTimeouts);
        f.Time.Advance(TimeSpan.FromMinutes(31)); await f.Enforcement.TickAsync(default);
        Assert.AreEqual(f.Time.Now, (await f.Post()).AcceptedAtUtc);
        Assert.AreEqual(0, f.Discord.Actions);
    }

    [TestMethod]
    public async Task TimeoutGateIsIndependentOfLifecycleAndListingGates()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        f.Options.EnforceLifecycleClosures = false; f.Options.EnforceListingLimits = false;
        f.Time.Advance(TimeSpan.FromMinutes(31)); await f.Enforcement.TickAsync(default);
        Assert.AreEqual(1, f.Discord.Actions);
        Assert.AreEqual(ListingLifecycle.Deleted, (await f.Post()).Lifecycle);
    }

    [TestMethod]
    public async Task SameGroupAcceptsOne_OtherGroupHasIndependentCapacity()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        foreach (var pair in new[] { (11ul, ForumKind.HobbyRecruiting), (12ul, ForumKind.PaidForHire) })
        {
            var post = RecruitmentTestData.Post(pair.Item1, pair.Item2);
            post.Acknowledgement = AcknowledgementStatus.NotPrompted;
            await f.Store.UpdateAsync(state => { state.Posts[post.ThreadId] = post; return true; });
            f.Discord.Posts[post.ThreadId] = new(post.ThreadId, post.ParentChannelId, post.AuthorId, false, false, false, true, []);
        }
        await f.Coordinator.TickAsync(default);
        foreach (var post in (await f.State()).Posts.Values)
        {
            string code = (await f.State()).Forums[post.ParentChannelId].Publication.Confirmed!.Code;
            await f.Owners.SubmitCodeAsync(new(1, post.ThreadId, post.AuthorId), post.Advisory.Generation, code, default);
        }
        f.Time.Advance(TimeSpan.FromMinutes(31)); await f.Enforcement.TickAsync(default);
        var state = await f.State();
        Assert.IsNotNull(state.Posts[10].AcceptedAtUtc); Assert.IsNotNull(state.Posts[12].AcceptedAtUtc);
        Assert.IsNull(state.Posts[11].AcceptedAtUtc);
        Assert.AreEqual(ListingLifecycle.Closed, state.Posts[11].Lifecycle);
        Assert.AreEqual(CloseReason.Ineligible, state.Posts[11].CloseReason);
    }

    [TestMethod]
    [DataRow("clear", true)]
    [DataRow("archived", true)]
    [DataRow("qualifying", false)]
    [DataRow("unknown", false)]
    [DataRow("incomplete", false)]
    [DataRow("gate", false)]
    public async Task UnansweredClosureRequiresVerifiedAbsence_AndNaturalArchiveKeepsLogicalCapacity(string evidence, bool close)
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        await f.Store.UpdateAsync(state =>
        {
            state.Posts[10].Acknowledgement = AcknowledgementStatus.Passed;
            state.Posts[10].AcceptedAtUtc = f.Time.Now;
            if (evidence == "unknown") state.Posts[10].Observation.HistoryUncertain = true;
            return true;
        });
        if (evidence == "qualifying") f.Observer.Replies = new([new(20, new(456, f.Time.Now.AddSeconds(1), false, false, false, false), null)], true);
        if (evidence == "incomplete") f.Observer.Replies = new([], false);
        if (evidence == "archived") f.Discord.Post = f.Discord.Post! with { Archived = true };
        if (evidence == "gate") f.Options.EnforceLifecycleClosures = false;
        f.Time.Advance(TimeSpan.FromDays(30)); await f.Enforcement.TickAsync(default);
        Assert.AreEqual(close ? ListingLifecycle.Closed : ListingLifecycle.Open, (await f.Post()).Lifecycle);
        Assert.AreEqual(close ? 1 : 0, f.Discord.Actions);
    }

    [TestMethod]
    public async Task ClosedTagWithdrawsAnUnacknowledgedPostWithoutTimeoutAccounting()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        f.Discord.Post = f.Discord.Post! with { Tags = [1101] };
        await f.Coordinator.RefreshPostAsync(10, default);
        f.Time.Advance(TimeSpan.FromMinutes(31)); await f.Enforcement.TickAsync(default);
        Assert.AreEqual(ListingLifecycle.Closed, (await f.Post()).Lifecycle);
        Assert.AreEqual(AcknowledgementStatus.Cancelled, (await f.Post()).Acknowledgement);
        Assert.IsNull((await f.Post()).AcceptedAtUtc);
        Assert.IsTrue((await f.State()).Authors.Values.All(author => author.ConsecutiveTimeouts == 0));
    }

    [TestMethod]
    public async Task ExternalRemovalBeforeDispatch_DoesNotCountAsAnAutomaticTimeout()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await StartEnforce(f);
        await f.Lifecycle.RequestAsync(10, ActionKind.Delete, ActionOrigin.Automatic,
            CloseReason.GuidelineTimeout, 0, "Timeout intent", default);
        f.Discord.Post = null;
        await f.Lifecycle.RecoverAsync(10, default);
        Assert.AreEqual(0, f.Discord.Actions);
        Assert.IsTrue((await f.State()).Authors.Values.All(author => author.ConsecutiveTimeouts == 0));
    }
}
