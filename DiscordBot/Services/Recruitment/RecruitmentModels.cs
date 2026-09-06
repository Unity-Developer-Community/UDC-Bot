using System.Text.Json.Serialization;

namespace DiscordBot.Services.Recruitment;

public enum RecruitmentAcknowledgement { NotPrompted, Pending, Passed, TimedOut, Cancelled }
public enum RecruitmentLifecycle { Open, Closed, Deleted, Missing }
public enum RecruitmentCloseReason { OwnerClosed, Unanswered, GuidelineTimeout, Ineligible, OwnerRemoved, Moderator }
public enum RecruitmentEligibilityKind { Eligible, ActiveListing, PendingListing, Cooldown, ReviewRequired, Terminal }
public enum RecruitmentActionKind { None, Delete, LockArchive }
public enum RecruitmentActivity { Unknown, NoneRecorded, Recorded }
public enum RecruitmentPaymentSignal { Unknown, NotApplicable, Missing, Ambiguous, Concrete }

public sealed class RecruitmentStateDocument
{
    public const int CurrentSchemaVersion = 2;
    [JsonRequired] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [JsonRequired] public ulong GuildId { get; set; }
    [JsonRequired] public long Revision { get; set; }
    [JsonRequired] public DateTimeOffset EnrolledAtUtc { get; set; }
    [JsonRequired] public Dictionary<ulong, RecruitmentPostRecord> Posts { get; set; } = [];
    [JsonRequired] public Dictionary<ulong, RecruitmentAuthorRecord> Authors { get; set; } = [];
    public Dictionary<ulong, RecruitmentForumObservation> Forums { get; set; } = [];
    public DateTimeOffset? LastGatewayGapAtUtc { get; set; }
    public long DroppedObservationEvents { get; set; }
}

public sealed class RecruitmentAuthorRecord
{
    [JsonRequired] public ulong UserId { get; set; }
    public DateTimeOffset? FirstAttemptAtUtc { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public int ConsecutiveTimeouts { get; set; }
    public Dictionary<RecruitmentListingGroup, RecruitmentGroupHistory> Groups { get; set; } = [];
}

public sealed class RecruitmentGroupHistory
{
    public DateTimeOffset? LastAcceptedCreatedAtUtc { get; set; }
    public DateTimeOffset? LastAcceptedDeletedAtUtc { get; set; }
    public bool RequiresReview { get; set; }
}

public sealed class RecruitmentPostRecord
{
    [JsonRequired] public ulong ThreadId { get; set; }
    [JsonRequired] public ulong ParentChannelId { get; set; }
    [JsonRequired] public ulong AuthorId { get; set; }
    [JsonRequired] public RecruitmentForumKind Forum { get; set; }
    [JsonRequired] public DateTimeOffset CreatedAtUtc { get; set; }
    [JsonRequired] public DateTimeOffset FirstSeenAtUtc { get; set; }
    public string Title { get; set; } = string.Empty;
    public ulong[] AppliedTagIds { get; set; } = [];
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public RecruitmentAcknowledgement Acknowledgement { get; set; }
    public DateTimeOffset? PromptedAtUtc { get; set; }
    public DateTimeOffset? ChallengeDeadlineUtc { get; set; }
    public string[] AcceptedCodes { get; set; } = [];
    // Set by the future coordinator only after a usable prompt and healthy reconciliation.
    public bool ChallengeEnforceable { get; set; }
    public bool EnforcementEnrolled { get; set; }
    public RecruitmentLifecycle Lifecycle { get; set; }
    public RecruitmentCloseReason? CloseReason { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public DateTimeOffset? DeletedObservedAtUtc { get; set; }
    public bool DeletionTimeUncertain { get; set; }
    public DateTimeOffset? FirstQualifyingResponseAtUtc { get; set; }
    public DateTimeOffset? ResponsesCheckedThroughUtc { get; set; }
    public bool RequiresReview { get; set; }
    public bool IsPinned { get; set; }
    public bool IsExempt { get; set; }
    public bool ClosedRequested { get; set; }
    public RecruitmentActivity Activity { get; set; }
    public RecruitmentPaymentSignal Payment { get; set; }
    public ulong? AdvisoryMessageId { get; set; }
    public ulong? FeedMessageId { get; set; }
    public RecruitmentPostObservation Observation { get; set; } = new();
}

public sealed class RecruitmentForumObservation
{
    public DateTimeOffset? ActiveCheckedAtUtc { get; set; }
    public DateTimeOffset? ArchiveBeforeUtc { get; set; }
    public DateTimeOffset? ArchiveCompletedAtUtc { get; set; }
    public bool ArchiveRescanRequired { get; set; }
    public string? Error { get; set; }
}

/// <summary>Evidence and resumable read/feed work; never acknowledgement or penalty state.</summary>
public sealed class RecruitmentPostObservation
{
    public bool Imported { get; set; }
    public bool Archived { get; set; }
    public bool Locked { get; set; }
    public bool HasClosedTag { get; set; }
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public DateTimeOffset? NextCheckAtUtc { get; set; }
    public ulong HistoryAfterId { get; set; }
    public bool HistoryUncertain { get; set; }
    public string? StarterHash { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? JoinedAtUtc { get; set; }
    public ulong FeedChannelId { get; set; }
    public string? FeedHash { get; set; }
    public DateTimeOffset? FeedSendRequestedAtUtc { get; set; }
    public ulong? FeedSearchBeforeId { get; set; }
    public DateTimeOffset? FeedRetryAtUtc { get; set; }
    public string? FeedError { get; set; }
}

public sealed record RecruitmentEligibility(
    RecruitmentEligibilityKind Kind,
    DateTimeOffset? NextAllowedAtUtc,
    ulong? BlockingThreadId,
    IReadOnlyList<ulong> RecentOtherForumThreadIds)
{
    public bool IsEligible => Kind == RecruitmentEligibilityKind.Eligible;
    public string? PlacementReminder => RecentOtherForumThreadIds.Count == 0 ? null :
        "You've recently posted in another recruitment forum. Please double-check that each listing is in the right place: recruiting seeks people; for-hire offers your services.";
}

public sealed record RecruitmentAction(RecruitmentActionKind Kind, RecruitmentCloseReason? Reason)
{
    public static RecruitmentAction None { get; } = new(RecruitmentActionKind.None, null);
}

public sealed record RecruitmentResponse(
    ulong AuthorId, DateTimeOffset CreatedAtUtc, bool IsBot, bool IsWebhook,
    bool? IsModerator, bool? IsAdministrator, bool IsUserMessage = true);
