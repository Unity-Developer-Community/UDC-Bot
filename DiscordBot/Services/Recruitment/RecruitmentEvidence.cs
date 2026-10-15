using System.Security.Cryptography;
using System.Text;

namespace DiscordBot.Services.Recruitment;

public sealed partial class RecruitmentEngine
{
    private void RefreshFacts(Listing post, ThreadSnapshot live)
    {
        var payment = RecruitmentContent.ReadPayment(live.Starter);
        post.Facts.AccountCreatedAt = live.AccountCreatedAt; post.Facts.JoinedAt = live.JoinedAt;
        post.Facts.MemberRoleCount = live.MemberRoleCount; post.Facts.HasCustomAvatar = live.HasCustomAvatar;
        post.Facts.Payment = payment.Summary; post.Facts.PaymentEvidence = payment.Evidence;
        post.Facts.Portfolio = RecruitmentContent.Portfolio(live.Starter, live.Attachments);
        post.Facts.Observations = RecruitmentContent.Observations(live.Starter);
        if (live.Starter is null) return; // Missing data must not overwrite previously observed evidence.
        var attachments = live.Attachments?.Take(10).ToList() ?? new();
        var normalized = string.Join(' ', live.Starter.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized + "\n" + string.Join('|', attachments.Select(a => a.Id)))));
        if (post.LatestStarter?.Fingerprint == fingerprint) return;
        var version = new StarterVersion(Now, live.Starter[..Math.Min(2000, live.Starter.Length)], live.Starter.Length > 2000,
            fingerprint, payment.Summary, payment.Evidence, RecruitmentContent.ContactLinks(live.Starter), attachments,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RecruitmentContent.CompensationTerms(live.Starter)))));
        if (post.LatestStarter is { } previous && post.AcceptedAt.HasValue &&
            (previous.Payment != version.Payment || previous.PaymentEvidence != version.PaymentEvidence ||
             previous.TermsFingerprint is not null && previous.TermsFingerprint != version.TermsFingerprint ||
             !previous.Links.SequenceEqual(version.Links) || !previous.Attachments.SequenceEqual(version.Attachments)))
        {
            if (post.MaterialChanges == post.ReviewedMaterialChanges) QueueAttention(post, "Payment terms, links or attachments changed after acceptance");
            post.MaterialChanges++;
            post.EarlierMaterialVersions.Add(previous);
            if (post.EarlierMaterialVersions.Count > 3) post.EarlierMaterialVersions.RemoveAt(0);
        }
        post.OriginalStarter ??= version;
        post.LatestStarter = version;
        post.UpdatedAt = Now;
    }

    private string EvidenceDetails(Listing post)
    {
        var text = RecruitmentPresentation.Staff(State!, post, _settings).Content + "\n" +
            $"Created {RecruitmentPresentation.Timestamp(post.CreatedAt)} · accepted {(post.AcceptedAt.HasValue ? RecruitmentPresentation.Timestamp(post.AcceptedAt.Value) : "no")}\n";
        var versions = new[] { post.OriginalStarter }.Concat(post.EarlierMaterialVersions).Append(post.LatestStarter)
            .OfType<StarterVersion>().DistinctBy(v => (v.ObservedAt, v.Fingerprint));
        foreach (var version in versions)
        {
            text += $"\n**Starter observed {version.ObservedAt:u}{(version.Truncated ? " (first 2,000 characters)" : "")}**\n" +
                RecruitmentPresentation.Safe(version.Content, 2000) + "\n";
            if (version.Attachments.Count > 0) text += "Attachments: " + string.Join(", ", version.Attachments.Select(a => RecruitmentPresentation.Safe(a.Name, 120))) + "\n";
        }
        if (post.LatestStarter is { } latest)
        {
            var related = State!.Listings.Values.Where(p => p.Id != post.Id && p.OwnerId != post.OwnerId && p.LatestStarter is { } other &&
                (latest.Content.Length >= 120 && latest.Fingerprint == other.Fingerprint || latest.Links.Intersect(other.Links, StringComparer.OrdinalIgnoreCase).Any()))
                .OrderByDescending(p => p.CreatedAt).Take(5).Select(p => $"<#{p.Id}> (author `{p.OwnerId}`)").ToArray();
            if (related.Length > 0) text += "\nShared starter text or link with: " + string.Join(", ", related) + ". Review context; shared projects can be legitimate.\n";
        }
        foreach (var report in post.Reports)
            text += $"\n**Private report · {(report.ResolvedAt.HasValue ? "resolved" : "open")} · {report.At:u} · reporter `{report.ReporterId}`**\n{RecruitmentPresentation.Safe(report.Concern, 1000)}\n";
        foreach (var audit in post.Audit.TakeLast(10)) text += $"\n{audit.At:u} · `{audit.Actor}` · {RecruitmentPresentation.Safe(audit.Reason, 300)}";
        return text;
    }
}
