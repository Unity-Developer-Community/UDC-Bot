using DiscordBot.Services.Recruitment.Domain.Analysis;
using DiscordBot.Utils;
using System.Security.Cryptography;
using System.Text;

namespace DiscordBot.Services.Recruitment;

public sealed partial class RecruitmentEngine
{
    private string EvidenceDetails(Listing post)
    {
        var text = RecruitmentPresentation.Staff(State!, post, _settings).Content + "\n" +
            $"Created {DiscordDisplayText.Timestamp(post.CreatedAt)} · accepted {(post.AcceptedAt.HasValue ? DiscordDisplayText.Timestamp(post.AcceptedAt.Value) : "no")}\n";
        var pin = post.PublicPin;
        text += $"Optional response pin: {(pin.MessageId is null ? "not attempted" : pin.Confirmed ? "confirmed; later manual unpins respected" : "pending")} · message `{pin.MessageId ?? "none"}`" +
            (pin.RestoreArchive ? " · archive/lock restoration pending" : "") + "\n";
        if (pin.Fault is { } pinFault)
            text += $"Pin fault: {DiscordDisplayText.Literal(pinFault.Message, 400)} · retry {(pinFault.RetryAt is { } retry ? DiscordDisplayText.Timestamp(retry) : "after relevant access change or explicit recovery")}\n";
        var versions = new[] { post.OriginalStarter }.Concat(post.EarlierMaterialVersions).Append(post.LatestStarter)
            .OfType<StarterVersion>().DistinctBy(v => (v.ObservedAt, v.Fingerprint));
        foreach (var version in versions)
        {
            text += $"\n**Starter observed {version.ObservedAt:u}{(version.Truncated ? " (first 2,000 characters)" : "")}**\n" +
                DiscordDisplayText.Literal(version.Content, 2000) + "\n";
            if (version.Attachments.Count > 0) text += "Attachments: " + string.Join(", ", version.Attachments.Select(a => DiscordDisplayText.Literal(a.Name, 120))) + "\n";
        }
        if (post.LatestStarter is { } latest)
        {
            static IEnumerable<StarterVersion> Retained(Listing p) => new[] { p.OriginalStarter }.Concat(p.EarlierMaterialVersions)
                .Append(p.LatestStarter).OfType<StarterVersion>();
            var links = Retained(post).SelectMany(v => v.Links).ToHashSet(StringComparer.Ordinal);
            var related = State!.Listings.Values.Where(p => p.Id != post.Id && p.OwnerId != post.OwnerId &&
                Retained(p).Any(other => latest.Content.Length >= 120 && latest.Fingerprint == other.Fingerprint || links.Overlaps(other.Links)))
                .OrderByDescending(p => p.CreatedAt).Take(5).Select(p => $"<#{p.Id}> (author `{p.OwnerId}`)").ToArray();
            if (related.Length > 0) text += "\nShared starter text or link with: " + string.Join(", ", related) + ". Review context; shared projects can be legitimate.\n";
        }
        foreach (var report in post.Reports)
            text += $"\n**Private report · {(report.ResolvedAt.HasValue ? "resolved" : "open")} · {report.At:u} · reporter `{report.ReporterId}`**\n{DiscordDisplayText.Literal(report.Concern, 1000)}\n";
        foreach (var audit in post.Audit.TakeLast(10)) text += $"\n{audit.At:u} · `{audit.Actor}` · {DiscordDisplayText.Literal(audit.Reason, 300)}";
        return text;
    }
}
