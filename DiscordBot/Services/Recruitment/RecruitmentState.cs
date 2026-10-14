using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DiscordBot.Services.Recruitment;

[JsonConverter(typeof(StringEnumConverter))]
public enum ListingStatus { Open, Closed, Deleted, Reference }
[JsonConverter(typeof(StringEnumConverter))]
public enum RecruitmentActionKind { Delete, Close, Reopen, RemoveAdvisory }

public sealed class RecruitmentState
{
    public const string Format = "udc-recruitment-v2";
    [JsonProperty(Required = Required.AllowNull)]
    public string Schema { get; set; } = Format;
    [JsonProperty(Required = Required.AllowNull)]
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
    [JsonProperty(Required = Required.AllowNull)]
    public string GuildId { get; set; } = "";
    [JsonProperty(Required = Required.AllowNull)]
    public DateTimeOffset InitializedAt { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public DateTimeOffset DiscoveryFloor { get; set; }
    public OverviewReceipt Overview { get; set; } = new();
    public RecruitmentFault? RecoveryFault { get; set; }
    public List<RecruitmentAudit> SetupAudit { get; set; } = new();
    public string? SweepAfterId { get; set; }
    public DateTimeOffset? LastSweepAt { get; set; }
    public string RotationCode { get; set; } = "";
    public DateTimeOffset RotateAt { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public Dictionary<string, ForumReceipt> Forums { get; set; } = new();
    [JsonProperty(Required = Required.AllowNull)]
    public Dictionary<string, Listing> Listings { get; set; } = new();
    [JsonProperty(Required = Required.AllowNull)]
    public Dictionary<string, AuthorHistory> Authors { get; set; } = new();
}

/// <summary>Verified forum publication and any unfinished desired state; never contains an inferred success.</summary>
public sealed class ForumReceipt
{
    [JsonProperty(Required = Required.AllowNull)]
    public string ForumId { get; set; } = "";
    public RecruitmentFault? Fault { get; set; }
    public ArchiveCursor? ArchiveCursor { get; set; }
    public string ConfirmedTopic { get; set; } = "";
    public string VisibleCode { get; set; } = "";
    public Dictionary<string, ManagedTag> Tags { get; set; } = new();
    // Persist the desired publication before Discord is called, including retries after ambiguous writes.
    public bool PendingRepair { get; set; }
    public string? PendingTopic { get; set; }
    public string? PendingCode { get; set; }
    public List<TagDefinition>? PendingTags { get; set; }
}

public sealed class ManagedTag
{
    [JsonProperty(Required = Required.AllowNull)]
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Moderated { get; set; }
}

/// <summary>A retained attempt, including delivery/action receipts and the evidence needed to enforce policy safely.</summary>
public sealed class Listing
{
    [JsonProperty(Required = Required.AllowNull)]
    public string Id { get; set; } = "";
    [JsonProperty(Required = Required.AllowNull)]
    public string ForumId { get; set; } = "";
    [JsonProperty(Required = Required.AllowNull)]
    public string OwnerId { get; set; } = "";
    public string Title { get; set; } = "";
    [JsonProperty(Required = Required.AllowNull)]
    public DateTimeOffset CreatedAt { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public DateTimeOffset UpdatedAt { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public ListingStatus Status { get; set; }
    public long Version { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? PromptDeliveredAt { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public List<string> ValidCodes { get; set; } = new();
    public DateTimeOffset? LastCodeAttemptAt { get; set; }
    public string? PublicMessageId { get; set; }
    public string? StaffMessageId { get; set; }
    public DateTimeOffset? PublicSendStartedAt { get; set; }
    public DateTimeOffset? StaffSendStartedAt { get; set; }
    public bool PublicationPending { get; set; }
    public bool PublicRestoreArchive { get; set; }
    public int TimeoutAlertCount { get; set; }
    public bool PublicSendPending { get; set; }
    public bool StaffSendPending { get; set; }
    public bool Exempt { get; set; }
    public bool Pinned { get; set; }
    public bool HasQualifyingReply { get; set; }
    public bool ResponseCoverageComplete { get; set; }
    public bool AvailabilityUnknown { get; set; }
    public bool SuppressUnansweredClose { get; set; }
    public RecruitmentFault? Fault { get; set; }
    public string? Hold { get; set; }
    public string LastOutcome { get; set; } = "Awaiting Guidelines acknowledgement";
    public string LastActor { get; set; } = "system";
    public string LastReason { get; set; } = "New listing";
    public RecruitmentAction? PendingAction { get; set; }
    public List<RecruitmentAudit> Audit { get; set; } = new();
    public InitialFacts Facts { get; set; } = new();
}

public sealed record RecruitmentAudit(DateTimeOffset At, string Actor, string Reason);

/// <summary>Advisory member and starter-message facts, refreshed on edits and bounded recovery checks.</summary>
public sealed class InitialFacts
{
    public DateTimeOffset? AccountCreatedAt { get; set; }
    public DateTimeOffset? JoinedAt { get; set; }
    public string Activity { get; set; } = "unknown"; // Retained for v2 history compatibility; no longer collected or displayed.
    public string Payment { get; set; } = "unknown";
    public string TagAdvice { get; set; } = "";
    public bool PriorAcceptedListing { get; set; }
    public int? MemberRoleCount { get; set; }
    public string Portfolio { get; set; } = "unknown";
}

/// <summary>Cross-forum cooldown and timeout history that survives removal of individual listings.</summary>
public sealed class AuthorHistory
{
    [JsonProperty(Required = Required.AllowNull)]
    public DateTimeOffset? LastAcceptedCreation { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public DateTimeOffset UpdatedAt { get; set; }
    public int ConsecutiveTimeouts { get; set; }
    public bool TimeoutAlerted { get; set; }
}

/// <summary>Durable mutation intent. CallStarted may represent a successful write whose response was lost.</summary>
public sealed class RecruitmentAction
{
    [JsonProperty(Required = Required.AllowNull)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public RecruitmentActionKind Kind { get; set; }
    public string ActorId { get; set; } = "system";
    public string Reason { get; set; } = "";
    public bool Automatic { get; set; }
    public bool RequiresModerator { get; set; }
    public string Condition { get; set; } = "";
    public bool GuidelineTimeout { get; set; }
    public bool CallStarted { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record Eligibility(string? BlockingListingId, DateTimeOffset? NextAllowedAt)
{
    public bool Allowed => BlockingListingId is null && NextAllowedAt is null;
}

public static class RecruitmentPolicy
{
    public static Eligibility Evaluate(RecruitmentState state, Listing candidate, DateTimeOffset now, int cooldownDays)
    {
        var blocker = state.Listings.Values
            .Where(p => p.OwnerId == candidate.OwnerId && p.Id != candidate.Id && p.Status == ListingStatus.Open)
            .Where(p => p.AcceptedAt.HasValue || p.AvailabilityUnknown ||
                p.CreatedAt < candidate.CreatedAt || (p.CreatedAt == candidate.CreatedAt &&
                ulong.Parse(p.Id) < ulong.Parse(candidate.Id)))
            .OrderBy(p => p.CreatedAt).ThenBy(p => ulong.Parse(p.Id)).FirstOrDefault();
        var next = state.Authors.GetValueOrDefault(candidate.OwnerId)?.LastAcceptedCreation?.AddDays(cooldownDays);
        return new Eligibility(blocker?.Id, next > now ? next : null);
    }

    public static bool TryAccept(RecruitmentState state, Listing post, DateTimeOffset now, int cooldownDays)
    {
        if (post.Status != ListingStatus.Open || post.AcceptedAt.HasValue || !post.AcknowledgedAt.HasValue ||
            post.AvailabilityUnknown || !Evaluate(state, post, now, cooldownDays).Allowed) return false;
        post.AcceptedAt = now;
        post.LastOutcome = "Accepted";
        post.Version++;
        post.UpdatedAt = now;
        Author(state, post.OwnerId, now).LastAcceptedCreation = post.CreatedAt;
        return true;
    }

    public static AuthorHistory Author(RecruitmentState state, string owner, DateTimeOffset now)
    {
        if (!state.Authors.TryGetValue(owner, out var history))
            state.Authors[owner] = history = new AuthorHistory();
        history.UpdatedAt = now;
        return history;
    }

    public static bool CanCloseUnanswered(Listing post, DateTimeOffset now, int days) =>
        post.Status == ListingStatus.Open && post.AcceptedAt.HasValue && !post.Exempt && !post.Pinned &&
        !post.AvailabilityUnknown && post.ResponseCoverageComplete && !post.HasQualifyingReply &&
        !post.SuppressUnansweredClose && post.CreatedAt.AddDays(days) <= now;

    public static void Prune(RecruitmentState state, DateTimeOffset now, int postMonths, int authorMonths, int cooldownDays)
    {
        var floor = now.AddMonths(-postMonths);
        if (floor > state.DiscoveryFloor) state.DiscoveryFloor = floor;
        foreach (var p in state.Listings.Values.Where(p => p.Status is ListingStatus.Closed or ListingStatus.Deleted &&
                     p.UpdatedAt < floor && p.PendingAction is null && p.Hold is null).ToArray())
            state.Listings.Remove(p.Id);
        foreach (var (id, author) in state.Authors.ToArray())
            if (author.UpdatedAt < now.AddMonths(-authorMonths) &&
                !(author.LastAcceptedCreation?.AddDays(cooldownDays) > now) &&
                !state.Listings.Values.Any(p => p.OwnerId == id)) state.Authors.Remove(id);
    }
}

/// <summary>The replacement is confirmed before the previous overview is retired; listing audit messages are separate.</summary>
public sealed class OverviewReceipt
{
    public string? MessageId { get; set; }
    public string? Marker { get; set; }
    public long Sequence { get; set; }
    public bool SendPending { get; set; }
    public DateTimeOffset? SendStartedAt { get; set; }
    public string? PendingMarker { get; set; }
    public bool PendingReplacement { get; set; }
    public string? RetiredMessageId { get; set; }
    public string? RetiredMarker { get; set; }
    public DateTimeOffset? LastPostedAt { get; set; }
    public DateTimeOffset? LastEditedAt { get; set; }
    public RecruitmentFault? Fault { get; set; }
}
