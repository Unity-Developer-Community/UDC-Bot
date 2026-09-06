using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment;

/// <summary>Owner intent only. Every submission is serialized with reconciliation by RecruitService.</summary>
public sealed class RecruitmentOwnerActions(RecruitmentStateStore store, IRecruitmentPublisher discord,
    RecruitmentGuidelinePublisher guidelines, IOptions<RecruitmentOptions> options,
    IOptions<DiscordGuildOptions> guild, TimeProvider time)
{
    private DateTimeOffset Now => time.GetUtcNow();

    public async Task ValidateControlAsync(RecruitmentOwnerContext context, string generation, CancellationToken token)
    {
        var state = await store.LoadAsync(token) ?? throw new InvalidOperationException("Recruitment state is unavailable.");
        RequireOwner(state, context, generation);
    }

    public async Task<string> SubmitCodeAsync(RecruitmentOwnerContext context, string generation, string code, CancellationToken token)
    {
        RecruitmentPostRecord post = await ReadOwnedPostAsync(context, generation, token);
        await RequireLivePostAsync(post, token);
        if (post.Acknowledgement == RecruitmentAcknowledgement.Passed) return "Practice acknowledgement already completed.";
        if (post.Advisory.NeedsFreshWindow || post.Advisory.Error is not null || post.ChallengeDeadlineUtc <= Now)
            return "Practice is paused or expired. Use the updated public controls for a fresh window.";
        if (post.Advisory.RetryCodeAtUtc > Now) return "Please wait a minute before trying the code again.";

        // Check the visible topic again: an old receipt alone cannot prove the code is still accessible.
        await guidelines.EnsureAsync(new(post.Forum, post.ParentChannelId), token);
        return await store.UpdateAsync(state =>
        {
            var saved = RequireOwner(state, context, generation);
            var policy = new RecruitmentPolicyEvaluator(options.Value, time);
            if (!policy.IsCorrectCode(saved, code))
            {
                saved.Advisory.IncorrectAttempts++;
                if (saved.Advisory.IncorrectAttempts % 5 == 0) saved.Advisory.RetryCodeAtUtc = Now.AddMinutes(1);
                return "That code did not match. Read this forum's Guidelines and try again; no penalty applies.";
            }
            saved.Acknowledgement = RecruitmentAcknowledgement.Passed;
            saved.ChallengeEnforceable = false;
            saved.Advisory.Version++;
            saved.Advisory.Confirmation = null;
            saved.Advisory.NextCheckAtUtc = null;
            // Practice must not call TryAccept: doing so would seed future cooldown history.
            return "Practice acknowledgement completed. No listing cooldown was started.";
        }, token);
    }

    public async Task RenewAsync(RecruitmentOwnerContext context, string generation, CancellationToken token)
    {
        var post = await ReadOwnedPostAsync(context, generation, token);
        await RequireLivePostAsync(post, token);
        if (post.Acknowledgement != RecruitmentAcknowledgement.Pending || post.ChallengeDeadlineUtc > Now)
            throw new InvalidOperationException("Only an expired practice window can be renewed.");
        await store.UpdateAsync(state =>
        {
            var saved = RequireOwner(state, context, generation);
            saved.Advisory.NeedsFreshWindow = true;
            saved.Advisory.NextCheckAtUtc = null;
            return true;
        }, token);
    }

    public async Task<RecruitmentOwnerConfirmation> PrepareAsync(RecruitmentOwnerContext context, string generation,
        RecruitmentActionKind kind, CancellationToken token)
    {
        var post = await ReadOwnedPostAsync(context, generation, token);
        await RequireLivePostAsync(post, token);
        if (kind != RecruitmentActionKind.Delete && kind != RecruitmentActionKind.LockArchive)
            throw new InvalidOperationException("Unsupported owner action.");
        if (kind == RecruitmentActionKind.LockArchive && post.Acknowledgement != RecruitmentAcknowledgement.Passed)
            throw new InvalidOperationException("Complete the practice acknowledgement before closing, or use Remove my post.");
        return await store.UpdateAsync(state =>
        {
            var saved = RequireOwner(state, context, generation);
            var confirmation = new RecruitmentOwnerConfirmation
            {
                Token = RecruitmentGuidelines.NewToken(), Kind = kind, Version = saved.Advisory.Version,
                AcceptedAtUtc = saved.AcceptedAtUtc, ExpiresAtUtc = Now.AddMinutes(2)
            };
            saved.Advisory.Confirmation = confirmation;
            return confirmation;
        }, token);
    }

    public async Task ConfirmAsync(RecruitmentOwnerContext context, string confirmationToken, CancellationToken token)
    {
        var state = (await store.LoadAsync(token))!;
        var post = RequireOwner(state, context);
        var confirmation = post.Advisory.Confirmation;
        if (confirmation is null || confirmation.Token != confirmationToken || confirmation.ExpiresAtUtc <= Now ||
            confirmation.Version != post.Advisory.Version || confirmation.AcceptedAtUtc != post.AcceptedAtUtc)
            throw new InvalidOperationException("This confirmation expired or the listing changed. Start again from the public message.");
        await RequireLivePostAsync(post, token);
        await store.UpdateAsync(current =>
        {
            var saved = RequireOwner(current, context);
            saved.Advisory.PendingAction = new()
            {
                Id = confirmation.Token, Kind = confirmation.Kind, RequestedAtUtc = Now,
                AcceptedAtUtc = saved.AcceptedAtUtc
            };
            saved.Advisory.Confirmation = null;
            saved.Advisory.Version++;
            saved.Advisory.NextCheckAtUtc = null;
            return true;
        }, token);
        await RecoverAsync(context.ThreadId, token);
    }

    public async Task RecoverAsync(ulong threadId, CancellationToken token)
    {
        var state = (await store.LoadAsync(token))!;
        var post = state.Posts[threadId];
        var action = post.Advisory.PendingAction;
        if (action is null || action.CompletedAtUtc is not null) return;
        RequireAdvisory();
        if (post.IsPinned || post.IsExempt || action.AcceptedAtUtc != post.AcceptedAtUtc)
            throw new InvalidOperationException("Listing protection or acceptance changed; staff review of the pending owner action is required.");

        RecruitmentPublicPost? live = await discord.GetPostAsync(threadId, token);
        if (!ActionCompleted(live, action.Kind))
        {
            ValidateLiveIdentity(post, live);
            await discord.ApplyOwnerActionAsync(live!, action.Kind, state.Forums[post.ParentChannelId].Publication.ClosedTagId,
                action.Id, token);
            live = await discord.GetPostAsync(threadId, token);
            if (!ActionCompleted(live, action.Kind))
                throw new InvalidOperationException("Owner action was not confirmed by Discord; recovery will check again.");
        }
        await store.UpdateAsync(current =>
        {
            var saved = current.Posts[threadId];
            saved.Advisory.PendingAction!.CompletedAtUtc = Now;
            bool tagMissing = action.Kind == RecruitmentActionKind.LockArchive &&
                !live!.Tags.Contains(current.Forums[saved.ParentChannelId].Publication.ClosedTagId ?? 0);
            saved.Advisory.Error = tagMissing ? "Listing closed; Closed tag was unavailable or could not fit. Existing tags were retained." : null;
            saved.Advisory.NextCheckAtUtc = Now.AddMinutes(2);
            saved.ClosedRequested = true;
            saved.ClosedAtUtc ??= Now;
            saved.CloseReason = action.Kind == RecruitmentActionKind.Delete ? RecruitmentCloseReason.OwnerRemoved : RecruitmentCloseReason.OwnerClosed;
            saved.Lifecycle = action.Kind == RecruitmentActionKind.Delete ? RecruitmentLifecycle.Deleted : RecruitmentLifecycle.Closed;
            if (saved.AcceptedAtUtc is null) saved.Acknowledgement = RecruitmentAcknowledgement.Cancelled;
            saved.ChallengeEnforceable = false;
            if (action.Kind == RecruitmentActionKind.Delete) RecordRemovalHistory(current, saved);
            return true;
        }, token);
    }

    private void RecordRemovalHistory(RecruitmentStateDocument state, RecruitmentPostRecord post)
    {
        post.DeletedObservedAtUtc ??= Now;
        post.DeletionTimeUncertain = false;
        if (post.AcceptedAtUtc is null) return;
        // Advisory creates no acceptance anchors, but an explicit removal must preserve older accepted history.
        if (!state.Authors.TryGetValue(post.AuthorId, out var author))
            state.Authors[post.AuthorId] = author = new() { UserId = post.AuthorId };
        var group = RecruitmentForumClassifier.GroupOf(post.Forum);
        if (!author.Groups.TryGetValue(group, out var history)) author.Groups[group] = history = new();
        if (history.LastAcceptedDeletedAtUtc is null || history.LastAcceptedDeletedAtUtc < post.DeletedObservedAtUtc)
            history.LastAcceptedDeletedAtUtc = post.DeletedObservedAtUtc;
    }

    private async Task<RecruitmentPostRecord> ReadOwnedPostAsync(RecruitmentOwnerContext context, string generation, CancellationToken token) =>
        RequireOwner((await store.LoadAsync(token))!, context, generation);

    private RecruitmentPostRecord RequireOwner(RecruitmentStateDocument state, RecruitmentOwnerContext context, string? generation = null)
    {
        RequireAdvisory();
        if (context.GuildId != guild.Value.GuildId || state.GuildId != context.GuildId ||
            !state.Posts.TryGetValue(context.ThreadId, out var post) || post.AuthorId != context.UserId)
            throw new InvalidOperationException("Only this post's owner can use these controls in the original thread.");
        if (post.Lifecycle != RecruitmentLifecycle.Open || post.IsPinned || post.IsExempt || post.ClosedRequested ||
            post.Advisory.PendingAction is { CompletedAtUtc: null })
            throw new InvalidOperationException("This listing is closed, protected, or already has an owner action pending.");
        if (generation is not null && (generation.Length != 16 || post.Advisory.Generation != generation))
            throw new InvalidOperationException("These controls are outdated. Use the current public message.");
        return post;
    }

    private void RequireAdvisory()
    {
        if (!options.Value.Enabled || options.Value.Mode != RecruitmentMode.Advisory)
            throw new InvalidOperationException("Owner controls are available only while Advisory is enabled.");
    }

    private async Task RequireLivePostAsync(RecruitmentPostRecord post, CancellationToken token)
    {
        var live = await discord.GetPostAsync(post.ThreadId, token);
        ValidateLiveIdentity(post, live);
        if (live!.Archived || live.Locked)
            throw new InvalidOperationException("The post is already archived or locked; ask staff if it needs attention.");
    }

    private static void ValidateLiveIdentity(RecruitmentPostRecord post, RecruitmentPublicPost? live)
    {
        if (live is null || live.ParentId != post.ParentChannelId || live.AuthorId != post.AuthorId || live.Pinned || live.OrdinaryAuthor != true)
            throw new InvalidOperationException("The post is unavailable, protected, or its owner could not be verified.");
    }

    private static bool ActionCompleted(RecruitmentPublicPost? live, RecruitmentActionKind kind) =>
        kind == RecruitmentActionKind.Delete ? live is null : live is { Archived: true, Locked: true };
}
