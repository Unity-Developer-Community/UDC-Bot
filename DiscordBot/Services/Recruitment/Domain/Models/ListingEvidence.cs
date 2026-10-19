using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DiscordBot.Services.Recruitment;

public sealed class AcknowledgementNoticeReceipt
{
    public string? MessageId { get; set; }
    public bool SendPending { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public RecruitmentFault? Fault { get; set; }
}

public sealed class RecruitmentReport
{
    public string ReporterId { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string Concern { get; set; } = "";
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class AttentionNotice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset At { get; set; }
    public string Reason { get; set; } = "";
    public string? MessageId { get; set; }
    public bool SendPending { get; set; }
    public RecruitmentFault? Fault { get; set; }
}

public sealed record StarterVersion(DateTimeOffset ObservedAt, string Content, bool Truncated, string Fingerprint,
    string Payment, string PaymentEvidence, List<string> Links, List<StarterAttachment> Attachments, string? TermsFingerprint = null);

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
    public bool? HasCustomAvatar { get; set; }
    public string PaymentEvidence { get; set; } = "";
    public string? ServerNickname { get; set; }
    public string? GlobalName { get; set; }
    public string? Username { get; set; }
    public string? AvatarUrl { get; set; }
    public string? AvatarIdentity { get; set; }
    public List<RecruitmentObservation> Checks { get; set; } = new();
    public PortfolioEvidence PortfolioCandidates { get; set; } = new(false, false, Array.Empty<PortfolioCandidate>());
    public List<ContentObservation> Observations { get; set; } = new();
}
