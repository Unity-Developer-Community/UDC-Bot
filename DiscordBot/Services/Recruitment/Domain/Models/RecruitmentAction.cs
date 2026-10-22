using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DiscordBot.Services.Recruitment;

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
