using DiscordBot.Settings.Options;

namespace DiscordBot.Services.Recruitment;

public sealed record RecruitmentAdvisoryView(string Marker, Embed Embed, MessageComponent Components, string ImageDescription);

/// <summary>All actionable information remains in text; the PNG is an initial snapshot only.</summary>
public static class RecruitmentAdvisoryMessage
{
    public static RecruitmentAdvisoryView Build(RecruitmentStateDocument state, RecruitmentPostRecord post,
        RecruitmentOptions options, TimeProvider time)
    {
        var policy = new RecruitmentPolicyEvaluator(options, time);
        RecruitmentEligibility eligibility = policy.EvaluateEligibility(state, post.ThreadId);
        bool enforced = options.Mode == RecruitmentMode.Enforce && post.EnforcementEnrolled;
        bool timeoutEnabled = enforced && options.EnforceGuidelineTimeouts;
        string marker = Marker(state.GuildId, post.ThreadId);
        var embed = new EmbedBuilder()
            .WithTitle(enforced ? "Recruitment · Enforce" : "Recruitment · Advisory practice")
            .WithColor(new Color(62, 158, 177))
            .WithDescription("Keep initial scope, terms, rates and portfolio verification in this public thread. " +
                "Verify work before moving to DMs. These facts and acknowledgement are not an endorsement.")
            .AddField("Acknowledgement", AcknowledgementText(post, time.GetUtcNow(), enforced, timeoutEnabled))
            .AddField("Listing placement", EligibilityText(eligibility))
            .AddField("Payment details", PaymentText(post.Payment))
            .AddField("Known context", Facts(post, policy))
            .AddField("Active policy", ModeText(options, enforced))
            .WithFooter(marker);

        bool open = post.Lifecycle == RecruitmentLifecycle.Open && !post.IsPinned && !post.IsExempt && !post.ClosedRequested;
        bool passed = post.Acknowledgement == RecruitmentAcknowledgement.Passed;
        bool expired = post.ChallengeDeadlineUtc <= time.GetUtcNow();
        string generation = post.Advisory.Generation;
        var controls = new ComponentBuilder()
            .WithButton(expired && !timeoutEnabled ? "New acknowledgement window" : "Acknowledge guidelines",
                $"udc-recruit:{(expired && !timeoutEnabled ? "renew" : "ack")}:{post.ThreadId}:{generation}", ButtonStyle.Primary,
                disabled: !open || passed || generation.Length == 0 || expired && timeoutEnabled)
            .WithButton("Close listing", $"udc-recruit:close:{post.ThreadId}:{generation}", ButtonStyle.Secondary,
                disabled: !open || !passed || enforced && post.AcceptedAtUtc is null)
            .WithButton("Remove my post", $"udc-recruit:remove:{post.ThreadId}:{generation}", ButtonStyle.Danger,
                disabled: !open);
        string alt = $"Initial recruitment snapshot. Account age: {policy.AccountAge(SnowflakeUtils.FromSnowflake(post.AuthorId))}. " +
            $"Server tenure: {policy.ServerTenure(post.Observation.JoinedAtUtc)}. Activity: {ActivityText(post.Activity)}. " +
            $"Payment: {PaymentText(post.Payment)} Keep initial terms and verification public.";
        return new(marker, embed.Build(), controls.Build(), alt);
    }

    public static string Marker(ulong guildId, ulong threadId) => $"udc-recruit-advisory:{guildId}:{threadId}";

    private static string AcknowledgementText(RecruitmentPostRecord post, DateTimeOffset now, bool enforced, bool timeoutEnabled)
    {
        if (post.Lifecycle != RecruitmentLifecycle.Open || post.ClosedRequested)
        {
            return "This listing is closed or unavailable.";
        }
        if (post.Acknowledgement == RecruitmentAcknowledgement.Passed)
        {
            if (post.AcceptedAtUtc is not null) return "Acknowledgement completed; listing accepted.";
            return enforced ? "Acknowledgement completed. Eligibility is checked at the grace deadline; listing acceptance is separate." :
                "Practice acknowledgement completed.";
        }
        if (post.Advisory.Error is not null || post.Advisory.NeedsFreshWindow)
        {
            return "Acknowledgement is paused while the bot checks availability. A fresh full window will be provided after recovery.";
        }
        if (post.ChallengeDeadlineUtc is { } deadline)
        {
            if (deadline <= now)
                return timeoutEnabled ? "The acknowledgement deadline has passed. The bot is verifying evidence before applying the enabled timeout policy." :
                    "Window expired. Start a new window below; no timeout penalty applies.";
            return $"Read this forum's Guidelines, then enter the code in the private form. Deadline: <t:{deadline.ToUnixTimeSeconds()}:F> (<t:{deadline.ToUnixTimeSeconds()}:R>).";
        }
        return "Read this forum's Guidelines. The bot is preparing your acknowledgement window; controls become usable once delivery is confirmed.";
    }

    private static string ModeText(RecruitmentOptions options, bool enforced)
    {
        if (!enforced) return "Practice only: no automatic removal, closure or failure counts. A practice answer does not accept a listing or start a cooldown.";
        List<string> active = [];
        if (options.EnforceGuidelineTimeouts) active.Add("unanswered acknowledgement challenges can be deleted");
        if (options.EnforceLifecycleClosures) active.Add($"Closed and accepted listings unanswered for {options.UnansweredDays} days can be locked/archived");
        if (options.EnforceListingLimits) active.Add("verified but ineligible listings can be locked/archived at the grace deadline");
        return active.Count == 0 ? "Automatic action gates are disabled. Eligible, acknowledged listings can still be accepted at the grace deadline." :
            "Enabled actions: " + string.Join("; ", active) + ". Recovery and evidence checks apply.";
    }

    private static string EligibilityText(RecruitmentEligibility eligibility)
    {
        string finding = eligibility.Kind switch
        {
            RecruitmentEligibilityKind.Eligible => "Placement check: eligible under the planned listing policy.",
            RecruitmentEligibilityKind.ActiveListing => "An accepted listing already occupies this recruiting/for-hire group.",
            RecruitmentEligibilityKind.PendingListing => "An earlier pending post occupies this group's reservation.",
            RecruitmentEligibilityKind.Cooldown => "This group's previous accepted listing is within its repost wait.",
            RecruitmentEligibilityKind.ReviewRequired => "Earlier or incomplete history needs staff review.",
            _ => "This listing is no longer active."
        };
        if (eligibility.NextAllowedAtUtc is { } next)
        {
            finding += $" Earliest date, subject to the other checks: <t:{next.ToUnixTimeSeconds()}:F>.";
        }
        if (eligibility.BlockingThreadId is { } previous)
        {
            finding += $" Previous post: <#{previous}>.";
        }
        if (eligibility.PlacementReminder is { } reminder)
        {
            finding += " " + reminder;
        }
        return finding;
    }

    public static string Facts(RecruitmentPostRecord post, RecruitmentPolicyEvaluator policy) =>
        $"Account age: {policy.AccountAge(SnowflakeUtils.FromSnowflake(post.AuthorId))}. " +
        $"Server tenure: {policy.ServerTenure(post.Observation.JoinedAtUtc)}. Activity: {ActivityText(post.Activity)}. " +
        $"Post created <t:{post.CreatedAtUtc.ToUnixTimeSeconds()}:F>.";

    public static string ActivityText(RecruitmentActivity activity) => activity switch
    {
        RecruitmentActivity.Recorded => "some recorded activity",
        RecruitmentActivity.NoneRecorded => "no activity recorded",
        _ => "unknown"
    };

    public static string PaymentText(RecruitmentPaymentSignal payment) => payment switch
    {
        RecruitmentPaymentSignal.Concrete => "A payment amount was detected. Confirm currency, scope, terms and schedule publicly.",
        RecruitmentPaymentSignal.Missing => "Please add a currency and rate/range or budget, with scope and payment terms.",
        RecruitmentPaymentSignal.Ambiguous => "Clarify guaranteed pay, currency and scope. Revenue share alone is not guaranteed pay.",
        RecruitmentPaymentSignal.NotApplicable => "Unpaid collaboration: make ownership, credit and expectations clear.",
        _ => "Payment details are unknown; review the original offer."
    };
}
