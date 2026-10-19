using DiscordBot.Utils;
using System.Globalization;
using System.IO;

namespace DiscordBot.Services.Recruitment;

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
        reason = DiscordDisplayText.Literal(reason, 300).Trim();
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
