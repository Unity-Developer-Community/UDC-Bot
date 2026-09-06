using DiscordBot.Services.Recruitment;
using DiscordBot.Services.Recruitment.Observation;
using DiscordBot.Services.Recruitment.Policy;
using DiscordBot.Services.Recruitment.Presentation;
using DiscordBot.Services.Recruitment.Publishing;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment.Actions;

/// <summary>Called only through RecruitmentService's work gate. Discord mutations and file commits are recovered separately.</summary>
public sealed class LifecycleExecutor(StateStore store, IForumPublisher discord,
    ObservationCoordinator observations, IOptions<RecruitmentOptions> options, TimeProvider time)
{
    private DateTimeOffset Now => time.GetUtcNow();

    public Task RequestAsync(ulong threadId, ActionKind kind, ActionOrigin origin,
        CloseReason reason, ulong actorId, string note, CancellationToken token, string? actionId = null) =>
        store.UpdateAsync(state =>
        {
            var post = state.Posts[threadId];
            if (post.PendingAction is { IsPending: true }) throw new InvalidOperationException("A lifecycle action is already pending.");
            post.Advisory.Version++;
            post.Advisory.Confirmation = null;
            post.PendingAction = new()
            {
                Id = actionId ?? GuidelineTemplates.NewToken(), Kind = kind, Origin = origin, Reason = reason,
                ActorId = actorId, Note = note, RequestedAtUtc = Now, ExpectedVersion = post.Advisory.Version,
                AcceptedAtUtc = post.AcceptedAtUtc, ReviewRequiredAtRequest = post.RequiresReview
            };
            post.EnforcementNextCheckAtUtc = null;
            post.Advisory.NextCheckAtUtc = null;
            post.Observation.FeedRetryAtUtc = null;
            return true;
        }, token);

    public async Task RecoverAsync(ulong threadId, CancellationToken token)
    {
        RequirePublicMode();
        var state = (await store.LoadAsync(token))!;
        var post = state.Posts[threadId];
        if (post.PendingAction is not { IsPending: true } action) return;

        PublicPost? live = await discord.GetPostAsync(threadId, token);
        if (await OutcomeConfirmedAsync(live, action.Kind, token))
        {
            // An outcome can arrive after cancellation or a lost response. Record it before considering another request.
            await CompleteAsync(threadId, live, token);
            return;
        }
        if (post.Advisory.Version != action.ExpectedVersion || post.AcceptedAtUtc != action.AcceptedAtUtc)
        {
            await CancelAsync(threadId, "Listing state changed before execution.", token);
            return;
        }
        ValidateIdentity(post, live, action.Origin);
        if (action.Origin != ActionOrigin.Moderator && (post.IsExempt || post.IsPinned || live!.Pinned))
        {
            await CancelAsync(threadId, "Listing is protected; the action was cancelled.", token);
            return;
        }

        // Automatic deletion requires a freshly confirmed audit entry containing this exact intent.
        if (action.Origin == ActionOrigin.Automatic && !await observations.FlushFeedAsync(threadId, token))
            throw new InvalidOperationException("Staff audit delivery is unavailable; automatic action is paused.");
        // Refresh absence evidence after audit I/O, immediately before the final identity check and dispatch.
        if (action.Origin == ActionOrigin.Automatic && !await RevalidateAutomaticAsync(threadId, token)) return;

        state = (await store.LoadAsync(token))!;
        post = state.Posts[threadId];
        live = await discord.GetPostAsync(threadId, token);
        if (await OutcomeConfirmedAsync(live, action.Kind, token)) { await CompleteAsync(threadId, live, token); return; }
        ValidateIdentity(post, live, action.Origin);
        token.ThrowIfCancellationRequested();
        RequirePublicMode();
        if (action.Origin == ActionOrigin.Automatic && !GateEnabled(action))
        {
            await CancelAsync(threadId, "The enforcement gate is disabled.", token);
            return;
        }
        await store.UpdateAsync(current => { current.Posts[threadId].PendingAction!.AttemptedAtUtc ??= Now; return true; }, token);
        await discord.ApplyLifecycleActionAsync(live!, action.Kind, state.Forums[post.ParentChannelId].Publication.ClosedTagId,
            action.Id, token);
        live = await discord.GetPostAsync(threadId, token);
        if (!await OutcomeConfirmedAsync(live, action.Kind, token)) throw new InvalidOperationException("Discord has not confirmed the lifecycle outcome; recovery will check again.");
        await CompleteAsync(threadId, live, token);
    }

    private async Task<bool> RevalidateAutomaticAsync(ulong threadId, CancellationToken token)
    {
        DateTimeOffset decisionAt = Now;
        await observations.ReconcileAsync(threadId, token);
        var state = (await store.LoadAsync(token))!;
        var post = state.Posts[threadId];
        var action = post.PendingAction!;
        var expected = new PolicyEvaluator(options.Value, time).EvaluateAction(state, threadId, decisionAt);
        if (!GateEnabled(action) || expected.Kind != action.Kind || expected.Reason != action.Reason ||
            post.Advisory.NeedsFreshWindow || post.Observation.Error is not null || post.Advisory.Error is not null)
        {
            await CancelAsync(threadId, "Current evidence or gates no longer authorize this automatic action.", token);
            return false;
        }

        if (action.Reason != CloseReason.GuidelineTimeout) return true;
        var publication = state.Forums[post.ParentChannelId].Publication;
        var forum = await discord.GetForumAsync(post.ParentChannelId, token);
        bool topicVisible = publication.Confirmed is not null && publication.Error is null &&
            GuidelineTemplates.Hash(forum.Topic) == publication.Confirmed.TopicHash;
        bool promptVisible = post.AdvisoryMessageId is { } messageId && await discord.EditAdvisoryAsync(threadId, messageId,
            AdvisoryMessage.Build(state, post, options.Value, time), token);
        if (!topicVisible || !promptVisible)
        {
            await store.UpdateAsync(current =>
            {
                var saved = current.Posts[threadId];
                saved.ChallengeEnforceable = false;
                saved.Advisory.NeedsFreshWindow = true;
                if (!promptVisible) saved.AdvisoryMessageId = null;
                return true;
            }, token);
            await CancelAsync(threadId, "Guidelines or prompt are unavailable; a fresh window is required.", token);
            return false;
        }
        return true;
    }

    public Task CancelAsync(ulong threadId, string explanation, CancellationToken token) => store.UpdateAsync(state =>
    {
        var post = state.Posts[threadId];
        if (post.PendingAction is not { IsPending: true } action) return false;
        action.CancelledAtUtc = Now;
        post.Audit.Add(new(action.Id, Now, action.ActorId, "Cancelled " + action.Kind, explanation));
        post.Advisory.Version++;
        post.Observation.FeedRetryAtUtc = null;
        return true;
    }, token);

    private Task CompleteAsync(ulong threadId, PublicPost? live, CancellationToken token) => store.UpdateAsync(state =>
    {
        var post = state.Posts[threadId];
        if (post.PendingAction is not { IsPending: true } action) return false;
        action.CompletedAtUtc = Now;
        post.Advisory.Error = null;
        post.ChallengeEnforceable = false;
        post.Advisory.Version++;
        post.Advisory.NextCheckAtUtc = null;
        post.Observation.FeedRetryAtUtc = null;
        string operation = action.AttemptedAtUtc is null ? "Observed " + action.Kind : action.Origin + " " + action.Kind;
        post.Audit.Add(new(action.Id, Now, action.ActorId, operation, action.Note));
        if (state.Authors.TryGetValue(post.AuthorId, out var existingAuthor)) existingAuthor.LastActivityAtUtc = Now;

        if (action.Kind == ActionKind.Reopen)
        {
            post.Lifecycle = ListingLifecycle.Open;
            post.ClosedRequested = false;
            post.ClosedAtUtc = null;
            post.CloseReason = null;
            post.IsExempt = true; // Staff can deliberately remove this hold after deciding the next lifecycle policy.
            post.Observation.Archived = false;
            post.Observation.Locked = false;
            post.Observation.HasClosedTag = false;
            post.AppliedTagIds = live!.Tags;
            return true;
        }

        if (post.Lifecycle == ListingLifecycle.Missing && action.AttemptedAtUtc is not null)
            post.RequiresReview = action.ReviewRequiredAtRequest;
        post.Lifecycle = action.Kind == ActionKind.Delete ? ListingLifecycle.Deleted : ListingLifecycle.Closed;
        post.ClosedRequested = true;
        post.ClosedAtUtc ??= Now;
        post.CloseReason = action.Origin == ActionOrigin.Automatic && action.AttemptedAtUtc is null &&
            action.Kind == ActionKind.Delete ? null : action.Reason;
        if (post.AcceptedAtUtc is null) post.Acknowledgement = AcknowledgementStatus.Cancelled;
        if (action.Kind == ActionKind.Delete) ListingHistory.RecordDeletion(state, post, Now);
        else if (!live!.Tags.Contains(state.Forums[post.ParentChannelId].Publication.ClosedTagId ?? 0))
            post.Advisory.Error = "Listing closed; Closed tag was unavailable or could not fit. Existing tags were retained.";

        // A merely queued intent does not prove the bot removed the post. Count only a dispatched, confirmed timeout.
        if (action.Origin == ActionOrigin.Automatic && action.Reason == CloseReason.GuidelineTimeout &&
            action.AttemptedAtUtc is not null)
        {
            post.Acknowledgement = AcknowledgementStatus.TimedOut;
            var author = ListingHistory.Author(state, post);
            author.LastActivityAtUtc = Now;
            author.ConsecutiveTimeouts = checked(author.ConsecutiveTimeouts + 1);
            action.TimeoutCountAfter = author.ConsecutiveTimeouts;
            if (author.ConsecutiveTimeouts >= 3) author.TimeoutAlertActionId = action.Id;
        }
        return true;
    }, token);

    private bool GateEnabled(LifecycleAction action)
    {
        var settings = options.Value;
        if (!settings.Enabled || settings.Mode != RecruitmentMode.Enforce) return false;
        return action.Reason switch
        {
            CloseReason.GuidelineTimeout => settings.EnforceGuidelineTimeouts,
            CloseReason.Ineligible => settings.EnforceListingLimits,
            CloseReason.OwnerClosed or CloseReason.Unanswered => settings.EnforceLifecycleClosures,
            _ => false
        };
    }

    private void RequirePublicMode()
    {
        if (!options.Value.Enabled || options.Value.Mode == RecruitmentMode.Observe)
            throw new InvalidOperationException("Public recruitment actions are unavailable in the current mode.");
    }

    private static void ValidateIdentity(PostRecord post, PublicPost? live, ActionOrigin origin)
    {
        if (live is null || live.ParentId != post.ParentChannelId || live.AuthorId != post.AuthorId || live.Pinned ||
            origin != ActionOrigin.Moderator && live.OrdinaryAuthor != true)
            throw new InvalidOperationException("The post is unavailable, pinned, protected, or its owner could not be verified.");
    }

    private async Task<bool> OutcomeConfirmedAsync(PublicPost? live, ActionKind kind, CancellationToken token)
    {
        if (kind == ActionKind.Delete) return live is null;
        if (kind == ActionKind.LockArchive) return live is { Archived: true, Locked: true };
        if (kind != ActionKind.Reopen || live is not { Archived: false, Locked: false }) return false;
        var forum = await discord.GetForumAsync(live.ParentId, token);
        return !forum.Tags.Any(tag => string.Equals(tag.Name.Trim(), "Closed", StringComparison.OrdinalIgnoreCase) && live.Tags.Contains(tag.Id));
    }
}
