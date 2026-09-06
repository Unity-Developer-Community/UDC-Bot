using System.Text.Json.Serialization;
using DiscordBot.Services.Recruitment.Policy;
using DiscordBot.Settings.Options;

namespace DiscordBot.Services.Recruitment.State;

public enum AcknowledgementStatus { NotPrompted, Pending, Passed, TimedOut, Cancelled }
public enum ListingLifecycle { Open, Closed, Deleted, Missing }
public enum CloseReason { OwnerClosed, Unanswered, GuidelineTimeout, Ineligible, OwnerRemoved, Moderator }
public enum EligibilityKind { Eligible, ActiveListing, PendingListing, Cooldown, ReviewRequired, Terminal }
public enum ActionKind { None, Delete, LockArchive, Reopen }
public enum ActivityStatus { Unknown, NoneRecorded, Recorded }
public enum PaymentSignal { Unknown, NotApplicable, Missing, Ambiguous, Concrete }

public sealed class StateDocument
{
    public const int CurrentSchemaVersion = 1;
    [JsonRequired] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [JsonRequired] public ulong GuildId { get; set; }
    [JsonRequired] public long Revision { get; set; }
    [JsonRequired] public DateTimeOffset EnrolledAtUtc { get; set; }
    [JsonRequired] public Dictionary<ulong, PostRecord> Posts { get; set; } = [];
    [JsonRequired] public Dictionary<ulong, AuthorRecord> Authors { get; set; } = [];
    public Dictionary<ulong, ForumObservation> Forums { get; set; } = [];
    public DateTimeOffset? LastGatewayGapAtUtc { get; set; }
    public long DroppedObservationEvents { get; set; }
    public RecruitmentMode? LastMode { get; set; }
    public DateTimeOffset? EnforcementStartedAtUtc { get; set; }
    public DateTimeOffset? LastRetentionAtUtc { get; set; }
    // Archive scans must not recreate detailed records after retention has removed them.
    public HashSet<ulong> RetiredThreadIds { get; set; } = [];
}

public sealed class AuthorRecord
{
    [JsonRequired] public ulong UserId { get; set; }
    public DateTimeOffset? FirstAttemptAtUtc { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public int ConsecutiveTimeouts { get; set; }
    public DateTimeOffset? LastActivityAtUtc { get; set; }
    public string? TimeoutAlertActionId { get; set; }
    public Dictionary<ListingGroup, GroupHistory> Groups { get; set; } = [];
}

public sealed class GroupHistory
{
    public CooldownWaiver? Waiver { get; set; }
    public DateTimeOffset? LastAcceptedCreatedAtUtc { get; set; }
    public DateTimeOffset? LastAcceptedDeletedAtUtc { get; set; }
    public bool RequiresReview { get; set; }
}

public sealed class PostRecord
{
    [JsonRequired] public ulong ThreadId { get; set; }
    [JsonRequired] public ulong ParentChannelId { get; set; }
    [JsonRequired] public ulong AuthorId { get; set; }
    [JsonRequired] public ForumKind Forum { get; set; }
    [JsonRequired] public DateTimeOffset CreatedAtUtc { get; set; }
    [JsonRequired] public DateTimeOffset FirstSeenAtUtc { get; set; }
    public string Title { get; set; } = string.Empty;
    public ulong[] AppliedTagIds { get; set; } = [];
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public AcknowledgementStatus Acknowledgement { get; set; }
    public DateTimeOffset? PromptedAtUtc { get; set; }
    public DateTimeOffset? ChallengeDeadlineUtc { get; set; }
    public string[] AcceptedCodes { get; set; } = [];
    // Set by public reconciliation only after a usable prompt and healthy reconciliation.
    public bool ChallengeEnforceable { get; set; }
    public bool EnforcementEnrolled { get; set; }
    public ListingLifecycle Lifecycle { get; set; }
    public CloseReason? CloseReason { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public DateTimeOffset? DeletedObservedAtUtc { get; set; }
    public bool DeletionTimeUncertain { get; set; }
    public DateTimeOffset? FirstQualifyingResponseAtUtc { get; set; }
    public DateTimeOffset? ResponsesCheckedThroughUtc { get; set; }
    public bool RequiresReview { get; set; }
    public bool IsPinned { get; set; }
    public bool IsExempt { get; set; }
    public bool ClosedRequested { get; set; }
    public ActivityStatus Activity { get; set; }
    public PaymentSignal Payment { get; set; }
    public ulong? AdvisoryMessageId { get; set; }
    public ulong? FeedMessageId { get; set; }
    public PostObservation Observation { get; set; } = new();
    public PostAdvisory Advisory { get; set; } = new();
    public LifecycleAction? PendingAction { get; set; }
    public DateTimeOffset? EnforcementNextCheckAtUtc { get; set; }
    public DateTimeOffset? HistoryReviewedThroughUtc { get; set; }
    public bool FindingDismissed { get; set; }
    public List<AuditRecord> Audit { get; set; } = [];
}

public sealed class ForumObservation
{
    public ForumPublication Publication { get; set; } = new();
    public DateTimeOffset? ActiveCheckedAtUtc { get; set; }
    public DateTimeOffset? ArchiveBeforeUtc { get; set; }
    public DateTimeOffset? ArchiveCompletedAtUtc { get; set; }
    public bool ArchiveRescanRequired { get; set; }
    public string? Error { get; set; }
}

/// <summary>Evidence and resumable read/feed work; never acknowledgement or penalty state.</summary>
public sealed class PostObservation
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

public sealed record Eligibility(
    EligibilityKind Kind,
    DateTimeOffset? NextAllowedAtUtc,
    ulong? BlockingThreadId,
    IReadOnlyList<ulong> RecentOtherForumThreadIds)
{
    public bool IsEligible => Kind == EligibilityKind.Eligible;
    public string? PlacementReminder => RecentOtherForumThreadIds.Count == 0 ? null :
        "You've recently posted in another recruitment forum. Please double-check that each listing is in the right place: recruiting seeks people; for-hire offers your services.";
}

public sealed record PolicyAction(ActionKind Kind, CloseReason? Reason)
{
    public static PolicyAction None { get; } = new(ActionKind.None, null);
}

public sealed record ReplyEvidence(
    ulong AuthorId, DateTimeOffset CreatedAtUtc, bool IsBot, bool IsWebhook,
    bool? IsModerator, bool? IsAdministrator, bool IsUserMessage = true);
