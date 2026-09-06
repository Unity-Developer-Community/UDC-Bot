using DiscordBot.Services.Recruitment.Policy;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Settings.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static DiscordBot.Tests.Recruitment.RecruitmentTestData;

namespace DiscordBot.Tests.Recruitment;

[TestClass]
public sealed class RecruitmentPolicyTests
{
    [TestMethod]
    public void OtherGroup_IsAllowedWithPlacementReminder()
    {
        var prior = Post(1, created: Now.AddDays(-1), accepted: true);
        var candidate = Post(2, ForumKind.PaidForHire);
        var state = State(prior, candidate);
        var result = Policy().TryAccept(state, 2);
        Assert.IsTrue(result.IsEligible);
        Assert.IsNotNull(candidate.AcceptedAtUtc);
        CollectionAssert.AreEqual(new ulong[] { 1 }, result.RecentOtherForumThreadIds.ToArray());
        StringAssert.Contains(result.PlacementReminder!, "double-check");
    }

    [TestMethod]
    public void SameGroup_PaidAndHobbyShareCapacity_EvenWhenOldAndNaturallyArchived()
    {
        var prior = Post(1, created: Now.AddDays(-90), accepted: true);
        // Logical Open persists independently of Discord's natural archive flag.
        var result = Policy().EvaluateEligibility(State(prior, Post(2, ForumKind.HobbyRecruiting)), 2);
        Assert.AreEqual(EligibilityKind.ActiveListing, result.Kind);
        Assert.AreEqual(1ul, result.BlockingThreadId);
        Assert.IsNull(result.PlacementReminder);
    }

    [TestMethod]
    public void PendingReservations_AreDeterministic_AndDoNotBlockTheOtherGroup()
    {
        var first = Post(1); first.Acknowledgement = AcknowledgementStatus.NotPrompted;
        var state = State(first, Post(2, ForumKind.HobbyRecruiting), Post(3, ForumKind.HobbyForHire));
        Assert.AreEqual(EligibilityKind.PendingListing, Policy().EvaluateEligibility(state, 2).Kind);
        Assert.IsTrue(Policy().EvaluateEligibility(state, 3).IsEligible);
        first.Lifecycle = ListingLifecycle.Deleted;
        first.Acknowledgement = AcknowledgementStatus.Cancelled;
        Assert.IsTrue(Policy().EvaluateEligibility(state, 2).IsEligible);
    }

    [TestMethod]
    public void ClosedAcceptedListing_UsesCreationBoundary_AndDeletionRestartsOnlyItsGroup()
    {
        var old = Post(1, created: Now.AddDays(-30), accepted: true);
        old.Lifecycle = ListingLifecycle.Closed;
        var state = State(old, Post(2), Post(3, ForumKind.HobbyForHire));
        Assert.IsTrue(Policy().EvaluateEligibility(state, 2).IsEligible);
        old.CreatedAtUtc = Now.AddDays(-30).AddTicks(1);
        Assert.AreEqual(EligibilityKind.Cooldown, Policy().EvaluateEligibility(state, 2).Kind);
        old.CreatedAtUtc = Now.AddDays(-60);
        old.DeletedObservedAtUtc = Now.AddDays(-1);
        old.Lifecycle = ListingLifecycle.Deleted;
        var blocked = Policy().EvaluateEligibility(state, 2);
        Assert.AreEqual(Now.AddDays(29), blocked.NextAllowedAtUtc);
        Assert.IsTrue(Policy().EvaluateEligibility(state, 3).IsEligible);
    }

    [TestMethod]
    public void CompactedHistory_PreservesCooldown_AndUnknownHistoryHoldsOnlyItsGroup()
    {
        var state = State(Post(2), Post(3, ForumKind.PaidForHire));
        state.Authors[123] = new AuthorRecord
        {
            UserId = 123,
            Groups = new() { [ListingGroup.Recruiting] = new() { LastAcceptedCreatedAtUtc = Now.AddDays(-2) } }
        };
        Assert.AreEqual(EligibilityKind.Cooldown, Policy().EvaluateEligibility(state, 2).Kind);
        state.Authors[123].Groups[ListingGroup.Recruiting].RequiresReview = true;
        Assert.AreEqual(EligibilityKind.ReviewRequired, Policy().EvaluateEligibility(state, 2).Kind);
        Assert.IsTrue(Policy().EvaluateEligibility(state, 3).IsEligible);
    }

    [TestMethod]
    public void UncertainDeletion_DoesNotBecomeAnAutomaticPenalty()
    {
        var old = Post(1, created: Now.AddDays(-60), accepted: true);
        old.Lifecycle = ListingLifecycle.Missing; old.DeletionTimeUncertain = true;
        var candidate = Post(2); candidate.ChallengeDeadlineUtc = Now.AddMinutes(-1);
        var state = State(old, candidate);
        Assert.AreEqual(EligibilityKind.ReviewRequired, Policy().EvaluateEligibility(state, 2).Kind);
        Assert.AreEqual(ActionKind.None, Policy().EvaluateAction(state, 2).Kind);
    }

    [TestMethod]
    public void AcceptanceRequiresAcknowledgement_AndIsIdempotent()
    {
        var post = Post(); post.Acknowledgement = AcknowledgementStatus.NotPrompted;
        var state = State(post); var policy = Policy();
        policy.TryAccept(state, 10);
        Assert.IsNull(post.AcceptedAtUtc);
        post.Acknowledgement = AcknowledgementStatus.Passed;
        Assert.IsTrue(policy.TryAccept(state, 10).IsEligible);
        Assert.IsTrue(policy.TryAccept(state, 10).IsEligible);
        Assert.AreEqual(Now, post.AcceptedAtUtc);
        Assert.AreEqual(Now, state.Authors[123].Groups[ListingGroup.Recruiting].LastAcceptedCreatedAtUtc);
    }

    [TestMethod]
    [DataRow(RecruitmentMode.Observe)]
    [DataRow(RecruitmentMode.Advisory)]
    public void NonEnforcementModes_NeverProposeAutomaticActions(RecruitmentMode mode)
    {
        var options = Options(); options.Mode = mode;
        var post = PendingTimeout();
        Assert.AreEqual(ActionKind.None, Policy(options).EvaluateAction(State(post), post.ThreadId).Kind);
    }

    [TestMethod]
    public void TimeoutRequiresDeliveredHealthyEnrolledChallenge_AndRespectsGate()
    {
        var post = PendingTimeout(); var state = State(post);
        Assert.AreEqual(ActionKind.Delete, Policy().EvaluateAction(state, 10).Kind);
        post.ChallengeEnforceable = false;
        Assert.AreEqual(ActionKind.None, Policy().EvaluateAction(state, 10).Kind);
        post.ChallengeEnforceable = true; post.EnforcementEnrolled = false;
        Assert.AreEqual(ActionKind.None, Policy().EvaluateAction(state, 10).Kind);
        post.EnforcementEnrolled = true;
        var options = Options(); options.EnforceGuidelineTimeouts = false;
        Assert.AreEqual(ActionKind.None, Policy(options).EvaluateAction(state, 10).Kind);
        options.EnforceGuidelineTimeouts = true; options.Enabled = false;
        Assert.AreEqual(ActionKind.None, Policy(options).EvaluateAction(state, 10).Kind);
    }

    [TestMethod]
    public void WithdrawalPinExemptionAndUnknownEvidence_PreventTimeoutDeletion()
    {
        foreach (var edit in new Action<PostRecord>[]
                 { p => p.IsPinned = true, p => p.IsExempt = true, p => p.RequiresReview = true,
                     p => p.ClosedRequested = true, p => p.Lifecycle = ListingLifecycle.Deleted })
        {
            var post = PendingTimeout(); edit(post);
            Assert.AreNotEqual(ActionKind.Delete, Policy().EvaluateAction(State(post), 10).Kind);
        }
    }

    [TestMethod]
    public void UnansweredClosure_RequiresCompleteEvidence_AndIsIndependentOfTimeoutGate()
    {
        var post = Post(created: Now.AddDays(-30), accepted: true);
        var state = State(post); var options = Options(); options.EnforceGuidelineTimeouts = false;
        Assert.AreEqual(ActionKind.None, Policy(options).EvaluateAction(state, 10).Kind);
        post.ResponsesCheckedThroughUtc = Now;
        Assert.AreEqual(CloseReason.Unanswered, Policy(options).EvaluateAction(state, 10).Reason);
        post.FirstQualifyingResponseAtUtc = Now.AddDays(-29);
        Assert.AreEqual(ActionKind.None, Policy(options).EvaluateAction(state, 10).Kind);
        post.ClosedRequested = true;
        Assert.AreEqual(CloseReason.OwnerClosed, Policy(options).EvaluateAction(state, 10).Reason);
    }

    [TestMethod]
    public void CodeValidation_AcceptsBothPublishedVersions_ButOnlyWithinPromptWindow()
    {
        var post = PendingTimeout(); post.ChallengeDeadlineUtc = Now.AddMinutes(1);
        post.AcceptedCodes = ["K7M4Q", "T8J5R"];
        Assert.IsTrue(Policy().IsCorrectCode(post, " k7m4q "));
        Assert.IsTrue(Policy().IsCorrectCode(post, "T8J5R"));
        Assert.IsFalse(Policy().IsCorrectCode(post, "ABCDE"));
        post.ClosedRequested = true;
        Assert.IsFalse(Policy().IsCorrectCode(post, "K7M4Q"));
        post.ClosedRequested = false;
        post.ChallengeDeadlineUtc = Now;
        Assert.IsFalse(Policy().IsCorrectCode(post, "K7M4Q"));
        Assert.AreEqual(Now.AddMinutes(30), Policy().ChallengeDeadline(Now));
    }

    [TestMethod]
    public void Responses_ExcludeStaffBotsOwner_AndPreserveUnknownRoleStatus()
    {
        var post = Post(created: Now.AddDays(-1));
        var human = new ReplyEvidence(456, Now, false, false, false, false);
        Assert.AreEqual(true, PolicyEvaluator.IsQualifyingResponse(post, human));
        foreach (var response in new[] { human with { AuthorId = 123 }, human with { IsBot = true },
                     human with { IsWebhook = true }, human with { IsModerator = true },
                     human with { IsAdministrator = true }, human with { IsUserMessage = false } })
            Assert.AreEqual(false, PolicyEvaluator.IsQualifyingResponse(post, response));
        Assert.IsNull(PolicyEvaluator.IsQualifyingResponse(post, human with { IsModerator = null }));
    }

    [TestMethod]
    public void Facts_UseCalendarBoundaries_AndNeverTurnUnknownIntoZero()
    {
        Assert.AreEqual("3–6 months", Policy().ServerTenure(Now.AddMonths(-3)));
        Assert.AreEqual("< 3 months", Policy().ServerTenure(Now.AddMonths(-3).AddTicks(1)));
        Assert.AreEqual("12+ months", Policy().AccountAge(Now.AddMonths(-12)));
        Assert.AreEqual("unknown", Policy().AccountAge(null));
        Assert.AreEqual(ActivityStatus.Unknown, PolicyEvaluator.Activity(null, 0, 0));
        Assert.AreEqual(ActivityStatus.NoneRecorded, PolicyEvaluator.Activity(0, 0, 0));
        Assert.AreEqual(ActivityStatus.Recorded, PolicyEvaluator.Activity(0, 0, 1));
    }

    private static PostRecord PendingTimeout()
    {
        var post = Post(created: Now.AddHours(-1));
        post.Acknowledgement = AcknowledgementStatus.Pending;
        post.PromptedAtUtc = Now.AddMinutes(-30); post.ChallengeDeadlineUtc = Now;
        post.AcceptedCodes = ["K7M4Q"]; post.ChallengeEnforceable = true;
        return post;
    }
}
