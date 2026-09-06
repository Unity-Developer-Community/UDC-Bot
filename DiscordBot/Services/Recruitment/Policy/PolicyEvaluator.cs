using DiscordBot.Services.Recruitment.State;
using DiscordBot.Settings.Options;
using DiscordBot.Settings.Validation;

namespace DiscordBot.Services.Recruitment.Policy;

/// <summary>Deterministic decisions only. No Discord, database or file operations.</summary>
public sealed class PolicyEvaluator
{
    private readonly RecruitmentOptions _options;
    private readonly TimeProvider _time;

    public PolicyEvaluator(RecruitmentOptions options, TimeProvider time)
    {
        var errors = RecruitmentOptionsValidator.ValidateValues(options);
        if (errors.Count != 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
        _options = options;
        _time = time;
    }

    public Eligibility EvaluateEligibility(StateDocument state, ulong threadId)
    {
        var post = state.Posts[threadId];
        var now = _time.GetUtcNow();
        var group = ForumClassifier.GroupOf(post.Forum);
        var history = state.Posts.Values.Where(p => p.AuthorId == post.AuthorId && p.ThreadId != threadId).ToArray();
        var recent = history.Where(p => p.Forum != post.Forum && p.CreatedAtUtc <= now &&
                p.CreatedAtUtc > now.AddDays(-_options.CooldownDays))
            .OrderByDescending(p => p.CreatedAtUtc).ThenBy(p => p.ThreadId).Select(p => p.ThreadId).ToArray();
        var sameGroup = history.Where(p => ForumClassifier.GroupOf(p.Forum) == group).ToArray();
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

        if (post.Lifecycle != ListingLifecycle.Open || post.ClosedRequested ||
            post.Acknowledgement is AcknowledgementStatus.Cancelled or AcknowledgementStatus.TimedOut)
            return Result(EligibilityKind.Terminal);
        // Replayed acceptance is about this existing listing, not a replacement
        // subject to the aggregate cooldown written by its own first acceptance.
        if (post.AcceptedAtUtc is not null && !post.RequiresReview)
            return Result(EligibilityKind.Eligible);
        if (post.RequiresReview || aggregate?.RequiresReview == true || sameGroup.Any(p =>
                p.RequiresReview || p.Lifecycle == ListingLifecycle.Missing || p.DeletionTimeUncertain))
            return Result(EligibilityKind.ReviewRequired);
        var active = sameGroup.Where(p => p.AcceptedAtUtc is not null && p.Lifecycle == ListingLifecycle.Open)
            .OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.ThreadId).FirstOrDefault();
        if (active is not null)
            return Result(EligibilityKind.ActiveListing, active.ThreadId);
        if (next > now)
            return Result(EligibilityKind.Cooldown);
        var pending = sameGroup.Where(p => p.AcceptedAtUtc is null && p.Lifecycle == ListingLifecycle.Open &&
                !p.ClosedRequested && p.Acknowledgement is AcknowledgementStatus.NotPrompted or
                    AcknowledgementStatus.Pending or AcknowledgementStatus.Passed)
            .Where(p => p.CreatedAtUtc < post.CreatedAtUtc || p.CreatedAtUtc == post.CreatedAtUtc && p.ThreadId < post.ThreadId)
            .OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.ThreadId).FirstOrDefault();
        return pending is null ? Result(EligibilityKind.Eligible) :
            Result(EligibilityKind.PendingListing, pending.ThreadId);

        Eligibility Result(EligibilityKind kind, ulong? blocker = null) =>
            new(kind, next, blocker, recent);
    }

    /// <summary>Call inside the state store's serialized UpdateAsync transaction.</summary>
    public Eligibility TryAccept(StateDocument state, ulong threadId)
    {
        var result = EvaluateEligibility(state, threadId);
        var post = state.Posts[threadId];
        if (!result.IsEligible || post.Acknowledgement != AcknowledgementStatus.Passed || post.AcceptedAtUtc is not null)
            return result;
        post.AcceptedAtUtc = _time.GetUtcNow();
        if (!state.Authors.TryGetValue(post.AuthorId, out var author))
            state.Authors.Add(post.AuthorId, author = new AuthorRecord { UserId = post.AuthorId });
        var group = ForumClassifier.GroupOf(post.Forum);
        if (!author.Groups.TryGetValue(group, out var aggregate))
            author.Groups.Add(group, aggregate = new GroupHistory());
        aggregate.LastAcceptedCreatedAtUtc = Later(aggregate.LastAcceptedCreatedAtUtc, post.CreatedAtUtc);
        return result;
    }

    public PolicyAction EvaluateAction(StateDocument state, ulong threadId, DateTimeOffset? decisionAtUtc = null)
    {
        var post = state.Posts[threadId];
        var now = decisionAtUtc ?? _time.GetUtcNow();
        if (!_options.Enabled || _options.Mode != RecruitmentMode.Enforce || !post.EnforcementEnrolled ||
            post.IsExempt || post.IsPinned || post.RequiresReview || post.Lifecycle != ListingLifecycle.Open)
            return PolicyAction.None;
        // An explicit owner withdrawal must not turn into an acknowledgement failure.
        if (post.ClosedRequested)
            return _options.EnforceLifecycleClosures ?
                new(ActionKind.LockArchive, CloseReason.OwnerClosed) : PolicyAction.None;
        if (post.ChallengeEnforceable && post.Acknowledgement == AcknowledgementStatus.Pending &&
            post.PromptedAtUtc is not null && post.ChallengeDeadlineUtc <= now && _options.EnforceGuidelineTimeouts)
            return new(ActionKind.Delete, CloseReason.GuidelineTimeout);
        if (post.Acknowledgement == AcknowledgementStatus.Passed && post.AcceptedAtUtc is null &&
            post.ChallengeDeadlineUtc <= now && _options.EnforceListingLimits)
        {
            var eligibility = EvaluateEligibility(state, threadId);
            if (eligibility.Kind is EligibilityKind.ActiveListing or
                EligibilityKind.Cooldown or EligibilityKind.PendingListing)
                return new(ActionKind.LockArchive, CloseReason.Ineligible);
        }
        if (_options.EnforceLifecycleClosures && post.AcceptedAtUtc is not null &&
            post.CreatedAtUtc.AddDays(_options.UnansweredDays) <= now &&
            post.FirstQualifyingResponseAtUtc is null && !post.Observation.HistoryUncertain && post.ResponsesCheckedThroughUtc >= now)
            return new(ActionKind.LockArchive, CloseReason.Unanswered);
        return PolicyAction.None;
    }

    public DateTimeOffset ChallengeDeadline(DateTimeOffset successfullyPromptedAtUtc) =>
        successfullyPromptedAtUtc.AddMinutes(_options.AcknowledgementMinutes);

    public bool IsCorrectCode(PostRecord post, string input) =>
        post.Lifecycle == ListingLifecycle.Open && !post.ClosedRequested &&
        post.Acknowledgement == AcknowledgementStatus.Pending && post.PromptedAtUtc is not null &&
        post.ChallengeDeadlineUtc > _time.GetUtcNow() && input.Length <= 32 &&
        post.AcceptedCodes.Contains(input.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool? IsQualifyingResponse(PostRecord post, ReplyEvidence response)
    {
        if (!response.IsUserMessage || response.AuthorId == post.AuthorId || response.IsBot || response.IsWebhook ||
            response.CreatedAtUtc <= post.CreatedAtUtc || response.IsModerator == true || response.IsAdministrator == true)
            return false;
        return response.IsModerator is null || response.IsAdministrator is null ? null : true;
    }

    public static ActivityStatus Activity(long? xp, int? karma, int? karmaGiven) =>
        xp is null or < 0 || karma is null or < 0 || karmaGiven is null or < 0 ? ActivityStatus.Unknown :
        xp > 0 || karma > 0 || karmaGiven > 0 ? ActivityStatus.Recorded : ActivityStatus.NoneRecorded;

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
