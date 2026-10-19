using DiscordBot.Utils;
namespace DiscordBot.Services.Recruitment;

public enum RecruitmentHealthKind { Unavailable, Operating, PendingIdentities, RecoveryNeeded, Stopped }

public sealed record ForumHealth(string Purpose, string Id, string Setup, string? Fault, DateTimeOffset? RetryAt, string? ReferenceId = null, string? ReferenceFault = null);

/// <summary>Detached from mutable state so moderators can inspect progress while the worker awaits Discord.</summary>
public sealed record RecruitmentHealth(string Status, string Work, DateTimeOffset ObservedAt,
    int Open, int Pending, int Held, bool InventoryComplete, DateTimeOffset? LastRecovery,
    DateTimeOffset? NextDue, IReadOnlyList<ForumHealth> Forums, string? OverviewFault = null, string? Job = null, bool RoutinePendingIdentity = false, string? Attention = null, string? Metrics = null, RecruitmentHealthKind Kind = RecruitmentHealthKind.Unavailable)
{
    public static RecruitmentHealth Starting(DateTimeOffset now) => new("Starting", "Opening recruitment state", now,
        0, 0, 0, false, null, null, Array.Empty<ForumHealth>());

}
