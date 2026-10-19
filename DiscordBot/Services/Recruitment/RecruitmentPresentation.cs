using DiscordBot.Settings;
using System.IO;

namespace DiscordBot.Services.Recruitment;

public static class RecruitmentPresentation
{
    public static string Timestamp(DateTimeOffset date) => $"<t:{date.ToUnixTimeSeconds()}:F>";
    public static string Safe(string value, int max = 180) => new string(value.Take(max)
        .Select(c => char.IsControl(c) || "`*_~|<>@[]\\".Contains(c) ? ' ' : c).ToArray());

    public const string Heading = "Recruitment · Automated Observations";
    public const string Guidance = "Automated observations; neither endorsement nor judgment. Verify claims. Keep first contact and key terms here; use DMs only for private information.";

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
        string? fileName = null;
        MessageComponent Build(bool image)
        {
            var children = new List<IMessageComponentBuilder> { new TextDisplayBuilder($"## {Heading}") };
            if (image) children.Add(new MediaGalleryBuilder().AddItem("attachment://" + fileName, RecruitmentBanner.GalleryAlt(checks)));
            else children.Add(new TextDisplayBuilder(string.Join("\n", checks.Select(c => $"{RecruitmentBanner.Icon(c)} **{c.Label}:** {Safe(c.Value, 60)}"))));
            children.Add(new TextDisplayBuilder($"*{Guidance}*"));
            if (foot.Length > 0) children.Add(new TextDisplayBuilder(foot));
            if (image && avatar is not null) children.Add(new SectionBuilder(new ThumbnailBuilder(new UnfurledMediaItemProperties(avatar), "Author avatar"), new[] { new TextDisplayBuilder(identity) }));
            else children.Add(new TextDisplayBuilder(identity + (avatar is null ? "\nAvatar unavailable." : "\nAvatar image omitted; see Profile Image observation.")));
            var row = new ActionRowBuilder().WithButton("Legend", $"recruit:details:{post.Id}", ButtonStyle.Secondary)
                .WithButton("Report concern", $"recruit:report:{post.Id}", ButtonStyle.Secondary)
                .WithButton("Your listing", $"recruit:owner:{post.Id}", ButtonStyle.Secondary);
            if (needsAck) row.WithButton("Acknowledge", $"recruit:ack:{post.Id}", ButtonStyle.Primary);
            children.Add(row);
            children.Add(new TextDisplayBuilder($"||{marker}||"));
            return new ComponentBuilderV2(new[] { new ContainerBuilder(children) }).Build();
        }
        RecruitmentMedia? media = null;
        try
        {
            var bytes = (render ?? (c => RecruitmentBanner.Render(c)))(checks);
            fileName = "recruitment-observations-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() + ".png";
            media = new(fileName, bytes, alt);
        }
        catch (Exception ex) when (ex is ImageMagick.MagickException or IOException or ArgumentException or InvalidOperationException) { /* Text remains complete and operable. */ }
        // Deliberately excludes CDN signatures and SDK-generated component IDs.
        var avatarKey = avatar is null ? "" : new Uri(avatar).GetLeftPart(UriPartial.Path);
        var key = RecruitmentBanner.Identity(RecruitmentBanner.Version + "\n" + state.PortfolioSites.Version + "\n" +
            alt + "\n" + visible + foot + identity + avatarKey + "\n" + needsAck + "\n" + marker + "\n" + fileName);
        var fallback = Build(false);
        return new RecruitmentMessage("", marker, true, AcknowledgementRequired: needsAck, ReaderControls: true,
            Format: RecruitmentMessageFormat.ComponentsV2, RenderIdentity: key, Components: media is null ? fallback : Build(true),
            FallbackComponents: fallback, Media: media);
    }

    public static string Details(Listing post)
    {
        return "**Legend · Recruitment observations**\n" + string.Join("\n", post.Facts.Checks.Select(c =>
            $"{RecruitmentBanner.Icon(c)} **{c.Label}:** {Safe(c.Value, 60)}" +
            (c.Id == "post-detail" || string.IsNullOrWhiteSpace(c.Evidence) ? "" : $" — {Safe(c.Evidence, 120)}"))) +
            (post.Facts.Observations.Count == 0 ? "" : "\n\n**Additional evidence**\n" + string.Join("\n", post.Facts.Observations.Take(4).Select(o => $"• **{Safe(o.Description, 80)}:** “{Safe(o.Evidence, 120)}”."))) +
            "\n\n🟢 Observed fact · 🔶 Clarification needed · 🟥 Missing detail or concern · ⚪ Unavailable or not applicable" +
            "\nAges are measured at posting. Job Duration looks for a work window or availability. Portfolio links are detected, not verified. Post Detail excludes links and formatting; length does not prove quality." +
            "\nThese observations do not establish trust or authorship; unavailable data is not evidence against the author. Use **Report concern** to contact moderators privately.";
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
