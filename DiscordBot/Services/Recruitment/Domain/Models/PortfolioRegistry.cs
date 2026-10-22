using System.Globalization;
using System.IO;

namespace DiscordBot.Services.Recruitment;

/// <summary>The effective set is persisted, including an intentionally empty set.</summary>
public sealed class PortfolioRegistry
{
    public long Version { get; set; } = 1;
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<string> Hosts { get; set; } = RecruitmentContent.SeedPortfolioHosts.Order(StringComparer.Ordinal).ToList();
    public List<RecruitmentAudit> Audit { get; set; } = new();
    public IReadOnlySet<string> EffectiveHosts() => new HashSet<string>(Hosts, StringComparer.OrdinalIgnoreCase);
}
