using static DiscordBot.Utils.DiscordDisplayText;

namespace DiscordBot.Services.Recruitment.Presentation;

/// <summary>Public fields only; private reports, audits and captured starter history cannot reach the renderer.</summary>
public sealed record PublicListingView(IReadOnlyList<RecruitmentObservation> Checks, string Alt, bool NeedsAcknowledgement,
    string Identity, string? Avatar, string Marker, string Visible, string Foot, string ListingId, long RegistryVersion)
{
    public static PublicListingView From(RecruitmentState state, Listing post)
    {
        var checks = post.Facts.Checks.ToArray();
        var alt = RecruitmentBanner.Alt(checks);
        var needsAck = post.Status == ListingStatus.Open && !post.AcceptedAt.HasValue && !post.AcknowledgedAt.HasValue;
        var operations = new List<string>();
        if (needsAck) operations.Add("**Author:** Read this forum's Guidelines and enter its code with **Acknowledge**." +
            (post.Deadline is { } deadline ? $" Due {Timestamp(deadline)}." : " Your window starts when these controls are available."));
        if (post.Status == ListingStatus.Closed) operations.Add("**Closed:** " + Literal(post.LastOutcome, 220));
        if (post.Hold is not null || post.AvailabilityUnknown) operations.Add("Bot processing is paused; listing details may be incomplete.");
        var warnings = string.Join("\n", post.Facts.Observations.Take(4).Select(o => $"• {Literal(o.Description, 100)}: “{Literal(o.Evidence, 65)}”."));
        var name = new[] { post.Facts.ServerNickname, post.Facts.GlobalName, post.Facts.Username }
            .Select(v => RecruitmentSignals.SafeIdentity(v)).FirstOrDefault(v => v is not null) ?? "Author unavailable";
        var identity = $"**{name}**\nUsername: {Literal(post.Facts.Username ?? "unavailable", 100)} · ID: {Literal(post.OwnerId, 30)}";
        var avatar = RecruitmentSignals.SafeAvatarUrl(post.Facts.AvatarUrl);
        if (post.AvailabilityUnknown) identity += "\nIdentity last observed; current membership unavailable.";
        var marker = $"recruitment:{post.Id}:public";
        var visible = $"## {RecruitmentPresentation.Heading}\n*{RecruitmentPresentation.Guidance}*\n";
        var foot = string.Join("\n", operations.Append(warnings).Where(v => v.Length > 0));
        return new(checks, alt, needsAck, identity, avatar, marker, visible, foot, post.Id, state.PortfolioSites.Version);
    }
}
