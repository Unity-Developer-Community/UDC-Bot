using DiscordBot.Settings;

namespace DiscordBot.Services.Recruitment;

public static class RecruitmentPresentation
{
    public static string Timestamp(DateTimeOffset date) => $"<t:{date.ToUnixTimeSeconds()}:F>";
    public static string Safe(string value, int max = 180) => new string(value.Take(max)
        .Select(c => char.IsControl(c) || "`*_~|<>@[]\\".Contains(c) ? ' ' : c).ToArray());

    public static RecruitmentMessage Public(RecruitmentState state, Listing post, RecruitmentSettings settings, DateTimeOffset now)
    {
        var lines = new List<string>();
        lines.Add(string.Join("\n", post.Facts.Checks.Select(c => $"**{c.Label}:** {Safe(c.Value, 60)} ({c.Applicability switch { ObservationApplicability.Unavailable => "unavailable", ObservationApplicability.NotApplicable => "not applicable", _ => c.Severity.ToString().ToLowerInvariant() }})")));
        var notes = post.Facts.Observations.Select(o => $"• {o.Description}: “{Safe(o.Evidence, 70)}”.").ToList();
        if (notes.Count > 0) lines.Add("\n**Things to check**\n" + string.Join("\n", notes));
        lines.Add("\nClarify scope, work samples and payment terms here before moving to DMs.");
        var identity = post.Facts.ServerNickname ?? post.Facts.GlobalName ?? post.Facts.Username ?? "Unavailable";
        lines.Add($"Author: {Safe(identity, 100)} · {Safe(post.Facts.Username ?? "username unavailable", 100)} · {post.OwnerId}");
        var needsAck = post.Status == ListingStatus.Open && !post.AcceptedAt.HasValue && !post.AcknowledgedAt.HasValue;
        if (needsAck)
            lines.Add("\n**Author:** Read this forum's Guidelines and enter its code with **Acknowledge**." +
                (post.Deadline is { } deadline ? $" Due {Timestamp(deadline)}." : " Your window starts when these controls are available."));
        if (post.Status == ListingStatus.Closed && !post.AcceptedAt.HasValue) lines.Add("\n**Closed:** " + Safe(post.LastOutcome, 220));
        if (post.Hold is not null || post.AvailabilityUnknown) lines.Add("\nBot processing is paused; listing details may be incomplete.");
        var embed = new EmbedBuilder().WithTitle(post.Status == ListingStatus.Closed ? "Recruitment · closed" : "Recruitment · reader notes")
            .WithDescription(string.Join("\n", lines)).WithColor(new Color(0x747F8D))
            .WithFooter("Automated observations; identity and work are unverified.")
            .WithThumbnailUrl(post.Facts.AvatarUrl).Build();
        var marker = $"recruitment:{post.Id}:public";
        var fallback = $"**{embed.Title}**\n{embed.Description}\n{embed.Footer?.Text}\n||{marker}||";
        return new RecruitmentMessage($"||{marker}||", marker, true, Embed: embed, TextFallback: fallback,
            AcknowledgementRequired: needsAck, ReaderControls: true);
    }

    public static string Details(Listing post)
    {
        return "**Listing details**\n" + string.Join("\n", post.Facts.Checks.Select(c =>
            $"{c.Label}: {c.Value}. {c.Applicability} / {c.Severity}." + (string.IsNullOrEmpty(c.Evidence) ? "" : $" {Safe(c.Evidence, 370)}"))) +
            "\nObservations use starter and membership data; unavailable data is not evidence against the author. Roles, avatars, links and length do not establish trust or authorship. Reports go privately to moderators.";
    }

    public static RecruitmentMessage Staff(RecruitmentState state, Listing post, RecruitmentSettings settings)
    {
        var text = $"**Recruitment · {post.Status}** · {Safe(post.Title, 100)}\n" +
            $"<#{post.Id}> · author `{post.OwnerId}` · {(post.AcceptedAt.HasValue ? "acknowledged" : "unaccepted")}\n" +
            $"{Safe(post.LastOutcome, 260)}\n";
        if (post.PendingAction is { } action) text += $"Pending {action.Kind}: {Safe(action.Reason)}\n";
        if (post.Hold is not null) text += $"Hold: {Safe(post.Hold)}\n";
        if (post.TimeoutAlertCount >= 3) text += $"Moderator attention: {post.TimeoutAlertCount} confirmed Guidelines timeouts.\n";
        if (post.Reports.Any(r => r.ResolvedAt is null)) text += "Private reports awaiting review.\n";
        if (post.MaterialChanges > post.ReviewedMaterialChanges) text += "Material starter changes awaiting review.\n";
        if (post.Audit.LastOrDefault() is { } audit) text += $"Latest action · `{audit.Actor}` · {Safe(audit.Reason)}\n";
        text += $"Details: `/recruitment lookup {post.Id}`\n";
        var marker = $"recruitment:{post.Id}:staff";
        if (text.Length > 1900) text = text[..1880] + "\n… more in stored audit\n";
        return new RecruitmentMessage(text + $"||{marker}||", marker, false);
    }

}
