using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DiscordBot.Services.Recruitment;

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

/// <summary>Optional message pin work; never grants forum-post protection or listing capacity.</summary>
public sealed class MessagePinReceipt
{
    public string? MessageId { get; set; }
    public bool Confirmed { get; set; }
    public bool RestoreArchive { get; set; }
    public bool RestoreLocked { get; set; }
    public RecruitmentFault? Fault { get; set; }
}
