using DiscordBot.Services.Recruitment.Observation;
using DiscordBot.Services.Recruitment.Policy;
using DiscordBot.Services.Recruitment.Publishing;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment.Actions;

public enum ResponseReview { KeepUnknown, NoQualifyingResponses, QualifyingResponsePresent }

/// <summary>Authenticated staff commands call mutations under the managed work gate; every change retains a reason.</summary>
public sealed class StaffActions(StateStore store, IForumPublisher discord,
    ObservationCoordinator observations, LifecycleExecutor lifecycle,
    IOptions<RecruitmentOptions> options, TimeProvider time)
{
    private DateTimeOffset Now => time.GetUtcNow();

    public async Task<string> StatusAsync(ulong threadId, CancellationToken token)
    {
        var state = await store.InspectAsync(token);
        if (state is null) return "Recruitment has not enrolled a state file.";
        if (state.RetiredThreadIds.Contains(threadId)) return "Detailed records for this terminal thread have expired; it will not be enrolled again.";
        if (!state.Posts.TryGetValue(threadId, out var post)) return "No recruitment record exists for this thread.";
        var eligibility = new PolicyEvaluator(options.Value, time).EvaluateEligibility(state, threadId);
        var author = state.Authors.GetValueOrDefault(post.AuthorId);
        return $"<#{threadId}> · {post.Lifecycle} · acknowledgement {post.Acknowledgement}\n" +
            $"Accepted: {post.AcceptedAtUtc:O} · enforced enrollment: {post.EnforcementEnrolled}\n" +
            $"Eligibility: {eligibility.Kind} · next eligible: {eligibility.NextAllowedAtUtc:O}\n" +
            $"Pinned: {post.IsPinned} · exempt: {post.IsExempt} · review hold: {post.RequiresReview}\n" +
            $"Response coverage uncertain: {post.Observation.HistoryUncertain} · deletion time uncertain: {post.DeletionTimeUncertain}\n" +
            $"Consecutive timeouts: {author?.ConsecutiveTimeouts ?? 0} · pending action: {post.PendingAction?.Id ?? "none"}\n" +
            $"Observation: {post.Observation.Error ?? "available"}\nPublic status: {post.Advisory.Error ?? "available"}";
    }

    public async Task ReconcileAsync(ulong threadId, CancellationToken token)
    {
        await observations.ReconcileAsync(threadId, token);
        await observations.FlushFeedAsync(threadId, token);
    }

    public async Task ExemptAsync(ulong threadId, bool exempt, ulong actorId, string reason, CancellationToken token)
    {
        RequireReason(reason);
        await lifecycle.CancelAsync(threadId, "Staff changed the listing exemption.", token);
        await store.UpdateAsync(state =>
        {
            var post = state.Posts[threadId];
            post.IsExempt = exempt;
            Record(state, post, actorId, exempt ? "Exempted" : "Exemption removed", reason);
            return true;
        }, token);
    }

    public async Task RestartAcknowledgementAsync(ulong threadId, ulong actorId, string reason, CancellationToken token)
    {
        RequireReason(reason);
        var post = (await store.LoadAsync(token))!.Posts[threadId];
        await RequireLiveAsync(post, token);
        if (post.AcceptedAtUtc is not null || post.Lifecycle != ListingLifecycle.Open || post.ClosedRequested || post.IsExempt)
            throw new InvalidOperationException("Only an open, unaccepted, unprotected listing can receive a new acknowledgement window.");
        await lifecycle.CancelAsync(threadId, "Staff requested a fresh acknowledgement window.", token);
        await store.UpdateAsync(state =>
        {
            var saved = state.Posts[threadId];
            saved.Acknowledgement = AcknowledgementStatus.NotPrompted;
            saved.Observation.Imported = false;
            saved.EnforcementEnrolled = options.Value.Mode == RecruitmentMode.Enforce;
            saved.ChallengeEnforceable = false;
            saved.Advisory.NeedsFreshWindow = true;
            Record(state, saved, actorId, "Acknowledgement restarted", reason);
            return true;
        }, token);
    }

    public Task WaiveCooldownAsync(ulong threadId, ulong actorId, string reason, CancellationToken token)
    {
        RequireReason(reason);
        return store.UpdateAsync(state =>
        {
            var post = state.Posts[threadId];
            var author = ListingHistory.Author(state, post);
            var group = ListingHistory.Group(author, post.Forum);
            DateTimeOffset? created = group.LastAcceptedCreatedAtUtc, deleted = group.LastAcceptedDeletedAtUtc;
            foreach (var accepted in state.Posts.Values.Where(previous => previous.AuthorId == post.AuthorId &&
                         previous.AcceptedAtUtc is not null && ForumClassifier.GroupOf(previous.Forum) == ForumClassifier.GroupOf(post.Forum)))
            {
                created = ListingHistory.Later(created, accepted.CreatedAtUtc);
                deleted = ListingHistory.Later(deleted, accepted.DeletedObservedAtUtc);
            }
            group.Waiver = new() { ThroughCreatedAtUtc = created, ThroughDeletedAtUtc = deleted,
                GrantedAtUtc = Now, ActorId = actorId, Reason = reason };
            // The waiver covers these anchors only; future accepted posts/deletions still start their normal wait.
            Record(state, post, actorId, "Cooldown waived for " + ForumClassifier.GroupOf(post.Forum), reason);
            return true;
        }, token);
    }

    public Task ResetTimeoutsAsync(ulong threadId, ulong actorId, string reason, CancellationToken token)
    {
        RequireReason(reason);
        return store.UpdateAsync(state =>
        {
            var post = state.Posts[threadId];
            var author = ListingHistory.Author(state, post);
            author.ConsecutiveTimeouts = 0;
            author.TimeoutAlertActionId = null;
            Record(state, post, actorId, "Timeout count reset", reason);
            return true;
        }, token);
    }

    public async Task ReviewAsync(ulong threadId, ResponseReview responseReview, bool dismiss,
        ulong actorId, string reason, CancellationToken token)
    {
        RequireReason(reason);
        var post = (await store.LoadAsync(token))!.Posts[threadId];
        await RequireLiveAsync(post, token);
        if (post.DeletionTimeUncertain) throw new InvalidOperationException("Resolve the uncertain disappearance before clearing this review hold.");
        await store.UpdateAsync(state =>
        {
            var saved = state.Posts[threadId];
            saved.RequiresReview = false;
            saved.FindingDismissed = dismiss;
            if (responseReview != ResponseReview.KeepUnknown)
            {
                if (responseReview == ResponseReview.NoQualifyingResponses && saved.FirstQualifyingResponseAtUtc is not null)
                    throw new InvalidOperationException("An already observed qualifying reply cannot be erased by a no-response review.");
                saved.HistoryReviewedThroughUtc = Now;
                saved.Observation.HistoryUncertain = false;
                saved.ResponsesCheckedThroughUtc = null; // A fresh read must still cover messages arriving after the staff review.
                if (responseReview == ResponseReview.QualifyingResponsePresent)
                    saved.FirstQualifyingResponseAtUtc ??= Now;
            }
            ClearResolvedGroupHold(state, saved);
            Record(state, saved, actorId, dismiss ? "Finding dismissed" : "Reviewed " + responseReview, reason);
            return true;
        }, token);
    }

    public async Task AdoptAsync(ulong threadId, ulong actorId, string reason, CancellationToken token)
    {
        RequireReason(reason);
        if (options.Value.Mode != RecruitmentMode.Enforce) throw new InvalidOperationException("Adoption requires Enforce mode; Advisory cannot accept listings.");
        var post = (await store.LoadAsync(token))!.Posts[threadId];
        var live = await RequireLiveAsync(post, token);
        var forum = await discord.GetForumAsync(post.ParentChannelId, token);
        bool closedTag = forum.Tags.Any(tag => string.Equals(tag.Name.Trim(), "Closed", StringComparison.OrdinalIgnoreCase) && live.Tags.Contains(tag.Id));
        if (post.Lifecycle != ListingLifecycle.Open || post.ClosedRequested || post.IsExempt || live.Pinned || live.OrdinaryAuthor != true ||
            post.DeletionTimeUncertain || post.PendingAction is { IsPending: true } || closedTag)
            throw new InvalidOperationException("Resolve the listing's protection, lifecycle or pending action before adoption.");
        await store.UpdateAsync(state =>
        {
            var saved = state.Posts[threadId];
            saved.RequiresReview = false;
            saved.Observation.Imported = false;
            saved.Acknowledgement = AcknowledgementStatus.Passed;
            if (!new PolicyEvaluator(options.Value, time).TryAccept(state, threadId).IsEligible)
                throw new InvalidOperationException("Capacity, cooldown or other unresolved history prevents adoption.");
            saved.EnforcementEnrolled = true;
            saved.ChallengeEnforceable = false;
            Record(state, saved, actorId, "Historical listing adopted", reason);
            return true;
        }, token);
    }

    public async Task ResolveMissingAsync(ulong threadId, DateTimeOffset deletedAtUtc, ulong actorId, string reason, CancellationToken token)
    {
        RequireReason(reason);
        var post = (await store.LoadAsync(token))!.Posts[threadId];
        if (post.PendingAction is { IsPending: true })
            throw new InvalidOperationException("Recover or cancel the pending lifecycle action before resolving deletion history.");
        if (post.Lifecycle != ListingLifecycle.Missing || await discord.GetPostAsync(threadId, token) is not null ||
            deletedAtUtc.Offset != TimeSpan.Zero || deletedAtUtc < post.CreatedAtUtc || deletedAtUtc > Now)
            throw new InvalidOperationException("Resolve only a still-missing post with a verified UTC deletion time between creation and now.");
        await store.UpdateAsync(state =>
        {
            var saved = state.Posts[threadId];
            saved.Lifecycle = ListingLifecycle.Deleted;
            saved.DeletedObservedAtUtc = deletedAtUtc;
            saved.ClosedAtUtc = deletedAtUtc;
            saved.RequiresReview = false;
            saved.Observation.Error = null;
            ListingHistory.RecordDeletion(state, saved, Now);
            ClearResolvedGroupHold(state, saved);
            Record(state, saved, actorId, "Missing post resolved", reason);
            return true;
        }, token);
    }

    public async Task ChangeLifecycleAsync(ulong threadId, ActionKind kind, ulong actorId, string reason, CancellationToken token)
    {
        RequireReason(reason);
        if (kind is not (ActionKind.LockArchive or ActionKind.Reopen))
            throw new InvalidOperationException("Deletion requires its separate confirmation.");
        var state = (await store.LoadAsync(token))!;
        var post = state.Posts[threadId];
        await RequireLiveAsync(post, token);
        if (kind == ActionKind.Reopen)
        {
            if (post.Lifecycle != ListingLifecycle.Closed) throw new InvalidOperationException("Only a closed listing can be reopened.");
            if (state.Posts.Values.Any(other => other.ThreadId != threadId && other.AuthorId == post.AuthorId &&
                other.AcceptedAtUtc is not null && other.Lifecycle == ListingLifecycle.Open &&
                ForumClassifier.GroupOf(other.Forum) == ForumClassifier.GroupOf(post.Forum)))
                throw new InvalidOperationException("Another accepted listing occupies this group; close it before reopening.");
        }
        await lifecycle.RequestAsync(threadId, kind, ActionOrigin.Moderator, CloseReason.Moderator, actorId, reason, token);
        await lifecycle.RecoverAsync(threadId, token);
    }

    public async Task<ActionConfirmation> PrepareRemovalAsync(ulong threadId, ulong actorId, string reason, CancellationToken token)
    {
        RequireReason(reason);
        var post = (await store.LoadAsync(token))!.Posts[threadId];
        await RequireLiveAsync(post, token);
        if (post.PendingAction is { IsPending: true }) throw new InvalidOperationException("Resolve the pending lifecycle action first.");
        return await store.UpdateAsync(state =>
        {
            var saved = state.Posts[threadId];
            var confirmation = new ActionConfirmation { Token = GuidelineTemplates.NewToken(), Kind = ActionKind.Delete,
                ActorId = actorId, Origin = ActionOrigin.Moderator, Note = reason, Version = saved.Advisory.Version,
                AcceptedAtUtc = saved.AcceptedAtUtc, ExpiresAtUtc = Now.AddMinutes(2) };
            saved.Advisory.Confirmation = confirmation;
            return confirmation;
        }, token);
    }

    public async Task ConfirmRemovalAsync(ulong threadId, ulong actorId, string tokenValue, CancellationToken token)
    {
        var post = (await store.LoadAsync(token))!.Posts[threadId];
        var confirmation = post.Advisory.Confirmation;
        if (confirmation is null || confirmation.Token != tokenValue || confirmation.ActorId != actorId ||
            confirmation.Origin != ActionOrigin.Moderator || confirmation.ExpiresAtUtc <= Now ||
            confirmation.Version != post.Advisory.Version || confirmation.AcceptedAtUtc != post.AcceptedAtUtc)
            throw new InvalidOperationException("This staff confirmation expired or the listing changed. Request a new confirmation.");
        await RequireLiveAsync(post, token);
        await lifecycle.RequestAsync(threadId, ActionKind.Delete, ActionOrigin.Moderator,
            CloseReason.Moderator, actorId, confirmation.Note, token, confirmation.Token);
        await lifecycle.RecoverAsync(threadId, token);
    }

    private async Task<PublicPost> RequireLiveAsync(PostRecord post, CancellationToken token)
    {
        var live = await discord.GetPostAsync(post.ThreadId, token);
        if (live is null || live.ParentId != post.ParentChannelId || live.AuthorId != post.AuthorId || live.Pinned)
            throw new InvalidOperationException("The post is unavailable, pinned or its identity changed; inspect it directly first.");
        return live;
    }

    private void Record(StateDocument state, PostRecord post, ulong actorId, string operation, string reason)
    {
        post.Audit.Add(new(GuidelineTemplates.NewToken(), Now, actorId, operation, reason));
        post.Advisory.Version++;
        post.Advisory.Confirmation = null;
        post.Advisory.NextCheckAtUtc = null;
        post.EnforcementNextCheckAtUtc = null;
        post.Observation.FeedRetryAtUtc = null;
        ListingHistory.Author(state, post).LastActivityAtUtc = Now;
    }

    private static void ClearResolvedGroupHold(StateDocument state, PostRecord post)
    {
        if (!state.Posts.Values.Any(other => other.AuthorId == post.AuthorId &&
            ForumClassifier.GroupOf(other.Forum) == ForumClassifier.GroupOf(post.Forum) &&
            (other.RequiresReview || other.DeletionTimeUncertain || other.Lifecycle == ListingLifecycle.Missing)))
            ListingHistory.Group(ListingHistory.Author(state, post), post.Forum).RequiresReview = false;
    }

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 200) throw new InvalidOperationException("Provide a staff reason of 1–200 characters.");
    }
}
