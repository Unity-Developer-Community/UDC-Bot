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

public static class RecruitmentPortfolioDomains
{
    private static readonly IdnMapping Idn = new();
    private static readonly Lazy<HashSet<string>> Rules = new(() =>
    {
        using var stream = typeof(RecruitmentPortfolioDomains).Assembly.GetManifestResourceStream("Recruitment.PublicSuffixIcann")
            ?? throw new InvalidDataException("Bundled ICANN public suffix rules are missing.");
        using var reader = new StreamReader(stream);
        var rules = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
            var prefix = line.StartsWith('!') ? "!" : line.StartsWith("*.", StringComparison.Ordinal) ? "*." : "";
            rules.Add(prefix + Idn.GetAscii(line[prefix.Length..]).ToLowerInvariant());
        }
        return rules;
    });

    private static readonly Lazy<HashSet<string>> Tlds = new(() => Rules.Value
        .Select(r => r.Split('.').Last().TrimStart('!')).ToHashSet(StringComparer.Ordinal));

    public static bool TryNormalize(string? input, out string host)
    {
        host = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        var value = input.Trim();
        // Only hostnames, never an authority, URL, wildcard, port or trailing root dot.
        if (value.IndexOfAny(['/', '\\', ':', '@', '*', '?', '#', '%']) >= 0 || value.EndsWith('.')) return false;
        try { value = Idn.GetAscii(value).ToLowerInvariant(); }
        catch (ArgumentException) { return false; }
        if (value.Length > 253 || Uri.CheckHostName(value) != UriHostNameType.Dns || !value.Contains('.')) return false;
        var labels = value.Split('.');
        if (labels.Any(l => l.Length is < 1 or > 63 || l[0] == '-' || l[^1] == '-' || l.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))) return false;
        var rules = Rules.Value;
        // An unlisted TLD is not an Internet registry boundary (e.g. a local search suffix).
        if (!Tlds.Value.Contains(labels[^1])) return false;
        var suffixLabels = 1;
        for (var i = 0; i < labels.Length; i++)
        {
            var suffix = string.Join('.', labels[i..]);
            if (rules.Contains("!" + suffix)) { suffixLabels = labels.Length - i - 1; break; }
            if (rules.Contains(suffix)) suffixLabels = Math.Max(suffixLabels, labels.Length - i);
            if (i + 1 < labels.Length && rules.Contains("*." + string.Join('.', labels[(i + 1)..])))
                suffixLabels = Math.Max(suffixLabels, labels.Length - i);
        }
        if (labels.Length <= suffixLabels) return false;
        host = value;
        return true;
    }
}

public sealed partial class RecruitmentEngine
{
    public async Task<string> PortfolioSitesAsync(string guild, string actor, string operation, string domain, string reason, CancellationToken ct)
    {
        if (guild != _bot.GuildId.ToString() || !await _discord.IsAdministratorAsync(guild, actor, ct))
            return "Administrator access is required in the configured server.";
        if (State is null || !_settings.Enabled) return "Recruitment is unavailable.";
        var registry = State.PortfolioSites;
        if (operation == "list")
            return "Recognized portfolio sites (recognition is not endorsement):\n" +
                (registry.Hosts.Count == 0 ? "None configured." : string.Join("\n", registry.Hosts.Order(StringComparer.Ordinal))) +
                $"\nRegistry version {registry.Version}. Subdomains match at a dot boundary.";
        if (operation is not ("add" or "remove")) return "Choose list, add or remove.";
        reason = RecruitmentPresentation.Safe(reason, 300).Trim();
        if (string.IsNullOrWhiteSpace(reason)) return "A reason is required for this change.";
        if (!RecruitmentPortfolioDomains.TryNormalize(domain, out var host))
            return "Enter a DNS hostname below a public suffix, without a URL, path, port, credentials or wildcard.";
        var exists = registry.Hosts.Contains(host, StringComparer.Ordinal);
        if (exists == (operation == "add")) return exists ? "That site is already configured." : "That site is not configured.";
        if (operation == "add" && registry.Hosts.Count >= 100) return "The portfolio-site limit is 100 domains. Remove a site first.";
        if (registry.Version == long.MaxValue) return "Registry version limit reached; operator review required.";
        if (operation == "add") registry.Hosts.Add(host); else registry.Hosts.Remove(host);
        registry.Version++;
        registry.Audit.Add(new RecruitmentAudit(Now, actor, $"{operation} {host}: {reason}"));
        if (registry.Audit.Count > 100) registry.Audit.RemoveRange(0, registry.Audit.Count - 100);
        // Version mismatch makes open listings due, using the existing bounded scheduling/retry path.
        // This policy update does not change starter versions or manufacture member edits.
        Save();
        return $"Portfolio site {host} {(operation == "add" ? "added" : "removed")}. Retained open listings will refresh in bounded batches; recognition is not endorsement.";
    }
}
