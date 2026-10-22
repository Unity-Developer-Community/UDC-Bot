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
    public PortfolioRegistry PortfolioSites { get; set; } = new();
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
