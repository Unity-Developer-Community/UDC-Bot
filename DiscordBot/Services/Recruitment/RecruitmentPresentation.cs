using DiscordBot.Settings;

namespace DiscordBot.Services.Recruitment;

public static class RecruitmentPresentation
{
    public static string Timestamp(DateTimeOffset date) => $"<t:{date.ToUnixTimeSeconds()}:F>";
    public static string Safe(string value, int max = 180) => new string(value.Take(max)
        .Select(c => char.IsControl(c) || "`*_~|<>@[]\\".Contains(c) ? ' ' : c).ToArray());

    public static RecruitmentMessage Public(RecruitmentState state, Listing post, RecruitmentSettings settings, DateTimeOffset now)
    {
        var purpose = state.Forums.First(f => f.Value.ForumId == post.ForumId).Key;
        var lines = new List<string>();
        var accountAge = post.Facts.AccountCreatedAt is { } created ? Age(post.CreatedAt - created) : "unavailable";
        var membership = post.Facts.JoinedAt is { } joined ? joined > post.CreatedAt ? "joined/rejoined after posting" : Age(post.CreatedAt - joined) : "unavailable";
        lines.Add($"At posting · account age: **{accountAge}** · in this server: **{membership}**.");
        var notes = new List<string>();
        if (post.Facts.Payment == "unknown") notes.Add("Starter text is unavailable; payment details could not be checked.");
        else if (purpose.StartsWith("paid-") && post.Facts.Payment != "concrete rate or budget stated" ||
            post.Facts.Payment.StartsWith("mixed") || post.Facts.Payment == "revenue-share stated")
        {
            var summary = post.Facts.Payment switch
            {
                "unpaid collaboration stated" => "Unpaid terms appear in a paid forum",
                "revenue-share stated" => "Revenue share is stated; guaranteed payment is unclear",
                "mixed compensation terms; clarify what is guaranteed" => "Mixed compensation terms; clarify what is guaranteed",
                _ => "A clear compensation rate or budget was not found"
            };
            notes.Add(summary + (post.Facts.PaymentEvidence.Length > 0 ? $": “{Safe(post.Facts.PaymentEvidence, 110)}”." : "."));
        }
        if (purpose.EndsWith("for-hire") && post.Facts.Portfolio is not ("work-sample link detected" or "media sample attached"))
            notes.Add(post.Facts.Portfolio == "unknown" ? "Work-sample information is unavailable." :
                post.Facts.Portfolio == "external link found; purpose unclear" ? "A link is supplied; its purpose as a work sample is unclear." : "No portfolio link or attached media sample was found.");
        notes.AddRange(post.Facts.Observations.Take(3).Select(o => $"{o.Description}: “{Safe(o.Evidence, 110)}”."));
        if (notes.Count > 0) lines.Add("\n**Things to check**\n" + string.Join("\n", notes.Select(n => "• " + n)));
        lines.Add("\nClarify scope, work samples and payment terms here before moving to DMs.");
        var needsAck = post.Status == ListingStatus.Open && !post.AcceptedAt.HasValue && !post.AcknowledgedAt.HasValue;
        if (needsAck)
            lines.Add("\n**Author:** Read this forum's Guidelines and enter its code with **Acknowledge**." +
                (post.Deadline is { } deadline ? $" Due {Timestamp(deadline)}." : " Your window starts when these controls are available."));
        if (post.Status == ListingStatus.Closed && !post.AcceptedAt.HasValue) lines.Add("\n**Closed:** " + Safe(post.LastOutcome, 220));
        if (post.Hold is not null || post.AvailabilityUnknown) lines.Add("\nBot processing is paused; listing details may be incomplete.");
        var embed = new EmbedBuilder().WithTitle(post.Status == ListingStatus.Closed ? "Recruitment · closed" : "Recruitment · reader notes")
            .WithDescription(string.Join("\n", lines)).WithColor(new Color(0x747F8D))
            .WithFooter("Automated observations; identity and work are unverified.").Build();
        var marker = $"recruitment:{post.Id}:public";
        var fallback = $"**{embed.Title}**\n{embed.Description}\n{embed.Footer?.Text}\n||{marker}||";
        return new RecruitmentMessage($"||{marker}||", marker, true, Embed: embed, TextFallback: fallback,
            AcknowledgementRequired: needsAck, ReaderControls: true);
    }

    public static string Details(Listing post)
    {
        var facts = post.Facts;
        return "**Listing details**\n" +
            $"Account created: {(facts.AccountCreatedAt is { } created ? Timestamp(created) : "unavailable")}\n" +
            $"Joined this server: {(facts.JoinedAt is { } joined ? Timestamp(joined) : "unavailable")}\n" +
            $"Profile image: {facts.HasCustomAvatar switch { true => "custom global or server avatar", false => "default Discord avatar", _ => "unavailable" }}\n" +
            $"Server roles (excluding everyone): {facts.MemberRoleCount?.ToString() ?? "unavailable"}. Roles and avatars do not establish trust.\n" +
            $"Compensation: {Safe(facts.Payment)}" + (facts.PaymentEvidence.Length > 0 ? $" · “{Safe(facts.PaymentEvidence, 370)}”" : "") + "\n" +
            $"Work samples: {Safe(facts.Portfolio)}. Links and attachments are not verified.\n{Safe(facts.TagAdvice)}\n" +
            "Observations use the starter post and available membership data. Missing data is not evidence against the author. Reports go privately to moderators; the bot cannot inspect other people's DMs.";
    }

    private static string Age(TimeSpan age)
    {
        var days = Math.Max(0, (int)age.TotalDays);
        if (days >= 365) return $"{days / 365} {(days / 365 == 1 ? "year" : "years")}";
        return days == 0 ? "under 1 day" : $"{days} {(days == 1 ? "day" : "days")}";
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
