namespace DiscordBot.Services.Recruitment.State;

public enum ActionOrigin { Owner, Automatic, Moderator }

/// <summary>One durable intent. Completion and its history/count effects commit in the same state transaction.</summary>
public sealed class LifecycleAction
{
    public string Id { get; set; } = "";
    public ActionKind Kind { get; set; }
    public ActionOrigin Origin { get; set; }
    public CloseReason Reason { get; set; }
    public ulong ActorId { get; set; }
    public string Note { get; set; } = "";
    public long ExpectedVersion { get; set; }
    public bool ReviewRequiredAtRequest { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? AttemptedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public int? TimeoutCountAfter { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPending => CompletedAtUtc is null && CancelledAtUtc is null;
}

public sealed class CooldownWaiver
{
    public DateTimeOffset? ThroughCreatedAtUtc { get; set; }
    public DateTimeOffset? ThroughDeletedAtUtc { get; set; }
    public DateTimeOffset GrantedAtUtc { get; set; }
    public ulong ActorId { get; set; }
    public string Reason { get; set; } = "";
}

public sealed record AuditRecord(string Id, DateTimeOffset AtUtc, ulong ActorId, string Operation, string Reason);

