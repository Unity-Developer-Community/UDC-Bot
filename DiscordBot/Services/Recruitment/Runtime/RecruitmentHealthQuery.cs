using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Policies;

namespace DiscordBot.Services.Recruitment.Runtime;

internal static class RecruitmentHealthQuery
{
    public static RecruitmentHealth Build(RecruitmentState? state, bool ready, bool evidenceCurrent, bool pendingIdentity,
        string status, string work, DateTimeOffset now, DateTimeOffset? nextWork)
    {
        var kind = RecruitmentHealthKind.Unavailable;
        if (ready)
        {
            if (evidenceCurrent) { kind = RecruitmentHealthKind.Operating; status = "Operating"; }
            else if (pendingIdentity) { kind = RecruitmentHealthKind.PendingIdentities; status = "Processing new listing identities"; }
            else { kind = RecruitmentHealthKind.RecoveryNeeded; status = "Inventory incomplete; acceptance and automatic actions held"; }
        }
        return new(status, work, now,
            state?.Listings.Values.Count(p => p.Status == ListingStatus.Open) ?? 0,
            state?.Listings.Values.Count(p => p.Status == ListingStatus.Open && !p.AcknowledgedAt.HasValue) ?? 0,
            state?.Listings.Values.Count(p => p.Hold is not null) ?? 0, evidenceCurrent,
            state?.LastSweepAt, nextWork,
            state?.Forums.Select(f => new ForumHealth(f.Key, f.Value.ForumId, ForumStatus(f.Value),
                f.Value.Fault?.Message, f.Value.Fault?.RetryAt, f.Value.Reference.ThreadId, f.Value.Reference.Fault?.Message)).ToArray() ?? Array.Empty<ForumHealth>(),
            state?.Overview.Fault?.Message, RoutinePendingIdentity: pendingIdentity, Attention: AttentionSummary(state, now), Metrics: ActivitySummary(state, now), Kind: kind);
    }

    private static string ForumStatus(ForumReceipt forum)
    {
        if (forum.Fault is not null) return "held";
        if (forum.PendingTopic is not null) return "publication pending";
        return forum.ConfirmedTopic.Length == 0 ? "not verified" : "Guidelines and tags verified";
    }

    private static string? AttentionSummary(RecruitmentState? State, DateTimeOffset Now)
    {
        if (State is null) return null;
        var rows = new List<string>();
        foreach (var post in State.Listings.Values.OrderByDescending(p => p.UpdatedAt))
        {
            var reasons = new List<string>();
            if (post.Reports.Any(r => r.ResolvedAt is null)) reasons.Add("private reports");
            if (post.MaterialChanges > post.ReviewedMaterialChanges) reasons.Add("changed terms/links");
            if (post.PublicPin.Fault is not null) reasons.Add("optional response pin: " + DiscordDisplayText.Literal(post.PublicPin.Fault.Message, 100));
            if (post.Hold is not null) reasons.Add("processing held");
            if (post.AttemptPausedUntil > Now) reasons.Add("posting pause");
            if (post.TimeoutAlertCount >= 3 && State.Authors.GetValueOrDefault(post.OwnerId)?.TimeoutAlerted == true) reasons.Add("repeated timeouts");
            if (post.Attention.Any(n => n.Fault is not null)) reasons.Add("notice delivery held");
            if (reasons.Count > 0) rows.Add($"• <#{post.Id}> · {string.Join(", ", reasons)}");
        }
        if (rows.Count == 0) return null;
        return string.Join("\n", rows.Take(5)) + (rows.Count > 5 ? $"\n…and {rows.Count - 5} more; use lookup for individual records." : "");
    }

    private static string? ActivitySummary(RecruitmentState? State, DateTimeOffset Now)
    {
        if (State is null) return null;
        var recent = State.Listings.Values.Where(p => p.CreatedAt > Now.AddDays(-1)).ToArray();
        var accepted = recent.Where(p => p.AcceptedAt.HasValue).ToArray();
        var delay = accepted.Length == 0 ? "—" : Math.Round(accepted.Average(p => (p.AcceptedAt!.Value - (p.PromptDeliveredAt ?? p.CreatedAt)).TotalMinutes), 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " min";
        return $"Past 24h: {recent.Length} creations · {accepted.Length} accepted · {recent.Count(p => p.Status == ListingStatus.Closed && !p.AcceptedAt.HasValue)} closed conflicts · " +
            $"{recent.Count(p => LegacyListingOutcomes.IsGuidelineTimeout(p))} timeouts · mean prompt-to-accept: {delay}.";
    }
}
