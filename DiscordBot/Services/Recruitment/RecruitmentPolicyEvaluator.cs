using DiscordBot.Settings.Options;
using DiscordBot.Settings.Validation;

namespace DiscordBot.Services.Recruitment;

/// <summary>Deterministic decisions only. No Discord, database or file operations.</summary>
public sealed class RecruitmentPolicyEvaluator
{
    private readonly RecruitmentOptions _options;
    private readonly TimeProvider _time;

    public RecruitmentPolicyEvaluator(RecruitmentOptions options, TimeProvider time)
    {
        var errors = RecruitmentOptionsValidator.ValidateValues(options);
        if (errors.Count != 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
        _options = options;
        _time = time;
    }

    public RecruitmentEligibility EvaluateEligibility(RecruitmentStateDocument state, ulong threadId)
    {
        var post = state.Posts[threadId];
        var now = _time.GetUtcNow();
        var group = RecruitmentForumClassifier.GroupOf(post.Forum);
        var history = state.Posts.Values.Where(p => p.AuthorId == post.AuthorId && p.ThreadId != threadId).ToArray();
        var recent = history.Where(p => p.Forum != post.Forum && p.CreatedAtUtc <= now &&
                p.CreatedAtUtc > now.AddDays(-_options.CooldownDays))
            .OrderByDescending(p => p.CreatedAtUtc).ThenBy(p => p.ThreadId).Select(p => p.ThreadId).ToArray();
        var sameGroup = history.Where(p => RecruitmentForumClassifier.GroupOf(p.Forum) == group).ToArray();
        var aggregate = state.Authors.GetValueOrDefault(post.AuthorId)?.Groups.GetValueOrDefault(group);
        var waiver = aggregate?.Waiver;
        var next = Later(Cooldown(aggregate?.LastAcceptedCreatedAtUtc, waiver?.ThroughCreatedAtUtc),
            Cooldown(aggregate?.LastAcceptedDeletedAtUtc, waiver?.ThroughDeletedAtUtc));
        foreach (var accepted in sameGroup.Where(p => p.AcceptedAtUtc is not null))
        {
            next = Later(next, Cooldown(accepted.CreatedAtUtc, waiver?.ThroughCreatedAtUtc));
            if (!accepted.DeletionTimeUncertain)
                next = Later(next, Cooldown(accepted.DeletedObservedAtUtc, waiver?.ThroughDeletedAtUtc));
        }

        if (post.Lifecycle != RecruitmentLifecycle.Open || post.ClosedRequested ||
            post.Acknowledgement is RecruitmentAcknowledgement.Cancelled or RecruitmentAcknowledgement.TimedOut)
            return Result(RecruitmentEligibilityKind.Terminal);
        // Replayed acceptance is about this existing listing, not a replacement
        // subject to the aggregate cooldown written by its own first acceptance.
        if (post.AcceptedAtUtc is not null && !post.RequiresReview)
            return Result(RecruitmentEligibilityKind.Eligible);
        if (post.RequiresReview || aggregate?.RequiresReview == true || sameGroup.Any(p =>
                p.RequiresReview || p.Lifecycle == RecruitmentLifecycle.Missing || p.DeletionTimeUncertain))
            return Result(RecruitmentEligibilityKind.ReviewRequired);
        var active = sameGroup.Where(p => p.AcceptedAtUtc is not null && p.Lifecycle == RecruitmentLifecycle.Open)
            .OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.ThreadId).FirstOrDefault();
        if (active is not null)
            return Result(RecruitmentEligibilityKind.ActiveListing, active.ThreadId);
        if (next > now)
            return Result(RecruitmentEligibilityKind.Cooldown);
        var pending = sameGroup.Where(p => p.AcceptedAtUtc is null && p.Lifecycle == RecruitmentLifecycle.Open &&
                !p.ClosedRequested && p.Acknowledgement is RecruitmentAcknowledgement.NotPrompted or
                    RecruitmentAcknowledgement.Pending or RecruitmentAcknowledgement.Passed)
            .Where(p => p.CreatedAtUtc < post.CreatedAtUtc || p.CreatedAtUtc == post.CreatedAtUtc && p.ThreadId < post.ThreadId)
            .OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.ThreadId).FirstOrDefault();
        return pending is null ? Result(RecruitmentEligibilityKind.Eligible) :
            Result(RecruitmentEligibilityKind.PendingListing, pending.ThreadId);

        RecruitmentEligibility Result(RecruitmentEligibilityKind kind, ulong? blocker = null) =>
            new(kind, next, blocker, recent);
    }

    /// <summary>Call inside the state store's serialized UpdateAsync transaction.</summary>
    public RecruitmentEligibility TryAccept(RecruitmentStateDocument state, ulong threadId)
    {
        var result = EvaluateEligibility(state, threadId);
        var post = state.Posts[threadId];
        if (!result.IsEligible || post.Acknowledgement != RecruitmentAcknowledgement.Passed || post.AcceptedAtUtc is not null)
            return result;
        post.AcceptedAtUtc = _time.GetUtcNow();
        if (!state.Authors.TryGetValue(post.AuthorId, out var author))
            state.Authors.Add(post.AuthorId, author = new RecruitmentAuthorRecord { UserId = post.AuthorId });
        var group = RecruitmentForumClassifier.GroupOf(post.Forum);
        if (!author.Groups.TryGetValue(group, out var aggregate))
            author.Groups.Add(group, aggregate = new RecruitmentGroupHistory());
        aggregate.LastAcceptedCreatedAtUtc = Later(aggregate.LastAcceptedCreatedAtUtc, post.CreatedAtUtc);
        return result;
    }

    public RecruitmentAction EvaluateAction(RecruitmentStateDocument state, ulong threadId, DateTimeOffset? decisionAtUtc = null)
    {
        var post = state.Posts[threadId];
        var now = decisionAtUtc ?? _time.GetUtcNow();
        if (!_options.Enabled || _options.Mode != RecruitmentMode.Enforce || !post.EnforcementEnrolled ||
            post.IsExempt || post.IsPinned || post.RequiresReview || post.Lifecycle != RecruitmentLifecycle.Open)
            return RecruitmentAction.None;
        // An explicit owner withdrawal must not turn into an acknowledgement failure.
        if (post.ClosedRequested)
            return _options.EnforceLifecycleClosures ?
                new(RecruitmentActionKind.LockArchive, RecruitmentCloseReason.OwnerClosed) : RecruitmentAction.None;
        if (post.ChallengeEnforceable && post.Acknowledgement == RecruitmentAcknowledgement.Pending &&
            post.PromptedAtUtc is not null && post.ChallengeDeadlineUtc <= now && _options.EnforceGuidelineTimeouts)
            return new(RecruitmentActionKind.Delete, RecruitmentCloseReason.GuidelineTimeout);
        if (post.Acknowledgement == RecruitmentAcknowledgement.Passed && post.AcceptedAtUtc is null &&
            post.ChallengeDeadlineUtc <= now && _options.EnforceListingLimits)
        {
            var eligibility = EvaluateEligibility(state, threadId);
            if (eligibility.Kind is RecruitmentEligibilityKind.ActiveListing or
                RecruitmentEligibilityKind.Cooldown or RecruitmentEligibilityKind.PendingListing)
                return new(RecruitmentActionKind.LockArchive, RecruitmentCloseReason.Ineligible);
        }
        if (_options.EnforceLifecycleClosures && post.AcceptedAtUtc is not null &&
            post.CreatedAtUtc.AddDays(_options.UnansweredDays) <= now &&
            post.FirstQualifyingResponseAtUtc is null && !post.Observation.HistoryUncertain && post.ResponsesCheckedThroughUtc >= now)
            return new(RecruitmentActionKind.LockArchive, RecruitmentCloseReason.Unanswered);
        return RecruitmentAction.None;
    }

    public DateTimeOffset ChallengeDeadline(DateTimeOffset successfullyPromptedAtUtc) =>
        successfullyPromptedAtUtc.AddMinutes(_options.AcknowledgementMinutes);

    public bool IsCorrectCode(RecruitmentPostRecord post, string input) =>
        post.Lifecycle == RecruitmentLifecycle.Open && !post.ClosedRequested &&
        post.Acknowledgement == RecruitmentAcknowledgement.Pending && post.PromptedAtUtc is not null &&
        post.ChallengeDeadlineUtc > _time.GetUtcNow() && input.Length <= 32 &&
        post.AcceptedCodes.Contains(input.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool? IsQualifyingResponse(RecruitmentPostRecord post, RecruitmentResponse response)
    {
        if (!response.IsUserMessage || response.AuthorId == post.AuthorId || response.IsBot || response.IsWebhook ||
            response.CreatedAtUtc <= post.CreatedAtUtc || response.IsModerator == true || response.IsAdministrator == true)
            return false;
        return response.IsModerator is null || response.IsAdministrator is null ? null : true;
    }

    public static RecruitmentActivity Activity(long? xp, int? karma, int? karmaGiven) =>
        xp is null or < 0 || karma is null or < 0 || karmaGiven is null or < 0 ? RecruitmentActivity.Unknown :
        xp > 0 || karma > 0 || karmaGiven > 0 ? RecruitmentActivity.Recorded : RecruitmentActivity.NoneRecorded;

    public string ServerTenure(DateTimeOffset? joinedAtUtc) => AgeBand(joinedAtUtc, [3, 6, 12]);
    public string AccountAge(DateTimeOffset? createdAtUtc) => AgeBand(createdAtUtc, [3, 12]);

    private string AgeBand(DateTimeOffset? start, int[] months)
    {
        var now = _time.GetUtcNow();
        if (start is null || start > now) return "unknown";
        for (var i = 0; i < months.Length; i++)
            if (start.Value.AddMonths(months[i]) > now)
                return i == 0 ? $"< {months[i]} months" : $"{months[i - 1]}–{months[i]} months";
        return $"{months[^1]}+ months";
    }

    private DateTimeOffset? Cooldown(DateTimeOffset? anchor, DateTimeOffset? waivedThrough) =>
        anchor is null || anchor <= waivedThrough ? null : anchor.Value.AddDays(_options.CooldownDays);

    private static DateTimeOffset? Later(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right : right is null || left >= right ? left : right;
}
