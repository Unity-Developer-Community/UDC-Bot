using DiscordBot.Settings.Options;

namespace DiscordBot.Services.Recruitment;

internal static class RecruitmentObservationMessage
{
    private const int DiscordMessageLimit = 2000;

    public static string Build(RecruitmentStateDocument state, RecruitmentPostRecord post,
        RecruitmentPolicyEvaluator policy, RecruitmentOptions options, string marker, DateTimeOffset now)
    {
        RecruitmentEligibility eligibility = policy.EvaluateEligibility(state, post.ThreadId);
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
            options.Mode == RecruitmentMode.Advisory ? "Practice only; automatic enforcement is unavailable." : DescribeProposal(post, options, now)
        ];

        if (eligibility.PlacementReminder is { } reminder)
        {
            lines.Add(reminder);
        }
        if (post.Observation.Error is { } error)
        {
            lines.Add($"Coverage: {error}");
        }

        if (options.Mode == RecruitmentMode.Advisory)
        {
            if (post.Advisory.Error is { } deliveryError) lines.Add($"Public advisory/action: {deliveryError}");
            if (post.Advisory.RenderError is { } imageError) lines.Add(imageError);
            if (state.Forums.GetValueOrDefault(post.ParentChannelId)?.Publication.Error is { } setupError)
                lines.Add($"Forum setup: {setupError}");
        }
        lines.Add(PreviousPostLinks(state, post));
        return AppendRecoveryMarker(string.Join('\n', lines), marker);
    }

    private static string DescribeOrigin(RecruitmentPostRecord post)
    {
        string origin = post.Observation.Imported ? "imported, unverified" : "observed after enrollment";
        return $"Origin: {origin} · archived: {post.Observation.Archived} · locked: {post.Observation.Locked}";
    }

    private static string DescribeAuthorFacts(RecruitmentPostRecord post, RecruitmentPolicyEvaluator policy)
    {
        string accountAge = policy.AccountAge(SnowflakeUtils.FromSnowflake(post.AuthorId));
        string serverTenure = policy.ServerTenure(post.Observation.JoinedAtUtc);
        return $"Account: {accountAge} · Server: {serverTenure} · Activity: {post.Activity}";
    }

    private static string DescribeResponseEvidence(RecruitmentPostRecord post)
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

    private static string DescribeEligibility(RecruitmentPostRecord post, RecruitmentEligibility eligibility)
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

    private static string DescribeProposal(RecruitmentPostRecord post, RecruitmentOptions options, DateTimeOffset now)
    {
        string proposal = DescribeAcknowledgementOrClosure(post);
        if (post.Lifecycle == RecruitmentLifecycle.Open && !post.IsPinned && !post.IsExempt &&
            post.CreatedAtUtc.AddDays(options.UnansweredDays) <= now && post.FirstQualifyingResponseAtUtc is null)
        {
            // Age alone is not enough to propose enforcement when acceptance or response evidence is missing.
            proposal += " Unanswered age threshold reached; closure would also require acceptance and verified response coverage.";
        }
        return proposal;
    }

    private static string DescribeAcknowledgementOrClosure(RecruitmentPostRecord post)
    {
        if (post.Lifecycle is RecruitmentLifecycle.Deleted or RecruitmentLifecycle.Missing)
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
        if (post.Acknowledgement == RecruitmentAcknowledgement.Pending)
        {
            return $"Existing prompt deadline: {post.ChallengeDeadlineUtc:O}; no timeout applied in Observe.";
        }
        if (post.Acknowledgement == RecruitmentAcknowledgement.Passed)
        {
            return "Acknowledgement already recorded; no automatic action in Observe.";
        }
        return "Would request acknowledgement. No prompt delivered and no timeout clock started.";
    }

    private static string PreviousPostLinks(RecruitmentStateDocument state, RecruitmentPostRecord post)
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
