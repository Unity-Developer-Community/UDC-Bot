using DiscordBot.Settings;
using System.IO;

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

    public const string Heading = "Recruitment · Automated Observations";
    public const string Guidance = "Automated observations; neither endorsement nor judgment. Verify claims. Keep first contact and key terms here; use DMs only for private information.";

    // Final public model. T05 switches Engine.PublishAsync/ExecuteActionAsync to this method
    // and deletes the interim embed Public method above.
    public static RecruitmentMessage PublicV2(RecruitmentState state, Listing post, RecruitmentSettings settings,
        DateTimeOffset now, Func<IReadOnlyList<RecruitmentObservation>, byte[]>? render = null)
    {
        var checks = post.Facts.Checks;
        var alt = RecruitmentBanner.Alt(checks);
        var needsAck = post.Status == ListingStatus.Open && !post.AcceptedAt.HasValue && !post.AcknowledgedAt.HasValue;
        var operations = new List<string>();
        if (needsAck) operations.Add("**Author:** Read this forum's Guidelines and enter its code with **Acknowledge**." +
            (post.Deadline is { } deadline ? $" Due {Timestamp(deadline)}." : " Your window starts when these controls are available."));
        if (post.Status == ListingStatus.Closed) operations.Add("**Closed:** " + Safe(post.LastOutcome, 220));
        if (post.Hold is not null || post.AvailabilityUnknown) operations.Add("Bot processing is paused; listing details may be incomplete.");
        var warnings = string.Join("\n", post.Facts.Observations.Take(4).Select(o => $"• {Safe(o.Description, 100)}: “{Safe(o.Evidence, 65)}”."));
        var name = new[] { post.Facts.ServerNickname, post.Facts.GlobalName, post.Facts.Username }
            .Select(v => RecruitmentSignals.SafeIdentity(v)).FirstOrDefault(v => v is not null) ?? "Author unavailable";
        var identity = $"**{name}**\nUsername: {Safe(post.Facts.Username ?? "unavailable", 100)} · ID: {Safe(post.OwnerId, 30)}";
        var avatar = RecruitmentSignals.SafeAvatarUrl(post.Facts.AvatarUrl);
        if (post.AvailabilityUnknown) identity += "\nIdentity last observed; current membership unavailable.";
        var marker = $"recruitment:{post.Id}:public";
        var visible = $"## {Heading}\n*{Guidance}*\n";
        var foot = string.Join("\n", operations.Append(warnings).Where(v => v.Length > 0));
        MessageComponent Build(bool image)
        {
            var children = new List<IMessageComponentBuilder> { new TextDisplayBuilder($"## {Heading}") };
            if (image) children.Add(new MediaGalleryBuilder().AddItem("attachment://recruitment-observations.png", RecruitmentBanner.GalleryAlt(checks)));
            else children.Add(new TextDisplayBuilder(string.Join("\n", checks.Select(c => $"**{c.Label}:** {Safe(c.Value, 60)} ({RecruitmentBanner.Status(c)})"))));
            children.Add(new TextDisplayBuilder($"*{Guidance}*"));
            if (foot.Length > 0) children.Add(new TextDisplayBuilder(foot));
            if (avatar is not null) children.Add(new SectionBuilder(new ThumbnailBuilder(new UnfurledMediaItemProperties(avatar), "Author avatar"), new[] { new TextDisplayBuilder(identity) }));
            else children.Add(new TextDisplayBuilder(identity + "\nAvatar unavailable."));
            var row = new ActionRowBuilder().WithButton("Legend", $"recruit:details:{post.Id}", ButtonStyle.Secondary)
                .WithButton("Report concern", $"recruit:report:{post.Id}", ButtonStyle.Secondary)
                .WithButton("Your listing", $"recruit:owner:{post.Id}", ButtonStyle.Secondary);
            if (needsAck) row.WithButton("Acknowledge", $"recruit:ack:{post.Id}", ButtonStyle.Primary);
            children.Add(row);
            children.Add(new TextDisplayBuilder($"||{marker}||"));
            return new ComponentBuilderV2(new[] { new ContainerBuilder(children) }).Build();
        }
        RecruitmentMedia? media = null;
        try { media = new("recruitment-observations.png", (render ?? (c => RecruitmentBanner.Render(c)))(checks), alt); }
        catch (Exception ex) when (ex is ImageMagick.MagickException or IOException or ArgumentException or InvalidOperationException) { /* Text remains complete and operable. */ }
        // Deliberately excludes CDN signatures and SDK-generated component IDs.
        var avatarKey = avatar is null ? "" : new Uri(avatar).GetLeftPart(UriPartial.Path);
        var key = RecruitmentBanner.Identity(RecruitmentBanner.Version + "\n" + state.PortfolioSites.Version + "\n" +
            alt + "\n" + visible + foot + identity + avatarKey + "\n" + needsAck + "\n" + marker + "\n" + (media is not null));
        var fallback = Build(false);
        return new RecruitmentMessage("", marker, true, AcknowledgementRequired: needsAck, ReaderControls: true,
            Format: RecruitmentMessageFormat.ComponentsV2, RenderIdentity: key, Components: media is null ? fallback : Build(true),
            FallbackComponents: fallback, Media: media);
    }

    public static string Details(Listing post)
    {
        return "**Legend · Recruitment observations**\n" + string.Join("\n", post.Facts.Checks.Select(c =>
            $"{c.Label}: {Safe(c.Value, 60)}. {c.Applicability} / {c.Severity}." + (string.IsNullOrEmpty(c.Evidence) ? "" : $" {Safe(c.Evidence, 370)}"))) +
            (post.Facts.Observations.Count == 0 ? "" : "\n**Additional evidence**\n" + string.Join("\n", post.Facts.Observations.Take(4).Select(o => $"• {Safe(o.Description, 140)}: “{Safe(o.Evidence, 180)}”."))) +
            "\nGreen describes an observed fact; yellow asks for clarification; red highlights a missing expected detail or concern. Grey means Unavailable or Not Applicable. Colors never establish trust.\nAccount Age: at posting, green ≥2 years; yellow ≥6 months. Join Date: at posting, green ≥1 year; yellow ≥2 months; later joins are rejoined, with no invented tenure. Server Roles excludes everyone.\nCompensation: monetary rate/budget, unclear or mixed terms, or no monetary pay; hobby is Not Applicable. Duration seeks an explicit work window/availability. Portfolio recognizes specific supported-platform work links, without endorsement; recruiting is Not Applicable; attached samples alone request clarification for paid for-hire.\nDM Requests flags affirmative DM-first instructions, more strongly below 120 body characters. Profile Image describes custom/default appearance only. Post Detail: green ≥300, yellow 120–299, red <120 normalized characters; URLs and markup excluded.\nObservations use starter and membership data; unavailable data is not evidence against the author. Roles, avatars, links and length do not establish trust or authorship. Reports go privately to moderators.";
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
