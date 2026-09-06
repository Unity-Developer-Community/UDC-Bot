using System.Security.Cryptography;
using System.Text;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment;

/// <summary>
/// Reconciles Discord observations and staff-feed delivery under RecruitService's single worker.
/// State callbacks only change local metadata; Discord/database calls run outside the store lock.
/// See docs/features.md, "Maintaining the Observe coordinator", for recovery invariants.
/// </summary>
public sealed class RecruitmentObservationCoordinator(
    RecruitmentStateStore store, IRecruitmentObserver discord, IOptions<RecruitmentOptions> options, TimeProvider time)
{
    // Separate budgets prevent a busy forum or feed backlog from monopolizing a scheduler tick.
    private const int PostsPerTick = 8;
    private const int FeedEntriesPerTick = 8;

    private RecruitmentOptions Options => options.Value;
    private DateTimeOffset Now => time.GetUtcNow();
    private DateTimeOffset _startedAt;
    public string Summary { get; private set; } = "Awaiting initial inventory.";
    public bool HasGaps { get; private set; } = true;

    public async Task InitializeAsync(CancellationToken token)
    {
        HasGaps = true;
        Summary = "Observe: initial reconciliation pending. No public actions.";
        _startedAt = Now;
        await discord.ValidateAsync(token);
        if (await store.LoadAsync(token) is null)
        {
            await store.InitializeAsync(Now, token);
        }
        var forums = RecruitmentForumClassifier.GetForums(Options.Forums);
        await store.UpdateAsync(state =>
        {
            if (state.Forums.Keys.Any(id => forums.All(forum => forum.ChannelId != id)) ||
                state.Posts.Values.Any(post => forums.All(forum => forum.ChannelId != post.ParentChannelId || forum.Kind != post.Forum)))
            {
                throw new InvalidOperationException("Recruitment forum mapping changed; review/migrate existing state before starting.");
            }
            foreach (var forum in forums)
            {
                state.Forums.TryAdd(forum.ChannelId, new());
            }
            return true;
        }, token);
        await RecordGapAsync(token);
    }

    public async Task RecordGapAsync(CancellationToken token, long droppedEvents = 0)
    {
        HasGaps = true;
        Summary = "Observe: gateway/reconciliation gap; catch-up pending. No public actions.";
        await store.UpdateAsync(state =>
        {
            state.LastGatewayGapAtUtc = Now;
            state.DroppedObservationEvents = checked(state.DroppedObservationEvents + droppedEvents);
            foreach (var forum in state.Forums.Values)
            {
                forum.ActiveCheckedAtUtc = null;
                // Resume a partially scanned archive; rescan from the top after it completes.
                if (forum.ArchiveBeforeUtc is not null)
                {
                    forum.ArchiveRescanRequired = true;
                }
                forum.ArchiveCompletedAtUtc = null;
            }
            foreach (var post in state.Posts.Values)
            {
                // Catch-up can find surviving replies, but cannot recover replies deleted during the gap.
                post.Observation.HistoryUncertain = true;
                post.ResponsesCheckedThroughUtc = null;
                post.Observation.NextCheckAtUtc = null;
            }
            return true;
        }, token);
    }

    public async Task HandleAsync(RecruitmentObservationEvent item, CancellationToken token)
    {
        if (item.Kind == RecruitmentEventKind.Gap)
        {
            await RecordGapAsync(token);
            return;
        }
        var state = (await store.LoadAsync(token))!;
        if (!state.Posts.ContainsKey(item.ThreadId))
        {
            if (RecruitmentForumClassifier.GetForums(Options.Forums).All(forum => forum.ChannelId != item.ParentId))
            {
                return;
            }
            if (item.Kind == RecruitmentEventKind.Deleted)
            {
                await RecordGapAsync(token);
                return;
            }
            var thread = await discord.GetThreadAsync(item.ThreadId, token);
            if (thread is null)
            {
                await RecordGapAsync(token);
                return;
            }
            await store.UpdateAsync(state =>
            {
                ObserveThread(state, thread);
                return true;
            }, token);
        }
        await store.UpdateAsync(state =>
        {
            if (!state.Posts.TryGetValue(item.ThreadId, out var post))
            {
                return false;
            }
            if (item.Kind == RecruitmentEventKind.Deleted)
            {
                if (post.Lifecycle == RecruitmentLifecycle.Deleted)
                {
                    return false;
                }
                RecordConfirmedDeletion(state, post);
            }
            else if (post.Lifecycle != RecruitmentLifecycle.Deleted)
            {
                post.Observation.NextCheckAtUtc = null;
                if (item.Message is { } message && message.Id != post.ThreadId)
                {
                    ObserveResponse(post, message.Response);
                }
            }
            return true;
        }, token);
    }

    private void RecordConfirmedDeletion(RecruitmentStateDocument state, RecruitmentPostRecord post)
    {
        post.Lifecycle = RecruitmentLifecycle.Deleted;
        post.DeletedObservedAtUtc = Now;
        post.DeletionTimeUncertain = false;
        if (post.AcceptedAtUtc is not null)
        {
            if (!state.Authors.TryGetValue(post.AuthorId, out var author))
            {
                state.Authors[post.AuthorId] = author = new() { UserId = post.AuthorId };
            }
            var group = RecruitmentForumClassifier.GroupOf(post.Forum);
            if (!author.Groups.TryGetValue(group, out var history))
            {
                author.Groups[group] = history = new();
            }
            history.LastAcceptedDeletedAtUtc = Now;
        }
    }

    public async Task TickAsync(CancellationToken token)
    {
        // Inventory first: eligibility and feed findings should include newly discovered attempts.
        foreach (RecruitmentForum forum in RecruitmentForumClassifier.GetForums(Options.Forums))
        {
            await ReconcileForumAsync(forum, token);
        }
        await CheckDuePostsAsync(token);
        await PublishDueFeedEntriesAsync(token);
        await RefreshHealthAsync(token);
    }

    private async Task ReconcileForumAsync(RecruitmentForum forum, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var inventory = (await store.LoadAsync(token))!.Forums[forum.ChannelId];
            if (inventory.ActiveCheckedAtUtc is null || inventory.ActiveCheckedAtUtc <= Now.AddMinutes(-5))
            {
                var active = await discord.GetActiveAsync(forum.ChannelId, token);
                await store.UpdateAsync(state =>
                {
                    foreach (var thread in active)
                    {
                        ObserveThread(state, thread);
                    }
                    state.Forums[forum.ChannelId].ActiveCheckedAtUtc = Now;
                    return true;
                }, token);
            }
            if (inventory.ArchiveCompletedAtUtc is null || inventory.ArchiveCompletedAtUtc <= Now.AddHours(-6))
            {
                var page = await discord.GetArchivedAsync(forum.ChannelId, inventory.ArchiveBeforeUtc, token);
                if (!page.Complete && (page.BeforeUtc is null ||
                    inventory.ArchiveBeforeUtc is { } before && page.BeforeUtc >= before))
                {
                    throw new InvalidOperationException("Archive cursor did not advance; inventory remains incomplete.");
                }
                await store.UpdateAsync(state =>
                {
                    foreach (var thread in page.Threads)
                    {
                        ObserveThread(state, thread);
                    }
                    var savedInventory = state.Forums[forum.ChannelId];
                    savedInventory.ArchiveBeforeUtc = page.Complete ? null : page.BeforeUtc;
                    savedInventory.ArchiveCompletedAtUtc = page.Complete && !savedInventory.ArchiveRescanRequired ? Now : null;
                    if (page.Complete)
                    {
                        savedInventory.ArchiveRescanRequired = false;
                    }
                    return true;
                }, token);
            }
            await store.UpdateAsync(state =>
            {
                state.Forums[forum.ChannelId].Error = null;
                return true;
            }, token);
        }
        catch (Exception e) when (CanRetry(e, token))
        {
            await store.UpdateAsync(state =>
            {
                state.Forums[forum.ChannelId].Error = Error(e);
                return true;
            }, token);
        }
    }

    private async Task CheckDuePostsAsync(CancellationToken token)
    {
        var state = (await store.LoadAsync(token))!;
        IEnumerable<RecruitmentPostRecord> duePosts = state.Posts.Values
            .Where(post => post.Lifecycle != RecruitmentLifecycle.Deleted &&
                (post.Observation.NextCheckAtUtc is null || post.Observation.NextCheckAtUtc <= Now))
            .OrderBy(post => post.Observation.NextCheckAtUtc)
            .ThenBy(post => post.ThreadId)
            .Take(PostsPerTick);

        foreach (RecruitmentPostRecord post in duePosts)
        {
            await CheckPostAsync(post.ThreadId, token);
        }
    }

    private async Task PublishDueFeedEntriesAsync(CancellationToken token)
    {
        var state = (await store.LoadAsync(token))!;
        IEnumerable<RecruitmentPostRecord> dueEntries = state.Posts.Values
            .Where(post => post.Observation.FeedRetryAtUtc is null || post.Observation.FeedRetryAtUtc <= Now)
            .OrderBy(post => post.Observation.FeedRetryAtUtc)
            .ThenBy(post => post.ThreadId)
            .Take(FeedEntriesPerTick);

        foreach (RecruitmentPostRecord post in dueEntries)
        {
            await PublishFeedAsync(post.ThreadId, token);
        }
    }

    private async Task RefreshHealthAsync(CancellationToken token)
    {
        var state = (await store.LoadAsync(token))!;
        int incompleteInventories = state.Forums.Values.Count(HasIncompleteInventory);
        int postsWithEvidenceGaps = state.Posts.Values.Count(HasEvidenceGap);
        int pendingFeedEntries = state.Posts.Values.Count(HasPendingFeedDelivery);

        HasGaps = incompleteInventories + postsWithEvidenceGaps + pendingFeedEntries > 0;
        Summary = $"Observe: {state.Posts.Count} posts; " +
            $"{incompleteInventories} incomplete forum inventories; " +
            $"{postsWithEvidenceGaps} posts with review/coverage gaps; " +
            $"{pendingFeedEntries} pending/failed feed entries; " +
            $"{state.DroppedObservationEvents} queue overflows recorded. No public actions.";
    }

    private static bool HasIncompleteInventory(RecruitmentForumObservation inventory) =>
        inventory.ActiveCheckedAtUtc is null || inventory.ArchiveCompletedAtUtc is null || inventory.Error is not null;

    private static bool HasEvidenceGap(RecruitmentPostRecord post)
    {
        bool responseCheckPending = post.Lifecycle == RecruitmentLifecycle.Open &&
            post.FirstQualifyingResponseAtUtc is null && post.ResponsesCheckedThroughUtc is null;
        return post.RequiresReview || post.Observation.HistoryUncertain ||
            post.Observation.Error is not null || responseCheckPending;
    }

    private static bool HasPendingFeedDelivery(RecruitmentPostRecord post) =>
        post.FeedMessageId is null || post.Observation.FeedError is not null ||
        post.Observation.FeedSendRequestedAtUtc is not null;

    private void ObserveThread(RecruitmentStateDocument state, RecruitmentThreadSnapshot thread)
    {
        var forum = RecruitmentForumClassifier.GetForums(Options.Forums).SingleOrDefault(forum => forum.ChannelId == thread.ParentId);
        if (forum is null)
        {
            return;
        }
        if (!state.Posts.TryGetValue(thread.Id, out var post))
        {
            // Enrollment defines historical imports; this process start defines the latest observation gap.
            // Neither timestamp proves that an author previously acknowledged the guidelines.
            bool imported = thread.CreatedAtUtc < state.EnrolledAtUtc;
            state.Posts[thread.Id] = post = new()
            {
                ThreadId = thread.Id,
                ParentChannelId = thread.ParentId,
                AuthorId = thread.AuthorId,
                Forum = forum.Kind,
                CreatedAtUtc = thread.CreatedAtUtc,
                FirstSeenAtUtc = Now,
                RequiresReview = imported,
                Observation = new() { Imported = imported, HistoryUncertain = thread.CreatedAtUtc < _startedAt }
            };
            if (!state.Authors.TryGetValue(post.AuthorId, out var author))
            {
                state.Authors[post.AuthorId] = author = new() { UserId = post.AuthorId };
            }
            author.FirstAttemptAtUtc = author.FirstAttemptAtUtc is { } first && first < post.CreatedAtUtc ? first : post.CreatedAtUtc;
            author.LastAttemptAtUtc = author.LastAttemptAtUtc is { } last && last > post.CreatedAtUtc ? last : post.CreatedAtUtc;
        }
        if (post.Lifecycle == RecruitmentLifecycle.Deleted)
        {
            return; // Late create/update cannot resurrect a confirmed deletion.
        }
        if (post.AuthorId != thread.AuthorId || post.ParentChannelId != thread.ParentId)
        {
            throw new InvalidOperationException("Thread identity changed; review is required.");
        }
        if (post.Lifecycle == RecruitmentLifecycle.Missing)
        {
            post.Lifecycle = RecruitmentLifecycle.Open;
        }
        post.Title = thread.Title.Length <= 100 ? thread.Title : thread.Title[..100];
        post.AppliedTagIds = thread.TagIds;
        post.IsPinned = thread.Pinned;
        post.Observation.Archived = thread.Archived;
        post.Observation.Locked = thread.Locked;
        post.Observation.HasClosedTag = thread.HasClosedTag;
        post.Observation.LastSeenAtUtc = Now;
        // Natural archive/lock and a Closed tag are observations, not bot-applied policy transitions.
    }

    private static void ObserveResponse(RecruitmentPostRecord post, RecruitmentResponse response)
    {
        var qualifies = RecruitmentPolicyEvaluator.IsQualifyingResponse(post, response);
        if (qualifies is null)
        {
            post.Observation.HistoryUncertain = true;
        }
        if (qualifies == true && (post.FirstQualifyingResponseAtUtc is null || response.CreatedAtUtc < post.FirstQualifyingResponseAtUtc))
        {
            post.FirstQualifyingResponseAtUtc = response.CreatedAtUtc;
        }
    }

    private async Task CheckPostAsync(ulong id, CancellationToken token)
    {
        try
        {
            var thread = await discord.GetThreadAsync(id, token);
            if (thread is null)
            {
                // A failed lookup cannot establish when or why the post disappeared.
                await store.UpdateAsync(state =>
                {
                    var post = state.Posts[id];
                    post.Lifecycle = RecruitmentLifecycle.Missing;
                    post.DeletionTimeUncertain = true;
                    post.RequiresReview = true;
                    post.Observation.HistoryUncertain = true;
                    post.Observation.Error = "Thread unavailable; disappearance time/cause unknown. Review required.";
                    post.Observation.NextCheckAtUtc = Now.AddMinutes(5);
                    return true;
                }, token);
                return;
            }
            await store.UpdateAsync(state =>
            {
                ObserveThread(state, thread);
                return true;
            }, token);
            var savedPost = (await store.LoadAsync(token))!.Posts[id];
            var historyReadStartedAt = Now;
            var starter = await discord.GetStarterAsync(id, token);
            var messages = await discord.GetRepliesAsync(id, Math.Max(id, savedPost.Observation.HistoryAfterId), token);
            var facts = await discord.GetAuthorAsync(savedPost.AuthorId, token);
            await store.UpdateAsync(state =>
            {
                var post = state.Posts[id];
                post.Payment = RecruitmentContentAnalyzer.Analyze(starter?.Content, post.Forum);
                post.Observation.StarterHash = starter?.Content is { } content ? Hash(content) : null;
                post.Observation.Error = starter?.Content is null ? "Starter content unavailable." : null;
                post.Activity = facts.Activity;
                post.Observation.JoinedAtUtc = facts.JoinedAtUtc;
                RecordResponsePage(post, messages, historyReadStartedAt);
                return true;
            }, token);
        }
        catch (Exception e) when (CanRetry(e, token))
        {
            await store.UpdateAsync(state =>
            {
                var post = state.Posts[id];
                post.Observation.Error = Error(e);
                post.ResponsesCheckedThroughUtc = null;
                post.Observation.NextCheckAtUtc = Now.AddMinutes(2);
                return true;
            }, token);
        }
    }

    private void RecordResponsePage(RecruitmentPostRecord post, RecruitmentMessagePage messages,
        DateTimeOffset historyReadStartedAt)
    {
        foreach (var message in messages.Messages)
        {
            ObserveResponse(post, message.Response);
        }
        if (messages.Messages.Count > 0)
        {
            post.Observation.HistoryAfterId = Math.Max(post.Observation.HistoryAfterId, messages.Messages.Max(m => m.Id));
        }
        // Bound coverage to the start of the request; messages may arrive while it is in flight.
        if (messages.Complete && !post.Observation.HistoryUncertain)
        {
            post.ResponsesCheckedThroughUtc = historyReadStartedAt;
        }
        post.Observation.NextCheckAtUtc = messages.Complete ? Now.AddMinutes(5) : Now.AddSeconds(30);
    }

    private async Task PublishFeedAsync(ulong id, CancellationToken token)
    {
        try
        {
            var state = (await store.LoadAsync(token))!;
            var post = state.Posts[id];
            var marker = $"recruit-observe:{state.GuildId}:{id}";
            var content = RecruitmentObservationMessage.Build(state, post, new(Options, time), Options, marker, Now);
            var hash = Hash(content);
            if (post.Observation.FeedChannelId != 0 && post.Observation.FeedChannelId != Options.FeedChannelId)
            {
                throw new InvalidOperationException("Staff feed changed; existing delivery identities require explicit migration.");
            }
            if (post.FeedMessageId is { } savedId)
            {
                if (await discord.EditFeedAsync(savedId, marker, content, token))
                {
                    await CompleteFeedAsync(id, savedId, hash, token);
                    return;
                }

                // The saved message was removed. Clear its identity before recording a replacement send.
                await store.UpdateAsync(state =>
                {
                    state.Posts[id].FeedMessageId = null;
                    state.Posts[id].Observation.FeedHash = null;
                    return true;
                }, token);
            }
            FeedRecoveryResult recovery = await RecoverPendingFeedAsync(post, marker, content, hash, token);
            if (recovery != FeedRecoveryResult.NotFound)
            {
                return;
            }

            // Persist intent before sending: Discord may accept a message even if its response is lost.
            await store.UpdateAsync(state =>
            {
                var observation = state.Posts[id].Observation;
                observation.FeedChannelId = Options.FeedChannelId;
                observation.FeedSendRequestedAtUtc = Now;
                observation.FeedSearchBeforeId = null;
                return true;
            }, token);
            var sent = await discord.SendFeedAsync(content, token);
            await CompleteFeedAsync(id, sent, hash, token);
        }
        catch (Exception e) when (CanRetry(e, token))
        {
            await store.UpdateAsync(state =>
            {
                state.Posts[id].Observation.FeedError = Error(e);
                state.Posts[id].Observation.FeedRetryAtUtc = Now.AddMinutes(2);
                return true;
            }, token);
        }
    }

    private enum FeedRecoveryResult { NotFound, Recovered, SearchIncomplete }

    // A pending intent is an uncertain send, not permission to send again. Search for its marker first.
    private async Task<FeedRecoveryResult> RecoverPendingFeedAsync(RecruitmentPostRecord post, string marker, string content,
        string hash, CancellationToken token)
    {
        if (post.Observation.FeedSendRequestedAtUtc is { } requested)
        {
            var page = await discord.FindFeedAsync(marker, requested, post.Observation.FeedSearchBeforeId, token);
            if (page.FoundId is { } found)
            {
                if (await discord.EditFeedAsync(found, marker, content, token))
                {
                    await CompleteFeedAsync(post.ThreadId, found, hash, token);
                    return FeedRecoveryResult.Recovered;
                }
            }
            else if (!page.Complete)
            {
                if (page.BeforeId is null || post.Observation.FeedSearchBeforeId is { } before && page.BeforeId >= before)
                {
                    throw new InvalidOperationException("Staff-feed recovery cursor did not advance.");
                }
                await store.UpdateAsync(state =>
                {
                    state.Posts[post.ThreadId].Observation.FeedSearchBeforeId = page.BeforeId;
                    state.Posts[post.ThreadId].Observation.FeedRetryAtUtc = Now.AddSeconds(30);
                    return true;
                }, token);
                return FeedRecoveryResult.SearchIncomplete;
            }
        }
        return FeedRecoveryResult.NotFound;
    }

    private Task CompleteFeedAsync(ulong id, ulong messageId, string hash, CancellationToken token) => store.UpdateAsync(state =>
    {
        var post = state.Posts[id];
        post.FeedMessageId = messageId;
        post.Observation.FeedHash = hash;
        post.Observation.FeedSendRequestedAtUtc = null;
        post.Observation.FeedSearchBeforeId = null;
        post.Observation.FeedError = null;
        post.Observation.FeedRetryAtUtc = Now.AddMinutes(2);
        return true;
    }, token);

    private bool CanRetry(Exception e, CancellationToken token) => store.IsHealthy && !token.IsCancellationRequested &&
        e is not System.IO.InvalidDataException;
    private static string Error(Exception e) => e is InvalidOperationException ? e.Message[..Math.Min(e.Message.Length, 200)] :
        $"{e.GetType().Name}: read/delivery failed; retry pending.";
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
