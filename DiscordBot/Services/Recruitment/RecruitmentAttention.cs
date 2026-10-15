namespace DiscordBot.Services.Recruitment;

public sealed partial class RecruitmentEngine
{
    private string? AttentionSummary()
    {
        if (State is null) return null;
        var rows = new List<string>();
        foreach (var post in State.Listings.Values.OrderByDescending(p => p.UpdatedAt))
        {
            var reasons = new List<string>();
            if (post.Reports.Any(r => r.ResolvedAt is null)) reasons.Add("private reports");
            if (post.MaterialChanges > post.ReviewedMaterialChanges) reasons.Add("changed terms/links");
            if (post.Hold is not null) reasons.Add("processing held");
            if (post.AttemptPausedUntil > Now) reasons.Add("posting pause");
            if (post.TimeoutAlertCount >= 3 && State.Authors.GetValueOrDefault(post.OwnerId)?.TimeoutAlerted == true) reasons.Add("repeated timeouts");
            if (post.Attention.Any(n => n.Fault is not null)) reasons.Add("notice delivery held");
            if (reasons.Count > 0) rows.Add($"• <#{post.Id}> · {string.Join(", ", reasons)}");
        }
        if (rows.Count == 0) return null;
        return string.Join("\n", rows.Take(5)) + (rows.Count > 5 ? $"\n…and {rows.Count - 5} more; use lookup for individual records." : "");
    }

    private string? ActivitySummary()
    {
        if (State is null) return null;
        var recent = State.Listings.Values.Where(p => p.CreatedAt > Now.AddDays(-1)).ToArray();
        var accepted = recent.Where(p => p.AcceptedAt.HasValue).ToArray();
        var delay = accepted.Length == 0 ? "—" : Math.Round(accepted.Average(p => (p.AcceptedAt!.Value - (p.PromptDeliveredAt ?? p.CreatedAt)).TotalMinutes), 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " min";
        return $"Past 24h: {recent.Length} creations · {accepted.Length} accepted · {recent.Count(p => p.Status == ListingStatus.Closed && !p.AcceptedAt.HasValue)} closed conflicts · " +
            $"{recent.Count(p => p.LastOutcome == "Guidelines acknowledgement timed out")} timeouts · mean prompt-to-accept: {delay}.";
    }
}
