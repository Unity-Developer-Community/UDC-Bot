using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DiscordBot.Services.Recruitment;

/// <summary>Verified forum publication and any unfinished desired state; never contains an inferred success.</summary>
public sealed class ForumReceipt
{
    [JsonProperty(Required = Required.AllowNull)]
    public string ForumId { get; set; } = "";
    public RecruitmentFault? Fault { get; set; }
    public ArchiveCursor? ArchiveCursor { get; set; }
    public ReferenceReceipt Reference { get; set; } = new();
    public string ConfirmedTopic { get; set; } = "";
    public string VisibleCode { get; set; } = "";
    public Dictionary<string, ManagedTag> Tags { get; set; } = new();
    // Persist the desired publication before Discord is called, including retries after ambiguous writes.
    public bool PendingRepair { get; set; }
    public string? PendingTopic { get; set; }
    public string? PendingCode { get; set; }
    public List<TagDefinition>? PendingTags { get; set; }
}

/// <summary>A forum's reference thread, separate from member listings and the private feed overview.</summary>
public sealed class ReferenceReceipt
{
    public string? ThreadId { get; set; }
    public bool BotOwned { get; set; }
    public bool CreatePending { get; set; }
    public DateTimeOffset? CreateStartedAt { get; set; }
    public RecruitmentFault? Fault { get; set; }
}

public sealed class ManagedTag
{
    [JsonProperty(Required = Required.AllowNull)]
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Moderated { get; set; }
}
