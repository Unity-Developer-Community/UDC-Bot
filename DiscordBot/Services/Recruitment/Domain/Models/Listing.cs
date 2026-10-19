using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DiscordBot.Services.Recruitment;

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
    public long PortfolioRegistryVersion { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? PromptDeliveredAt { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public List<string> ValidCodes { get; set; } = new();
    public DateTimeOffset? LastCodeAttemptAt { get; set; }
    public DateTimeOffset? AttemptPausedUntil { get; set; }
    public AcknowledgementNoticeReceipt AcknowledgementNotice { get; set; } = new();
    public MessagePinReceipt PublicPin { get; set; } = new();
    public string? PublicMessageId { get; set; }
    public string? StaffMessageId { get; set; }
    public DateTimeOffset? PublicSendStartedAt { get; set; }
    public DateTimeOffset? StaffSendStartedAt { get; set; }
    public bool PublicationPending { get; set; }
    public bool PublicRestoreArchive { get; set; }
    public bool PublicRestoreLocked { get; set; }
    public string? PublicMediaAttachmentId { get; set; }
    public string? PublicMediaFileName { get; set; }
    public int TimeoutAlertCount { get; set; }
    public bool PublicSendPending { get; set; }
    public bool StaffSendPending { get; set; }
    public bool Exempt { get; set; }
    public bool Pinned { get; set; }
    // Legacy v2 fields remain readable; reply history no longer determines listing lifetime.
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
    public StarterVersion? OriginalStarter { get; set; }
    public StarterVersion? LatestStarter { get; set; }
    public List<StarterVersion> EarlierMaterialVersions { get; set; } = new();
    public int MaterialChanges { get; set; }
    public int ReviewedMaterialChanges { get; set; }
    public List<RecruitmentReport> Reports { get; set; } = new();
    public List<AttentionNotice> Attention { get; set; } = new();
}
