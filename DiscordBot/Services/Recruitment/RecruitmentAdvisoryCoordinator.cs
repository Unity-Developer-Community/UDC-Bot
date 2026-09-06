using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment;

/// <summary>Public practice workflow. RecruitService serializes ticks and mutating owner/staff commands.</summary>
public sealed class RecruitmentAdvisoryCoordinator(RecruitmentStateStore store, IRecruitmentPublisher discord,
    RecruitmentGuidelines templates, RecruitmentGuidelinePublisher guidelines, IRecruitmentBannerRenderer renderer,
    RecruitmentOwnerActions ownerActions, IOptions<RecruitmentOptions> options, TimeProvider time)
{
    private DateTimeOffset Now => time.GetUtcNow();
    private RecruitmentOptions Options => options.Value;
    public bool HasGaps { get; private set; } = true;
    public string Summary { get; private set; } = "Advisory setup pending.";

    public Task InitializeAsync(CancellationToken token)
    {
        templates.ValidateAll();
        HasGaps = true;
        return RecordGapAsync(token);
    }

    public Task RecordGapAsync(CancellationToken token) => store.UpdateAsync(state =>
    {
        foreach (var forum in state.Forums.Values) forum.Publication.CheckedAtUtc = null;
        foreach (var post in state.Posts.Values)
        {
            if (post.Acknowledgement == RecruitmentAcknowledgement.Pending) post.Advisory.NeedsFreshWindow = true;
            post.Advisory.NextCheckAtUtc = null;
        }
        return true;
    }, token);

    public async Task TickAsync(CancellationToken token)
    {
        // A visible, confirmed code is a prerequisite for starting or refreshing a practice window.
        await EnsureForumsAsync(token);
        await RefreshDuePostsAsync(token);
        await UpdateSummaryAsync(token);
    }

    private async Task EnsureForumsAsync(CancellationToken token)
    {
        foreach (RecruitmentForum forum in RecruitmentForumClassifier.GetForums(Options.Forums))
        {
            var saved = (await store.LoadAsync(token))!.Forums[forum.ChannelId].Publication;
            if (saved.CheckedAtUtc > Now.AddMinutes(-2)) continue;
            try { await guidelines.EnsureAsync(forum, token); }
            catch (Exception error) when (Retryable(error, token))
            {
                await store.UpdateAsync(state =>
                {
                    state.Forums[forum.ChannelId].Publication.Error = Error(error);
                    state.Forums[forum.ChannelId].Publication.CheckedAtUtc = Now;
                    foreach (var post in state.Posts.Values.Where(post => post.ParentChannelId == forum.ChannelId &&
                                 post.Acknowledgement == RecruitmentAcknowledgement.Pending))
                    {
                        post.Advisory.NeedsFreshWindow = true;
                    }
                    return true;
                }, token);
            }
        }

    }

    private async Task RefreshDuePostsAsync(CancellationToken token)
    {
        var state = (await store.LoadAsync(token))!;
        var duePosts = state.Posts.Values.Where(post => post.Advisory.NextCheckAtUtc is null || post.Advisory.NextCheckAtUtc <= Now)
            .OrderBy(post => post.Advisory.NextCheckAtUtc).ThenBy(post => post.ThreadId).Take(8);
        foreach (var post in duePosts)
        {
            try
            {
                if (post.Advisory.PendingAction is { CompletedAtUtc: null })
                    await ownerActions.RecoverAsync(post.ThreadId, token);
                else
                    await RefreshPostAsync(post.ThreadId, token);
            }
            catch (Exception error) when (Retryable(error, token))
            {
                await RecordPostErrorAsync(post.ThreadId, error, token);
            }
        }

    }

    private async Task UpdateSummaryAsync(CancellationToken token)
    {
        var state = (await store.LoadAsync(token))!;
        int forumsPending = state.Forums.Values.Count(forum => forum.Publication.Confirmed is null || forum.Publication.Error is not null);
        int postsWithNotices = state.Posts.Values.Count(post => post.Advisory.Error is not null || post.Advisory.RenderError is not null ||
            post.Advisory.SendRequestedAtUtc is not null || post.Advisory.PendingAction is { CompletedAtUtc: null });
        HasGaps = forumsPending + postsWithNotices > 0;
        Summary = $"Advisory practice: {forumsPending} forums need setup/recovery; {postsWithNotices} posts have delivery/action notices. Automatic enforcement is unavailable.";
    }

    public async Task<bool> TryRefreshPostAsync(ulong threadId, CancellationToken token)
    {
        try
        {
            await RefreshPostAsync(threadId, token);
            return true;
        }
        catch (Exception error) when (Retryable(error, token))
        {
            // The owner action may already be committed. A failed status edit must not undo or misreport it.
            await RecordPostErrorAsync(threadId, error, token);
            return false;
        }
    }

    private Task RecordPostErrorAsync(ulong threadId, Exception error, CancellationToken token) => store.UpdateAsync(state =>
    {
        var advisory = state.Posts[threadId].Advisory;
        advisory.Error = Error(error);
        advisory.NeedsFreshWindow = true;
        advisory.NextCheckAtUtc = Now.AddMinutes(2);
        return true;
    }, token);

    public async Task RefreshPostAsync(ulong threadId, CancellationToken token)
    {
        var state = (await store.LoadAsync(token))!;
        var post = state.Posts[threadId];
        await ScheduleAsync(threadId, Now.AddMinutes(2), token);
        if (post.Lifecycle != RecruitmentLifecycle.Open || post.ClosedRequested)
        {
            if (post.Lifecycle != RecruitmentLifecycle.Deleted && post.AdvisoryMessageId is not null)
                await UpdateMessageAsync(threadId, token);
            return;
        }
        if (post.IsPinned || post.IsExempt) return;
        // Imported history is left for staff adoption; rollout must not create retroactive challenges.
        if (post.Observation.Imported && post.Advisory.Generation.Length == 0) return;
        RecruitmentPublicPost? live = await discord.GetPostAsync(threadId, token);
        if (live is null || live.Pinned || live.OrdinaryAuthor != true || live.Archived || live.Locked) return;
        if (live.AuthorId != post.AuthorId || live.ParentId != post.ParentChannelId)
            throw new InvalidOperationException("Public post identity changed; staff review is required.");

        var publication = state.Forums[post.ParentChannelId].Publication;
        if (publication.Confirmed is null || publication.Error is not null)
            throw new InvalidOperationException("Guidelines are not currently confirmed; practice is paused.");

        // A Closed tag on an unaccepted post means withdrawal, not an acknowledgement failure.
        if (live.Tags.Contains(publication.ClosedTagId ?? 0) && post.AcceptedAtUtc is null)
        {
            await store.UpdateAsync(current =>
            {
                var saved = current.Posts[threadId];
                saved.ClosedRequested = true;
                saved.Acknowledgement = RecruitmentAcknowledgement.Cancelled;
                saved.Advisory.Version++;
                return true;
            }, token);
            return;
        }

        if (post.Advisory.Generation.Length == 0)
        {
            await store.UpdateAsync(current =>
            {
                current.Posts[threadId].Advisory.Generation = RecruitmentGuidelines.NewToken();
                current.Posts[threadId].Advisory.Version++;
                return true;
            }, token);
            state = (await store.LoadAsync(token))!;
            post = state.Posts[threadId];
        }
        if (post.AdvisoryMessageId is null && !await EnsureMessageAsync(state, post, token)) return;

        state = (await store.LoadAsync(token))!;
        post = state.Posts[threadId];
        bool freshWindow = post.Acknowledgement == RecruitmentAcknowledgement.NotPrompted ||
            post.Advisory.NeedsFreshWindow && post.Acknowledgement == RecruitmentAcknowledgement.Pending;
        if (freshWindow)
        {
            // New control identities invalidate modals opened before an outage or replacement message.
            await store.UpdateAsync(current =>
            {
                var saved = current.Posts[threadId];
                saved.Advisory.Generation = RecruitmentGuidelines.NewToken();
                saved.Advisory.Version++;
                saved.Advisory.Confirmation = null;
                return true;
            }, token);
        }
        if (!await UpdateMessageAsync(threadId, token)) return;

        if (freshWindow)
        {
            // Start the full window after usable controls are delivered, never at thread creation or send intent.
            await store.UpdateAsync(current =>
            {
                var saved = current.Posts[threadId];
                saved.PromptedAtUtc = Now;
                saved.ChallengeDeadlineUtc = Now.AddMinutes(Options.AcknowledgementMinutes);
                saved.AcceptedCodes = [publication.Confirmed.Code];
                saved.Acknowledgement = RecruitmentAcknowledgement.Pending;
                saved.ChallengeEnforceable = false;
                saved.EnforcementEnrolled = false;
                saved.Advisory.NeedsFreshWindow = false;
                saved.Advisory.IncorrectAttempts = 0;
                saved.Advisory.RetryCodeAtUtc = null;
                saved.Advisory.Error = null;
                return true;
            }, token);
            await UpdateMessageAsync(threadId, token);
        }
        await store.UpdateAsync(current => { current.Posts[threadId].Advisory.Error = null; return true; }, token);
    }

    private async Task<bool> EnsureMessageAsync(RecruitmentStateDocument state, RecruitmentPostRecord post, CancellationToken token)
    {
        string marker = RecruitmentAdvisoryMessage.Marker(state.GuildId, post.ThreadId);
        if (post.Advisory.SendRequestedAtUtc is not null)
        {
            var search = await discord.FindAdvisoryAsync(post.ThreadId, marker, post.Advisory.SearchBeforeId, token);
            if (search.Found is { } found)
            {
                await SaveMessageAsync(post.ThreadId, found.Id, token);
                return true;
            }
            if (!search.Complete)
            {
                if (search.BeforeId is null || post.Advisory.SearchBeforeId is { } before && search.BeforeId >= before)
                    throw new InvalidOperationException("Advisory recovery cursor did not advance.");
                await store.UpdateAsync(current =>
                {
                    current.Posts[post.ThreadId].Advisory.SearchBeforeId = search.BeforeId;
                    current.Posts[post.ThreadId].Advisory.NextCheckAtUtc = Now.AddSeconds(30);
                    return true;
                }, token);
                return false;
            }
        }

        byte[]? banner = null;
        if (post.Advisory.RenderError is null)
        {
            try { banner = await renderer.RenderAsync(post, new(Options, time), token); }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                await RecordRenderFallbackAsync(post.ThreadId, token);
            }
        }
        await store.UpdateAsync(current =>
        {
            current.Posts[post.ThreadId].Advisory.SendRequestedAtUtc = Now;
            current.Posts[post.ThreadId].Advisory.SearchBeforeId = null;
            return true;
        }, token);
        try
        {
            var sent = await discord.SendAdvisoryAsync(post.ThreadId, RecruitmentAdvisoryMessage.Build(state, post, Options, time), banner, token);
            await SaveMessageAsync(post.ThreadId, sent.Id, token);
            return true;
        }
        catch (Exception) when (banner is not null && Retryable(null, token))
        {
            // Upload may have reached Discord. Recovery must search before attempting the text fallback.
            await RecordRenderFallbackAsync(post.ThreadId, token);
            throw;
        }
    }

    private async Task<bool> UpdateMessageAsync(ulong threadId, CancellationToken token)
    {
        var state = (await store.LoadAsync(token))!;
        var post = state.Posts[threadId];
        if (await discord.EditAdvisoryAsync(threadId, post.AdvisoryMessageId!.Value,
                RecruitmentAdvisoryMessage.Build(state, post, Options, time), token)) return true;
        await store.UpdateAsync(current =>
        {
            var saved = current.Posts[threadId];
            saved.AdvisoryMessageId = null;
            saved.Advisory.NeedsFreshWindow = true;
            return true;
        }, token);
        return false;
    }

    private Task SaveMessageAsync(ulong id, ulong messageId, CancellationToken token) => store.UpdateAsync(state =>
    {
        var post = state.Posts[id];
        post.AdvisoryMessageId = messageId;
        post.Advisory.SendRequestedAtUtc = null;
        post.Advisory.SearchBeforeId = null;
        return true;
    }, token);

    private Task RecordRenderFallbackAsync(ulong id, CancellationToken token) => store.UpdateAsync(state =>
    {
        state.Posts[id].Advisory.RenderError = "Image unavailable; advisory uses equivalent text.";
        return true;
    }, token);

    private Task ScheduleAsync(ulong id, DateTimeOffset next, CancellationToken token) => store.UpdateAsync(state =>
    {
        state.Posts[id].Advisory.NextCheckAtUtc = next;
        return true;
    }, token);

    private bool Retryable(Exception? error, CancellationToken token) => store.IsHealthy && !token.IsCancellationRequested && error is not System.IO.InvalidDataException;
    private static string Error(Exception error) => error is InvalidOperationException ? error.Message[..Math.Min(error.Message.Length, 200)] :
        $"{error.GetType().Name}: public setup/delivery failed; retry pending.";
}
