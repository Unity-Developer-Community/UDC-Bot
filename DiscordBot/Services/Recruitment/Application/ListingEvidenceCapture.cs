using System.Security.Cryptography;
using System.Text;
using System.IO;
using DiscordBot.Settings;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Analysis;
using DiscordBot.Services.Recruitment.Domain.Policies;
using DiscordBot.Services.Recruitment.Infrastructure.Discord;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed class ListingEvidenceCapture(RecruitmentState state, TimeProvider time, Action<Listing, string> queueAttention)
{
    private readonly RecruitmentState State = state;
    private DateTimeOffset Now => time.GetUtcNow();
    private void QueueAttention(Listing post, string reason) => queueAttention(post, reason);

    public void RefreshFacts(Listing post, ThreadSnapshot live)
    {
        var purpose = State!.Forums.First(f => f.Value.ForumId == post.ForumId).Key;
        var analysis = ListingAnalysis.Read(live, purpose, State.PortfolioSites.EffectiveHosts());
        var payment = analysis.Payment;
        post.Facts.Checks = RecruitmentSignals.Evaluate(live, purpose, analysis);
        post.PortfolioRegistryVersion = State.PortfolioSites.Version;
        post.Facts.PortfolioCandidates = analysis.Portfolio;
        post.Facts.ServerNickname = RecruitmentSignals.SafeIdentity(live.ServerNickname);
        post.Facts.GlobalName = RecruitmentSignals.SafeIdentity(live.GlobalName);
        post.Facts.Username = RecruitmentSignals.SafeIdentity(live.Username);
        post.Facts.AvatarUrl = RecruitmentSignals.SafeAvatarUrl(live.AvatarUrl);
        post.Facts.AvatarIdentity = RecruitmentSignals.SafeIdentity(live.AvatarIdentity, 200);
        post.Facts.AccountCreatedAt = live.AccountCreatedAt; post.Facts.JoinedAt = live.JoinedAt;
        post.Facts.MemberRoleCount = live.MemberRoleCount; post.Facts.HasCustomAvatar = live.HasCustomAvatar;
        post.Facts.Payment = payment.Summary; post.Facts.PaymentEvidence = payment.Evidence;
        post.Facts.Portfolio = analysis.PortfolioSummary;
        post.Facts.Observations = analysis.Observations;
        if (live.Starter is null) return; // Missing data must not overwrite previously observed evidence.
        var attachments = live.Attachments?.Take(10).ToList() ?? new();
        var normalized = string.Join(' ', live.Starter.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized + "\n" + string.Join('|', attachments.Select(a => a.Id)))));
        if (post.LatestStarter?.Fingerprint == fingerprint) return;
        var version = new StarterVersion(Now, live.Starter[..Math.Min(2000, live.Starter.Length)], live.Starter.Length > 2000,
            fingerprint, payment.Summary, payment.Evidence, analysis.ContactLinks, attachments,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(analysis.CompensationTerms))));
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

}
