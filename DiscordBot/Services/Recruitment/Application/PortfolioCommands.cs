using DiscordBot.Settings;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Policies;
using DiscordBot.Services.Recruitment.Presentation;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed class PortfolioCommands(RecruitmentState state, TimeProvider time, Action save)
{
    private readonly RecruitmentState State = state;
    private DateTimeOffset Now => time.GetUtcNow();
    private void Save() => save();

    public string Execute(string actor, PortfolioCommand command, string domain, string reason)
    {
        var operation = command.Operation;
        var registry = State.PortfolioSites;
        if (operation == PortfolioOperation.List)
            return "Recognized portfolio sites (recognition is not endorsement):\n" +
                (registry.Hosts.Count == 0 ? "None configured." : string.Join("\n", registry.Hosts.Order(StringComparer.Ordinal))) +
                $"\nRegistry version {registry.Version}. Subdomains match at a dot boundary.";
        if (operation is not (PortfolioOperation.Add or PortfolioOperation.Remove)) return "Choose list, add or remove.";
        reason = DiscordDisplayText.Literal(reason, 300).Trim();
        if (string.IsNullOrWhiteSpace(reason)) return "A reason is required for this change.";
        if (!RecruitmentPortfolioDomains.TryNormalize(domain, out var host))
            return "Enter a DNS hostname below a public suffix, without a URL, path, port, credentials or wildcard.";
        var exists = registry.Hosts.Contains(host, StringComparer.Ordinal);
        if (exists == (operation == PortfolioOperation.Add)) return exists ? "That site is already configured." : "That site is not configured.";
        if (operation == PortfolioOperation.Add && registry.Hosts.Count >= 100) return "The portfolio-site limit is 100 domains. Remove a site first.";
        if (registry.Version == long.MaxValue) return "Registry version limit reached; operator review required.";
        if (operation == PortfolioOperation.Add) registry.Hosts.Add(host); else registry.Hosts.Remove(host);
        registry.Version++;
        registry.Audit.Add(new RecruitmentAudit(Now, actor, $"{command.Name} {host}: {reason}"));
        if (registry.Audit.Count > 100) registry.Audit.RemoveRange(0, registry.Audit.Count - 100);
        // Version mismatch makes open listings due, using the existing bounded scheduling/retry path.
        // This policy update does not change starter versions or manufacture member edits.
        Save();
        return $"Portfolio site {host} {(operation == PortfolioOperation.Add ? "added" : "removed")}. Retained open listings will refresh in bounded batches; recognition is not endorsement.";
    }

}
