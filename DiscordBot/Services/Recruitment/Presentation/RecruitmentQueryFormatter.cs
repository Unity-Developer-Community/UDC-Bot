using DiscordBot.Utils;

namespace DiscordBot.Services.Recruitment.Presentation;

internal sealed record EligibilityListing(string Id, bool Accepted);
internal sealed record EligibilityView(IReadOnlyList<EligibilityListing> Open, bool HasAuthor, DateTimeOffset? NextAllowedAt,
    DateTimeOffset? AttemptPausedUntil, DateTimeOffset Now, bool EvidenceCurrent, int RecentAttempts,
    int CooldownDays, int AttemptLimit, int AttemptWindowMinutes, int AttemptPauseMinutes);
internal sealed record RelatedListing(string Id, string OwnerId);
internal sealed record PrivateEvidenceView(string StaffSummary, DateTimeOffset CreatedAt, DateTimeOffset? AcceptedAt,
    MessagePinReceipt Pin, IReadOnlyList<StarterVersion> Versions, IReadOnlyList<RelatedListing> Related,
    IReadOnlyList<RecruitmentReport> Reports, IReadOnlyList<RecruitmentAudit> Audit);

internal static class RecruitmentQueryFormatter
{
    public static string Eligibility(EligibilityView view)
    {
        var lines = new List<string>();
        foreach (var post in view.Open)
            lines.Add($"Your {(post.Accepted ? "open" : "pending")} listing occupies your slot: <#{post.Id}>. Update or bump this thread to keep its conversation history. Close it when finished; auto-archive does not free your slot.");
        if (view.HasAuthor)
        {
            if (view.NextAllowedAt is { } next && next > view.Now)
                lines.Add($"Your accepted-listing cooldown ends {DiscordDisplayText.Timestamp(next)}. Closing or deleting does not reset this cooldown.");
            if (view.AttemptPausedUntil > view.Now)
                lines.Add($"Posting attempts are paused until {DiscordDisplayText.Timestamp(view.AttemptPausedUntil.Value)}. A moderator can review a mistake.");
        }
        if (!view.EvidenceCurrent)
            lines.Add("Eligibility is being checked; the one-listing rule still applies.");
        else if (view.Open.Count == 0)
            lines.Add("No open listing is recorded.");
        lines.Add($"You may have **one open listing across all four recruitment forums**. Pending listings reserve that slot. An accepted listing starts a **{view.CooldownDays}-day cooldown from creation**.");
        var used = view.RecentAttempts;
        lines.Add($"Anti-spam: creation attempt {view.AttemptLimit + 1} within {view.AttemptWindowMinutes} minutes triggers a {view.AttemptPauseMinutes}-minute pause; deleted attempts count. Recent attempts: {used}.");
        return string.Join("\n", lines);
    }

    public static string Evidence(PrivateEvidenceView view)
    {
        var text = view.StaffSummary + "\n" +
            $"Created {DiscordDisplayText.Timestamp(view.CreatedAt)} · accepted {(view.AcceptedAt.HasValue ? DiscordDisplayText.Timestamp(view.AcceptedAt.Value) : "no")}\n";
        var pin = view.Pin;
        text += $"Optional response pin: {(pin.MessageId is null ? "not attempted" : pin.Confirmed ? "confirmed; later manual unpins respected" : "pending")} · message `{pin.MessageId ?? "none"}`" +
            (pin.RestoreArchive ? " · archive/lock restoration pending" : "") + "\n";
        if (pin.Fault is { } pinFault)
            text += $"Pin fault: {DiscordDisplayText.Literal(pinFault.Message, 400)} · retry {(pinFault.RetryAt is { } retry ? DiscordDisplayText.Timestamp(retry) : "after relevant access change or explicit recovery")}\n";
        foreach (var version in view.Versions)
        {
            text += $"\n**Starter observed {version.ObservedAt:u}{(version.Truncated ? " (first 2,000 characters)" : "")}**\n" +
                DiscordDisplayText.Literal(version.Content, 2000) + "\n";
            if (version.Attachments.Count > 0) text += "Attachments: " + string.Join(", ", version.Attachments.Select(a => DiscordDisplayText.Literal(a.Name, 120))) + "\n";
        }
        if (view.Related.Count > 0) text += "\nShared starter text or link with: " +
            string.Join(", ", view.Related.Select(p => $"<#{p.Id}> (author `{p.OwnerId}`)")) + ". Review context; shared projects can be legitimate.\n";
        foreach (var report in view.Reports)
            text += $"\n**Private report · {(report.ResolvedAt.HasValue ? "resolved" : "open")} · {report.At:u} · reporter `{report.ReporterId}`**\n{DiscordDisplayText.Literal(report.Concern, 1000)}\n";
        foreach (var audit in view.Audit) text += $"\n{audit.At:u} · `{audit.Actor}` · {DiscordDisplayText.Literal(audit.Reason, 300)}";
        return text;
    }
}
