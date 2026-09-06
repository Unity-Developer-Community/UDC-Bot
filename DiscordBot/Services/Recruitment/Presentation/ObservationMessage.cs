using DiscordBot.Services.Recruitment.Policy;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Settings.Options;

namespace DiscordBot.Services.Recruitment.Presentation;

internal static class ObservationMessage
{
    private const int DiscordMessageLimit = 2000;

    public static string Build(StateDocument state, PostRecord post,
        PolicyEvaluator policy, RecruitmentOptions options, string marker, DateTimeOffset now)
    {
        Eligibility eligibility = policy.EvaluateEligibility(state, post.ThreadId);
        List<string> lines =
        [
            $"**Recruitment · {options.Mode}**",
            Format.Sanitize(post.Title),
            PostLink(state.GuildId, post.ThreadId),
            $"Author: `{post.AuthorId}` · {post.Forum} · {post.Lifecycle}",
            DescribeOrigin(post),
            DescribeAuthorFacts(post, policy),
            $"Payment signal: {post.Payment} · {DescribeResponseEvidence(post)}",
            DescribeEligibility(post, eligibility),
            DescribeModeProposal(post, options, now)
        ];

        if (post.PendingAction is { } action)
        {
            lines.Add($"Action `{action.Id}`: {action.Origin} {action.Kind} / {action.Reason}; " +
                $"dispatched: {action.AttemptedAtUtc:O}; completed: {action.CompletedAtUtc:O}; cancelled: {action.CancelledAtUtc:O}.");
            if (action.TimeoutCountAfter >= 3)
                lines.Add($"**Staff review alert:** {action.TimeoutCountAfter} consecutive confirmed timeouts; action `{action.Id}`.");
        }
        if (post.Audit.LastOrDefault() is { } review)
            lines.Add($"Latest record: {review.Operation} by `{review.ActorId}` — {Format.Sanitize(review.Reason)}");
        if (post.FindingDismissed) lines.Add("Staff marked this finding dismissed; recorded history is retained.");
        if (eligibility.PlacementReminder is { } reminder)
        {
            lines.Add(reminder);
        }
        if (post.Observation.Error is { } error)
        {
            lines.Add($"Coverage: {error}");
        }

        if (options.Mode != RecruitmentMode.Observe)
        {
            if (post.Advisory.Error is { } deliveryError) lines.Add($"Public advisory/action: {deliveryError}");
            if (post.Advisory.RenderError is { } imageError) lines.Add(imageError);
            if (state.Forums.GetValueOrDefault(post.ParentChannelId)?.Publication.Error is { } setupError)
                lines.Add($"Forum setup: {setupError}");
        }
        lines.Add(PreviousPostLinks(state, post));
        return AppendRecoveryMarker(string.Join('\n', lines), marker);
    }

    private static string DescribeModeProposal(PostRecord post, RecruitmentOptions options, DateTimeOffset now)
    {
        if (options.Mode == RecruitmentMode.Observe) return DescribeProposal(post, options, now);
        if (options.Mode == RecruitmentMode.Advisory || !post.EnforcementEnrolled)
            return "Practice only; no automatic enforcement for this record.";
        return $"Enrolled for Enforce; timeout={options.EnforceGuidelineTimeouts}, lifecycle={options.EnforceLifecycleClosures}, " +
            $"listing limits={options.EnforceListingLimits}. Current evidence and audit delivery are rechecked before action.";
    }

    private static string DescribeOrigin(PostRecord post)
    {
        string origin = post.Observation.Imported ? "imported, unverified" : "observed after enrollment";
        return $"Origin: {origin} · archived: {post.Observation.Archived} · locked: {post.Observation.Locked}";
    }

    private static string DescribeAuthorFacts(PostRecord post, PolicyEvaluator policy)
    {
        string accountAge = policy.AccountAge(SnowflakeUtils.FromSnowflake(post.AuthorId));
        string serverTenure = policy.ServerTenure(post.Observation.JoinedAtUtc);
        return $"Account: {accountAge} · Server: {serverTenure} · Activity: {post.Activity}";
    }

    private static string DescribeResponseEvidence(PostRecord post)
    {
        if (post.FirstQualifyingResponseAtUtc is not null)
        {
            return "ordinary human response observed (retained if deleted)";
        }
        if (post.ResponsesCheckedThroughUtc is not null && !post.Observation.HistoryUncertain)
        {
            return "no qualifying response in checked history";
        }
        return "response coverage incomplete/uncertain";
    }

    private static string DescribeEligibility(PostRecord post, Eligibility eligibility)
    {
        string description = $"Acknowledgement: {post.Acknowledgement} · hypothetical eligibility: {eligibility.Kind}";
        if (eligibility.NextAllowedAtUtc is { } next)
        {
            description += $" · next eligible: {next:O}";
        }
        if (eligibility.BlockingThreadId is { } blocker)
        {
            description += $" · blocking post: `{blocker}`";
        }
        return description;
    }

    private static string DescribeProposal(PostRecord post, RecruitmentOptions options, DateTimeOffset now)
    {
        string proposal = DescribeAcknowledgementOrClosure(post);
        if (post.Lifecycle == ListingLifecycle.Open && !post.IsPinned && !post.IsExempt &&
            post.CreatedAtUtc.AddDays(options.UnansweredDays) <= now && post.FirstQualifyingResponseAtUtc is null)
        {
            // Age alone is not enough to propose enforcement when acceptance or response evidence is missing.
            proposal += " Unanswered age threshold reached; closure would also require acceptance and verified response coverage.";
        }
        return proposal;
    }

    private static string DescribeAcknowledgementOrClosure(PostRecord post)
    {
        if (post.Lifecycle is ListingLifecycle.Deleted or ListingLifecycle.Missing)
        {
            return "No action proposed for an unavailable post.";
        }
        if (post.IsPinned || post.IsExempt)
        {
            return "Pinned/exempt: no automatic action proposed.";
        }
        if (post.Observation.HasClosedTag)
        {
            return "Would consider Closed lock/archive; no action taken.";
        }
        if (post.Acknowledgement == AcknowledgementStatus.Pending)
        {
            return $"Existing prompt deadline: {post.ChallengeDeadlineUtc:O}; no timeout applied in Observe.";
        }
        if (post.Acknowledgement == AcknowledgementStatus.Passed)
        {
            return "Acknowledgement already recorded; no automatic action in Observe.";
        }
        return "Would request acknowledgement. No prompt delivered and no timeout clock started.";
    }

    private static string PreviousPostLinks(StateDocument state, PostRecord post)
    {
        IEnumerable<string> links = state.Posts.Values
            .Where(previous => previous.AuthorId == post.AuthorId && previous.ThreadId != post.ThreadId)
            .OrderByDescending(previous => previous.CreatedAtUtc)
            .Take(3)
            .Select(previous => PostLink(state.GuildId, previous.ThreadId));
        return string.Join('\n', links);
    }

    private static string PostLink(ulong guildId, ulong threadId) => $"https://discord.com/channels/{guildId}/{threadId}";

    private static string AppendRecoveryMarker(string body, string marker)
    {
        // Recovery looks for this suffix after a send/save interruption. Truncate prose, never the identity.
        string suffix = $"\n`{marker}`";
        int availableBodyLength = DiscordMessageLimit - suffix.Length;
        if (body.Length > availableBodyLength)
        {
            body = body[..(availableBodyLength - 1)] + "…";
        }
        return body + suffix;
    }
}
