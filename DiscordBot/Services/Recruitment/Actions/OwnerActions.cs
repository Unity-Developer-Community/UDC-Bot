using DiscordBot.Services.Recruitment;
using DiscordBot.Services.Recruitment.Policy;
using DiscordBot.Services.Recruitment.Publishing;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment.Actions;

/// <summary>Owner intent only. Every submission is serialized with reconciliation by RecruitmentService.</summary>
public sealed class OwnerActions(StateStore store, IForumPublisher discord,
    GuidelinePublisher guidelines, LifecycleExecutor lifecycle, IOptions<RecruitmentOptions> options,
    IOptions<DiscordGuildOptions> guild, TimeProvider time)
{
    private DateTimeOffset Now => time.GetUtcNow();

    public async Task ValidateControlAsync(OwnerContext context, string generation, CancellationToken token)
    {
        var state = await store.LoadAsync(token) ?? throw new InvalidOperationException("Recruitment state is unavailable.");
        RequireOwner(state, context, generation);
    }

    public async Task<string> SubmitCodeAsync(OwnerContext context, string generation, string code, CancellationToken token)
    {
        PostRecord post = await ReadOwnedPostAsync(context, generation, token);
        await RequireLivePostAsync(post, token);
        if (post.Acknowledgement == AcknowledgementStatus.Passed) return "Acknowledgement already completed.";
        if (post.Advisory.NeedsFreshWindow || post.Advisory.Error is not null || post.ChallengeDeadlineUtc <= Now)
            return "Acknowledgement is paused or expired. Check the public status and contact staff if no renewal is available.";
        if (post.Advisory.RetryCodeAtUtc > Now) return "Please wait a minute before trying the code again.";

        // Check the visible topic again: an old receipt alone cannot prove the code is still accessible.
        await guidelines.EnsureAsync(new(post.Forum, post.ParentChannelId), token);
        return await store.UpdateAsync(state =>
        {
            var saved = RequireOwner(state, context, generation);
            var policy = new PolicyEvaluator(options.Value, time);
            if (!policy.IsCorrectCode(saved, code))
            {
                saved.Advisory.IncorrectAttempts++;
                if (saved.Advisory.IncorrectAttempts % 5 == 0) saved.Advisory.RetryCodeAtUtc = Now.AddMinutes(1);
                return "That code did not match. Read this forum's Guidelines and try again; no penalty applies.";
            }
            saved.Acknowledgement = AcknowledgementStatus.Passed;
            saved.ChallengeEnforceable = false;
            saved.Advisory.Version++;
            saved.Advisory.Confirmation = null;
            saved.Advisory.NextCheckAtUtc = null;
            bool enforced = options.Value.Mode == RecruitmentMode.Enforce && saved.EnforcementEnrolled;
            if (enforced)
            {
                var author = ListingHistory.Author(state, saved);
                author.ConsecutiveTimeouts = 0;
                author.TimeoutAlertActionId = null;
                author.LastActivityAtUtc = Now;
                saved.EnforcementNextCheckAtUtc = null;
                return "Acknowledgement recorded. Eligibility will be checked at the grace deadline; acknowledgement alone does not accept the listing.";
            }
            return "Practice acknowledgement completed. No listing cooldown was started.";
        }, token);
    }

    public async Task RenewAsync(OwnerContext context, string generation, CancellationToken token)
    {
        var post = await ReadOwnedPostAsync(context, generation, token);
        await RequireLivePostAsync(post, token);
        if (options.Value.Mode == RecruitmentMode.Enforce && post.EnforcementEnrolled && options.Value.EnforceGuidelineTimeouts)
            throw new InvalidOperationException("This acknowledgement deadline is enforced; a new window requires staff review or availability recovery.");
        if (post.Acknowledgement != AcknowledgementStatus.Pending || post.ChallengeDeadlineUtc > Now)
            throw new InvalidOperationException("Only an expired practice window can be renewed.");
        await store.UpdateAsync(state =>
        {
            var saved = RequireOwner(state, context, generation);
            saved.Advisory.NeedsFreshWindow = true;
            saved.Advisory.NextCheckAtUtc = null;
            return true;
        }, token);
    }

    public async Task<ActionConfirmation> PrepareAsync(OwnerContext context, string generation,
        ActionKind kind, CancellationToken token)
    {
        var post = await ReadOwnedPostAsync(context, generation, token);
        await RequireLivePostAsync(post, token);
        if (kind != ActionKind.Delete && kind != ActionKind.LockArchive)
            throw new InvalidOperationException("Unsupported owner action.");
        if (kind == ActionKind.LockArchive && (post.Acknowledgement != AcknowledgementStatus.Passed ||
            options.Value.Mode == RecruitmentMode.Enforce && post.EnforcementEnrolled && post.AcceptedAtUtc is null))
            throw new InvalidOperationException("Complete the practice acknowledgement before closing, or use Remove my post.");
        return await store.UpdateAsync(state =>
        {
            var saved = RequireOwner(state, context, generation);
            var confirmation = new ActionConfirmation
            {
                Token = GuidelineTemplates.NewToken(), Kind = kind, Version = saved.Advisory.Version,
                ActorId = context.UserId, Origin = ActionOrigin.Owner, Note = "Confirmed by the listing owner.",
                AcceptedAtUtc = saved.AcceptedAtUtc, ExpiresAtUtc = Now.AddMinutes(2)
            };
            saved.Advisory.Confirmation = confirmation;
            return confirmation;
        }, token);
    }

    public async Task ConfirmAsync(OwnerContext context, string confirmationToken, CancellationToken token)
    {
        var state = (await store.LoadAsync(token))!;
        var post = RequireOwner(state, context);
        var confirmation = post.Advisory.Confirmation;
        if (confirmation is null || confirmation.Origin != ActionOrigin.Owner || confirmation.ActorId != context.UserId || confirmation.Token != confirmationToken || confirmation.ExpiresAtUtc <= Now ||
            confirmation.Version != post.Advisory.Version || confirmation.AcceptedAtUtc != post.AcceptedAtUtc)
            throw new InvalidOperationException("This confirmation expired or the listing changed. Start again from the public message.");
        await RequireLivePostAsync(post, token);
        await lifecycle.RequestAsync(context.ThreadId, confirmation.Kind, ActionOrigin.Owner,
            confirmation.Kind == ActionKind.Delete ? CloseReason.OwnerRemoved : CloseReason.OwnerClosed,
            context.UserId, "Confirmed by the listing owner.", token, confirmation.Token);
        await lifecycle.RecoverAsync(context.ThreadId, token);
    }

    public Task RecoverAsync(ulong threadId, CancellationToken token) => lifecycle.RecoverAsync(threadId, token);

    private async Task<PostRecord> ReadOwnedPostAsync(OwnerContext context, string generation, CancellationToken token) =>
        RequireOwner((await store.LoadAsync(token))!, context, generation);

    private PostRecord RequireOwner(StateDocument state, OwnerContext context, string? generation = null)
    {
        RequirePublicMode();
        if (context.GuildId != guild.Value.GuildId || state.GuildId != context.GuildId ||
            !state.Posts.TryGetValue(context.ThreadId, out var post) || post.AuthorId != context.UserId)
            throw new InvalidOperationException("Only this post's owner can use these controls in the original thread.");
        if (post.Lifecycle != ListingLifecycle.Open || post.IsPinned || post.IsExempt || post.ClosedRequested ||
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

    private async Task RequireLivePostAsync(PostRecord post, CancellationToken token)
    {
        var live = await discord.GetPostAsync(post.ThreadId, token);
        ValidateLiveIdentity(post, live);
        if (live!.Archived || live.Locked)
            throw new InvalidOperationException("The post is already archived or locked; ask staff if it needs attention.");
    }

    private static void ValidateLiveIdentity(PostRecord post, PublicPost? live)
    {
        if (live is null || live.ParentId != post.ParentChannelId || live.AuthorId != post.AuthorId || live.Pinned || live.OrdinaryAuthor != true)
            throw new InvalidOperationException("The post is unavailable, protected, or its owner could not be verified.");
    }

}
