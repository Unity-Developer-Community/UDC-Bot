using DiscordBot.Settings;

namespace DiscordBot.Services.Recruitment;

public static class RecruitmentPresentation
{
    public static string Timestamp(DateTimeOffset date) => $"<t:{date.ToUnixTimeSeconds()}:F>";
    public static string Safe(string value, int max = 180) => new string(value.Take(max)
        .Select(c => char.IsControl(c) || "`*_~|<>@".Contains(c) ? ' ' : c).ToArray());

    public static RecruitmentMessage Public(RecruitmentState state, Listing post, RecruitmentSettings settings, DateTimeOffset now)
    {
        var eligibility = RecruitmentPolicy.Evaluate(state, post, now, settings.CooldownDays);
        var status = post.Status != ListingStatus.Open ? post.Status.ToString() : post.AcceptedAt.HasValue ? "Listing active" :
            post.AcknowledgedAt.HasValue ? "Waiting for eligibility" : "Acknowledgement needed";
        var instructions = new List<string>();
        if (post.Status == ListingStatus.Open && !post.AcceptedAt.HasValue)
        {
            if (!post.AcknowledgedAt.HasValue) instructions.Add("Read this forum's Guidelines and enter the code using **Acknowledge**.");
            instructions.Add(post.Deadline.HasValue ? $"Deadline: {Timestamp(post.Deadline.Value)}." : "Your acknowledgement window starts when these controls are available.");
            if (eligibility.BlockingListingId is not null) instructions.Add($"Your other listing <#{eligibility.BlockingListingId}> occupies your listing slot.");
            if (eligibility.NextAllowedAt.HasValue) instructions.Add($"Creation cooldown ends {Timestamp(eligibility.NextAllowedAt.Value)}.");
        }
        if (post.Status == ListingStatus.Closed && !post.AcceptedAt.HasValue) instructions.Add(Safe(post.LastOutcome, 400));
        if (post.AcceptedAt.HasValue)
        {
            var next = state.Authors.GetValueOrDefault(post.OwnerId)?.LastAcceptedCreation?.AddDays(settings.CooldownDays);
            if (next > now) instructions.Add($"Next listing: {Timestamp(next.Value)}." + (post.Status == ListingStatus.Open ? " Close this listing before replacing it." : ""));
            else if (post.Status == ListingStatus.Open) instructions.Add("Close this listing before posting a replacement.");
        }
        if (post.Status == ListingStatus.Open && (post.Exempt || post.Pinned)) instructions.Add("This listing stays open until its owner or a moderator closes it.");
        if (post.Hold is not null || post.AvailabilityUnknown) instructions.Add("Processing paused; moderator review or Discord recovery is needed.");

        var purpose = state.Forums.First(f => f.Value.ForumId == post.ForumId).Key;
        var paid = purpose.StartsWith("paid-");
        var forHire = purpose.EndsWith("for-hire");
        var embed = new EmbedBuilder().WithTitle("Recruitment · " + status).WithDescription(string.Join("\n", instructions));
        var flagged = false;
        void Field(string name, string value, bool flag, bool inline = true)
        {
            flagged |= flag;
            embed.AddField((flag ? "⚠ " : "") + name, value, inline);
        }
        var accountAge = post.Facts.AccountCreatedAt.HasValue ? post.CreatedAt - post.Facts.AccountCreatedAt.Value : (TimeSpan?)null;
        Field("Account age", accountAge.HasValue
            ? $"{Age(accountAge.Value)} at posting\nCreated <t:{post.Facts.AccountCreatedAt!.Value.ToUnixTimeSeconds()}:d>" : "Unavailable",
            !accountAge.HasValue || accountAge.Value.TotalDays < 30);
        var joined = post.Facts.JoinedAt;
        Field("Joined server", joined.HasValue ? $"<t:{joined.Value.ToUnixTimeSeconds()}:D>" +
            (post.CreatedAt - joined.Value < TimeSpan.FromDays(7) ? "\nJoined less than 7 days before posting" : "") : "Unavailable",
            !joined.HasValue || post.CreatedAt - joined.Value < TimeSpan.FromDays(7));
        Field("Server roles", post.Facts.MemberRoleCount switch
        {
            null => "Unavailable",
            0 => "None assigned. Add relevant skill roles.",
            var count => $"{count} assigned"
        }, post.Facts.MemberRoleCount is null or 0);
        var concrete = post.Facts.Payment == "concrete rate or budget stated";
        var unpaid = post.Facts.Payment == "unpaid collaboration stated";
        var revenueShare = post.Facts.Payment == "revenue-share stated";
        Field("Payment", concrete ? "Rate or budget detected; terms unverified." : revenueShare ? "Revenue share stated; no guaranteed payment detected." :
            unpaid ? paid ? "Unpaid terms detected in a paid forum." : "Unpaid collaboration stated." :
            post.Facts.Payment == "unknown" ? "Starter text unavailable." : paid ? "No concrete rate or budget detected." : "No clear unpaid, revenue-share or payment terms detected.",
            !concrete && (paid || !unpaid), false);
        if (forHire)
            Field("Portfolio / work samples", post.Facts.Portfolio switch
            {
                "work-sample link detected" => "Link detected; authorship and contents unverified.",
                "external link found; purpose unclear" => "External link found. Label your portfolio or work samples.",
                "unknown" => "Starter text unavailable.",
                _ => "No link detected. Add a portfolio, demo or work samples."
            }, post.Facts.Portfolio != "work-sample link detected", false);
        embed.AddField("Before moving to DMs", "Clarify scope, deliverables, timing and payment in this thread first. Verify work samples and agree terms in writing.")
            .WithColor(flagged ? Color.Orange : Color.Blue)
            .WithFooter("Checks use the starter post and server membership; they do not verify identity or endorse the listing.");
        var built = embed.Build();
        var marker = $"recruitment:{post.Id}:public";
        var fallback = $"**{built.Title}**\n{built.Description}\n" + string.Join("\n", built.Fields.Select(f => $"**{f.Name}**: {f.Value}")) + $"\n{built.Footer?.Text}\n||{marker}||";
        return new RecruitmentMessage($"||{marker}||", marker, post.Status == ListingStatus.Open, Embed: built, TextFallback: fallback);
    }

    private static string Age(TimeSpan age)
    {
        var days = Math.Max(0, (int)age.TotalDays);
        if (days >= 365) return $"{days / 365} {(days / 365 == 1 ? "year" : "years")}";
        return $"{days} {(days == 1 ? "day" : "days")}";
    }

    public static RecruitmentMessage Staff(RecruitmentState state, Listing post, RecruitmentSettings settings)
    {
        var author = state.Authors.GetValueOrDefault(post.OwnerId);
        var next = author?.LastAcceptedCreation?.AddDays(settings.CooldownDays);
        var text = $"**Recruitment · {post.Status}** · {Safe(post.Title)}\n" +
            $"Author `{post.OwnerId}` · thread `{post.Id}` · forum `{post.ForumId}`\n" +
            $"https://discord.com/channels/{state.GuildId}/{post.Id}\n" +
            $"Created {Timestamp(post.CreatedAt)} · acknowledged {(post.AcknowledgedAt.HasValue ? "yes" : "no")} · accepted {(post.AcceptedAt.HasValue ? "yes" : "no")}\n" +
            $"Next creation: {(next.HasValue ? Timestamp(next.Value) : "no accepted cooldown")}\n" +
            $"Outcome: {Safe(post.LastOutcome)}\n";
        if (post.PendingAction is { } action)
            text += $"Pending {action.Kind}: {Safe(action.Reason)} · actor `{action.ActorId}` · action `{action.Id}`\n";
        if (post.Hold is not null) text += $"Hold: {Safe(post.Hold)}\n";
        if (post.TimeoutAlertCount >= 3) text += $"**Moderator attention: {post.TimeoutAlertCount} consecutive confirmed Guidelines timeouts.**\n";
        foreach (var audit in post.Audit.TakeLast(3)) text += $"{audit.At:yyyy-MM-dd HH:mm} · `{audit.Actor}` · {Safe(audit.Reason, 180)}\n";
        var marker = $"recruitment:{post.Id}:staff";
        if (text.Length > 1900) text = text[..1880] + "\n… more in stored audit\n";
        return new RecruitmentMessage(text + $"||{marker}||", marker, false);
    }

}
