using DiscordBot.Services.Recruitment.Presentation;
using static DiscordBot.Utils.DiscordDisplayText;
using DiscordBot.Settings;
using System.IO;

namespace DiscordBot.Services.Recruitment;

public static class RecruitmentPresentation
{
    public const string Heading = "Recruitment · Automated Observations";
    public const string Guidance = "Automated observations; neither endorsement nor judgment. Verify claims. Keep first contact and key terms here; use DMs only for private information.";

    public static RecruitmentMessage PublicV2(RecruitmentState state, Listing post, RecruitmentSettings settings,
        DateTimeOffset now, Func<IReadOnlyList<RecruitmentObservation>, byte[]>? render = null)
    {
        return PublicListingRenderer.Render(PublicListingView.From(state, post), render);
    }

    public static string Details(Listing post)
    {
        return "**Legend · Recruitment observations**\n" + string.Join("\n", post.Facts.Checks.Select(c =>
            $"{RecruitmentBanner.Icon(c)} **{c.Label}:** {Literal(c.Value, 60)}" +
            (c.Id == "post-detail" || string.IsNullOrWhiteSpace(c.Evidence) ? "" : $" — {Literal(c.Evidence, 120)}"))) +
            (post.Facts.Observations.Count == 0 ? "" : "\n\n**Additional evidence**\n" + string.Join("\n", post.Facts.Observations.Take(4).Select(o => $"• **{Literal(o.Description, 80)}:** “{Literal(o.Evidence, 120)}”."))) +
            "\n\n🟢 Observed fact · 🔶 Clarification needed · 🟥 Missing detail or concern · ⚪ Unavailable or not applicable" +
            "\nAges are measured at posting. Job Duration looks for a work window or availability. Portfolio links are detected, not verified. Post Detail excludes links and formatting; length does not prove quality." +
            "\nThese observations do not establish trust or authorship; unavailable data is not evidence against the author. Use **Report concern** to contact moderators privately.";
    }

    public static RecruitmentMessage Staff(RecruitmentState state, Listing post, RecruitmentSettings settings)
    {
        var text = $"**Recruitment · {post.Status}** · {Literal(post.Title, 100)}\n" +
            $"<#{post.Id}> · author `{post.OwnerId}` · {(post.AcceptedAt.HasValue ? "acknowledged" : "unaccepted")}\n" +
            $"{Literal(post.LastOutcome, 260)}\n";
        if (post.PendingAction is { } action) text += $"Pending {action.Kind}: {Literal(action.Reason)}\n";
        if (post.Hold is not null) text += $"Hold: {Literal(post.Hold)}\n";
        if (post.TimeoutAlertCount >= 3) text += $"Moderator attention: {post.TimeoutAlertCount} confirmed Guidelines timeouts.\n";
        if (post.Reports.Any(r => r.ResolvedAt is null)) text += "Private reports awaiting review.\n";
        if (post.MaterialChanges > post.ReviewedMaterialChanges) text += "Material starter changes awaiting review.\n";
        if (post.Audit.LastOrDefault() is { } audit) text += $"Latest action · `{audit.Actor}` · {Literal(audit.Reason)}\n";
        text += $"Details: `/recruitment lookup {post.Id}`\n";
        var marker = $"recruitment:{post.Id}:staff";
        if (text.Length > 1900) text = text[..1880] + "\n… more in stored audit\n";
        return new RecruitmentMessage(text + $"||{marker}||", marker, false);
    }

}
