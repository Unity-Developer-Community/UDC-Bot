using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment;

/// <summary>Owner intent only. Every submission is serialized with reconciliation by RecruitService.</summary>
public sealed class RecruitmentOwnerActions(RecruitmentStateStore store, IRecruitmentPublisher discord,
    RecruitmentGuidelinePublisher guidelines, RecruitmentLifecycleExecutor lifecycle, IOptions<RecruitmentOptions> options,
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
        if (post.Acknowledgement == RecruitmentAcknowledgement.Passed) return "Acknowledgement already completed.";
        if (post.Advisory.NeedsFreshWindow || post.Advisory.Error is not null || post.ChallengeDeadlineUtc <= Now)
            return "Acknowledgement is paused or expired. Check the public status and contact staff if no renewal is available.";
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
            bool enforced = options.Value.Mode == RecruitmentMode.Enforce && saved.EnforcementEnrolled;
            if (enforced)
            {
                var author = RecruitmentHistory.Author(state, saved);
                author.ConsecutiveTimeouts = 0;
                author.TimeoutAlertActionId = null;
                author.LastActivityAtUtc = Now;
                saved.EnforcementNextCheckAtUtc = null;
                return "Acknowledgement recorded. Eligibility will be checked at the grace deadline; acknowledgement alone does not accept the listing.";
            }
            return "Practice acknowledgement completed. No listing cooldown was started.";
        }, token);
    }

    public async Task RenewAsync(RecruitmentOwnerContext context, string generation, CancellationToken token)
    {
        var post = await ReadOwnedPostAsync(context, generation, token);
        await RequireLivePostAsync(post, token);
        if (options.Value.Mode == RecruitmentMode.Enforce && post.EnforcementEnrolled && options.Value.EnforceGuidelineTimeouts)
            throw new InvalidOperationException("This acknowledgement deadline is enforced; a new window requires staff review or availability recovery.");
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

    public async Task<RecruitmentConfirmation> PrepareAsync(RecruitmentOwnerContext context, string generation,
        RecruitmentActionKind kind, CancellationToken token)
    {
        var post = await ReadOwnedPostAsync(context, generation, token);
        await RequireLivePostAsync(post, token);
        if (kind != RecruitmentActionKind.Delete && kind != RecruitmentActionKind.LockArchive)
            throw new InvalidOperationException("Unsupported owner action.");
        if (kind == RecruitmentActionKind.LockArchive && (post.Acknowledgement != RecruitmentAcknowledgement.Passed ||
            options.Value.Mode == RecruitmentMode.Enforce && post.EnforcementEnrolled && post.AcceptedAtUtc is null))
            throw new InvalidOperationException("Complete the practice acknowledgement before closing, or use Remove my post.");
        return await store.UpdateAsync(state =>
        {
            var saved = RequireOwner(state, context, generation);
            var confirmation = new RecruitmentConfirmation
            {
                Token = RecruitmentGuidelines.NewToken(), Kind = kind, Version = saved.Advisory.Version,
                ActorId = context.UserId, Origin = RecruitmentActionOrigin.Owner, Note = "Confirmed by the listing owner.",
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
        if (confirmation is null || confirmation.Origin != RecruitmentActionOrigin.Owner || confirmation.ActorId != context.UserId || confirmation.Token != confirmationToken || confirmation.ExpiresAtUtc <= Now ||
            confirmation.Version != post.Advisory.Version || confirmation.AcceptedAtUtc != post.AcceptedAtUtc)
            throw new InvalidOperationException("This confirmation expired or the listing changed. Start again from the public message.");
        await RequireLivePostAsync(post, token);
        await lifecycle.RequestAsync(context.ThreadId, confirmation.Kind, RecruitmentActionOrigin.Owner,
            confirmation.Kind == RecruitmentActionKind.Delete ? RecruitmentCloseReason.OwnerRemoved : RecruitmentCloseReason.OwnerClosed,
            context.UserId, "Confirmed by the listing owner.", token, confirmation.Token);
        await lifecycle.RecoverAsync(context.ThreadId, token);
    }

    public Task RecoverAsync(ulong threadId, CancellationToken token) => lifecycle.RecoverAsync(threadId, token);

    private async Task<RecruitmentPostRecord> ReadOwnedPostAsync(RecruitmentOwnerContext context, string generation, CancellationToken token) =>
        RequireOwner((await store.LoadAsync(token))!, context, generation);

    private RecruitmentPostRecord RequireOwner(RecruitmentStateDocument state, RecruitmentOwnerContext context, string? generation = null)
    {
        RequirePublicMode();
        if (context.GuildId != guild.Value.GuildId || state.GuildId != context.GuildId ||
            !state.Posts.TryGetValue(context.ThreadId, out var post) || post.AuthorId != context.UserId)
            throw new InvalidOperationException("Only this post's owner can use these controls in the original thread.");
        if (post.Lifecycle != RecruitmentLifecycle.Open || post.IsPinned || post.IsExempt || post.ClosedRequested ||
            post.PendingAction is { IsPending: true })
            throw new InvalidOperationException("This listing is closed, protected, or already has an owner action pending.");
        if (generation is not null && (generation.Length != 16 || post.Advisory.Generation != generation))
            throw new InvalidOperationException("These controls are outdated. Use the current public message.");
        return post;
    }

    private void RequirePublicMode()
    {
        if (!options.Value.Enabled || options.Value.Mode == RecruitmentMode.Observe)
            throw new InvalidOperationException("Owner controls are available only while Advisory or Enforce is enabled.");
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

}
