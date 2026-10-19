using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DiscordBot.Services.Recruitment;

/// <summary>Cross-forum cooldown and timeout history that survives removal of individual listings.</summary>
public sealed class AuthorHistory
{
    [JsonProperty(Required = Required.AllowNull)]
    public DateTimeOffset? LastAcceptedCreation { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public DateTimeOffset UpdatedAt { get; set; }
    public int ConsecutiveTimeouts { get; set; }
    public bool TimeoutAlerted { get; set; }
    public List<DateTimeOffset> RecentCreations { get; set; } = new();
    public List<DateTimeOffset> RecentReports { get; set; } = new();
    public DateTimeOffset? AttemptPausedUntil { get; set; }
}
