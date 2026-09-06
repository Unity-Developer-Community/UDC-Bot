using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment;

/// <summary>Chooses actions; the lifecycle executor independently rechecks evidence immediately before mutation.</summary>
public sealed class RecruitmentEnforcementCoordinator(RecruitmentStateStore store, IRecruitmentPublisher discord,
    RecruitmentObservationCoordinator observations, RecruitmentLifecycleExecutor lifecycle,
    IOptions<RecruitmentOptions> options, TimeProvider time)
{
    private DateTimeOffset Now => time.GetUtcNow();
    public bool HasGaps { get; private set; }
    public string Summary { get; private set; } = "Enforcement has not run.";

    public async Task TickAsync(CancellationToken token)
    {
        if (!options.Value.Enabled || options.Value.Mode != RecruitmentMode.Enforce) return;
        var state = (await store.LoadAsync(token))!;
        var due = state.Posts.Values.Where(post => post.EnforcementEnrolled &&
                (post.Lifecycle == RecruitmentLifecycle.Open || post.PendingAction is { IsPending: true }) &&
                (post.EnforcementNextCheckAtUtc is null || post.EnforcementNextCheckAtUtc <= Now))
            .OrderBy(post => post.EnforcementNextCheckAtUtc).ThenBy(post => post.CreatedAtUtc).ThenBy(post => post.ThreadId)
            .Take(8).Select(post => post.ThreadId).ToArray();
        foreach (ulong id in due)
        {
            await store.UpdateAsync(current => { current.Posts[id].EnforcementNextCheckAtUtc = Now.AddSeconds(30); return true; }, token);
            try { await ProcessPostAsync(id, token); }
            catch (Exception error) when (store.IsHealthy && !token.IsCancellationRequested && error is not System.IO.InvalidDataException)
            {
                await store.UpdateAsync(current =>
                {
                    var post = current.Posts[id];
                    post.EnforcementNextCheckAtUtc = Now.AddMinutes(2);
                    post.Advisory.Error = error is InvalidOperationException ? error.Message[..Math.Min(200, error.Message.Length)] :
                        $"{error.GetType().Name}: enforcement verification failed; review/retry pending.";
                    post.ChallengeEnforceable = false;
                    if (post.Acknowledgement == RecruitmentAcknowledgement.Pending) post.Advisory.NeedsFreshWindow = true;
                    post.Observation.FeedRetryAtUtc = null;
                    return true;
                }, token);
            }
        }
        state = (await store.LoadAsync(token))!;
        int holds = state.Posts.Values.Count(post => post.EnforcementEnrolled &&
            (post.RequiresReview || post.Advisory.Error is not null || post.PendingAction is { IsPending: true }));
        HasGaps = holds > 0;
        Summary = $"Enforce: {holds} listings need review/recovery. Timeout={options.Value.EnforceGuidelineTimeouts}, " +
            $"lifecycle={options.Value.EnforceLifecycleClosures}, listing limits={options.Value.EnforceListingLimits}.";
    }

    private async Task ProcessPostAsync(ulong threadId, CancellationToken token)
    {
        var state = (await store.LoadAsync(token))!;
        var post = state.Posts[threadId];
        if (post.PendingAction is { IsPending: true }) { await lifecycle.RecoverAsync(threadId, token); return; }
        if (post.IsPinned || post.IsExempt || post.RequiresReview || post.Advisory.NeedsFreshWindow) return;

        DateTimeOffset decisionAt = Now;
        await observations.ReconcileAsync(threadId, token);
        state = (await store.LoadAsync(token))!;
        post = state.Posts[threadId];
        var live = await discord.GetPostAsync(threadId, token);
        if (live is null || live.Pinned || live.OrdinaryAuthor != true || post.Observation.Error is not null ||
            post.RequiresReview || post.Lifecycle != RecruitmentLifecycle.Open) return;
        if (live.ParentId != post.ParentChannelId || live.AuthorId != post.AuthorId)
            throw new InvalidOperationException("Listing identity changed; enforcement is held for staff review.");

        if (post.Observation.HasClosedTag && !post.ClosedRequested)
        {
            await store.UpdateAsync(current =>
            {
                var saved = current.Posts[threadId];
                saved.ClosedRequested = true;
                if (saved.AcceptedAtUtc is null) saved.Acknowledgement = RecruitmentAcknowledgement.Cancelled;
                return true;
            }, token);
            state = (await store.LoadAsync(token))!;
            post = state.Posts[threadId];
        }

        var policy = new RecruitmentPolicyEvaluator(options.Value, time);
        if (!post.ClosedRequested && post.Acknowledgement == RecruitmentAcknowledgement.Passed && post.AcceptedAtUtc is null &&
            post.ChallengeDeadlineUtc <= decisionAt)
        {
            await store.UpdateAsync(current =>
            {
                var saved = current.Posts[threadId];
                if (!policy.TryAccept(current, threadId).IsEligible || saved.AcceptedAtUtc is null) return false;
                saved.Advisory.Version++;
                saved.Advisory.Confirmation = null;
                saved.Advisory.NextCheckAtUtc = null;
                saved.Observation.FeedRetryAtUtc = null;
                saved.Audit.Add(new(RecruitmentGuidelines.NewToken(), Now, 0, "Accepted", "Acknowledged and eligible at the grace deadline."));
                RecruitmentHistory.Author(current, saved).LastActivityAtUtc = Now;
                return true;
            }, token);
            state = (await store.LoadAsync(token))!;
        }

        RecruitmentAction action = policy.EvaluateAction(state, threadId, decisionAt);
        if (action.Kind == RecruitmentActionKind.None) return;
        await lifecycle.RequestAsync(threadId, action.Kind, RecruitmentActionOrigin.Automatic, action.Reason!.Value,
            0, $"Policy decision: {action.Reason}.", token);
        await lifecycle.RecoverAsync(threadId, token);
    }
}
