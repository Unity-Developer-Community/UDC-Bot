using System.Collections.Concurrent;
using Discord.WebSocket;

namespace DiscordBot.Services.Moderation;

/// <summary>
/// Keeps the bounded, in-memory state needed to audit a deleted guild thread.
/// At the configured hard limits, retained message text uses approximately 1.3 MiB
/// (120 snapshots × 6 messages × 900 UTF-16 characters) before object metadata.
/// </summary>
internal sealed class ForumPostTracker
{
    internal const int MaxActivePosts = 100;
    internal const int MaxRecentlyDeletedPosts = 20;
    internal const int MaxRecentlyDeletedThreadIds = 500;
    internal const int LargeThreadMessageCount = 6;
    internal const int LargeThreadParticipantCount = 6;
    internal const int MaxMessagesPerPost = 6;
    internal const int MaxParticipantsPerPost = 50;
    internal const int MaxMessageLength = 900;

    private static readonly TimeSpan DeletedPostRetention = TimeSpan.FromMinutes(1);

    private readonly ulong _moderatorRoleId;
    private readonly ConcurrentDictionary<ulong, ForumPostSnapshot> _activePosts = new();
    private readonly ConcurrentDictionary<ulong, RecentlyDeletedForumPost> _recentlyDeletedPosts = new();
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _recentlyDeletedThreadIds = new();

    internal ForumPostTracker(ulong moderatorRoleId)
    {
        _moderatorRoleId = moderatorRoleId;
    }

    internal ForumPostSnapshot? GetOrTrack(ulong channelId, SocketThreadChannel? thread)
    {
        RemoveExpiredRecentlyDeletedPosts();

        if (_activePosts.TryGetValue(channelId, out var snapshot))
            return thread == null ? snapshot : TrackThread(thread);

        if (TryGetRecentlyDeleted(channelId, out var deletedSnapshot))
            return deletedSnapshot;

        return TrackThread(thread);
    }

    internal ForumPostSnapshot? TrackThread(SocketThreadChannel? thread)
    {
        if (thread?.ParentChannel is not SocketGuildChannel parentChannel)
            return null;

        if (TryGetRecentlyDeleted(thread.Id, out var deletedSnapshot))
            return deletedSnapshot;
        if (IsRecentlyDeleted(thread.Id))
            return null;

        var candidate = new ForumPostSnapshot(
            thread.Id,
            thread.Name,
            thread.MessageCount,
            parentChannel,
            thread.Owner.Id,
            thread.Owner.GetUserPreferredName(),
            DateTimeOffset.UtcNow,
            new ConcurrentDictionary<ulong, ForumPostParticipant>(),
            new ConcurrentDictionary<ulong, ForumPostMessage>());
        var snapshot = _activePosts.AddOrUpdate(
            thread.Id,
            candidate,
            (_, existing) => candidate with
            {
                Participants = existing.Participants,
                Messages = existing.Messages
            });

        ObserveParticipant(snapshot, thread.Owner);
        TrimActivePosts(thread.Id);
        return snapshot;
    }

    internal ForumPostSnapshot? ObserveMessage(IMessage message)
    {
        if (message.Channel is not SocketThreadChannel thread)
            return null;

        var snapshot = GetOrTrack(thread.Id, thread);
        if (snapshot != null)
            ObserveMessage(snapshot, message);

        return snapshot;
    }

    internal void ObserveMessage(ForumPostSnapshot snapshot, IMessage message)
    {
        ObserveParticipant(snapshot, message.Author);
        if (message.Author.IsUserBotOrWebhook() ||
            !ShouldRetainMessage(message, snapshot))
        {
            return;
        }

        snapshot.Messages[message.Id] = new ForumPostMessage(
            message.Id,
            message.Author.GetUserPreferredName(),
            message.Timestamp,
            message.FormatForAuditLog(MaxMessageLength).Value);
        TrimMessages(snapshot);
    }

    internal bool IsRecentlyDeleted(ulong threadId)
    {
        RemoveExpiredRecentlyDeletedPosts();
        if (_recentlyDeletedThreadIds.TryGetValue(threadId, out var expiresAt) &&
            expiresAt > DateTimeOffset.UtcNow)
        {
            return true;
        }

        _recentlyDeletedThreadIds.TryRemove(threadId, out _);
        return false;
    }

    private bool ShouldRetainMessage(IMessage message, ForumPostSnapshot snapshot) =>
        message.Id == snapshot.ThreadId ||
        message.Author.IsModeratorOrAdministrator(_moderatorRoleId);

    internal ForumPostSnapshot? MarkDeleted(
        ulong threadId,
        SocketThreadChannel? thread = null)
    {
        RemoveExpiredRecentlyDeletedPosts();

        var snapshot = thread == null
            ? GetExisting(threadId)
            : TrackThread(thread);

        var expiresAt = DateTimeOffset.UtcNow + DeletedPostRetention;
        _recentlyDeletedThreadIds[threadId] = expiresAt;
        TrimRecentlyDeletedThreadIds(threadId);
        if (snapshot == null)
            return null;

        _recentlyDeletedPosts[threadId] = new RecentlyDeletedForumPost(
            snapshot,
            expiresAt);
        _activePosts.TryRemove(threadId, out _);
        TrimRecentlyDeletedPosts(threadId);
        return snapshot;
    }

    internal IReadOnlyCollection<ForumPostSnapshot> MarkParentChannelDeleted(ulong parentChannelId)
    {
        RemoveExpiredRecentlyDeletedPosts();

        var snapshots = _activePosts.Values
            .Where(snapshot => snapshot.ParentChannel.Id == parentChannelId)
            .ToList();
        var expiresAt = DateTimeOffset.UtcNow + DeletedPostRetention;
        foreach (var snapshot in snapshots)
        {
            _recentlyDeletedThreadIds[snapshot.ThreadId] = expiresAt;
            _activePosts.TryRemove(snapshot.ThreadId, out _);
            _recentlyDeletedPosts.TryRemove(snapshot.ThreadId, out _);
        }

        TrimRecentlyDeletedThreadIds(default);
        return snapshots;
    }

    private ForumPostSnapshot? GetExisting(ulong threadId)
    {
        if (_activePosts.TryGetValue(threadId, out var snapshot))
            return snapshot;

        return TryGetRecentlyDeleted(threadId, out var deletedSnapshot)
            ? deletedSnapshot
            : null;
    }

    private bool TryGetRecentlyDeleted(
        ulong threadId,
        out ForumPostSnapshot? snapshot)
    {
        if (_recentlyDeletedPosts.TryGetValue(threadId, out var deletedPost) &&
            deletedPost.ExpiresAt > DateTimeOffset.UtcNow)
        {
            snapshot = deletedPost.Snapshot;
            return true;
        }

        _recentlyDeletedPosts.TryRemove(threadId, out _);
        snapshot = null;
        return false;
    }

    private void RemoveExpiredRecentlyDeletedPosts()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (threadId, post) in _recentlyDeletedPosts)
        {
            if (post.ExpiresAt <= now)
                _recentlyDeletedPosts.TryRemove(threadId, out _);
        }

        foreach (var (threadId, expiresAt) in _recentlyDeletedThreadIds)
        {
            if (expiresAt <= now)
                _recentlyDeletedThreadIds.TryRemove(threadId, out _);
        }
    }

    private void ObserveParticipant(ForumPostSnapshot snapshot, IUser participant)
    {
        if (participant.IsUserBotOrWebhook())
            return;

        var guildUser = participant as IGuildUser;
        snapshot.Participants[participant.Id] = new ForumPostParticipant(
            participant.Id,
            participant.GetUserPreferredName(),
            participant.HasRoleGroup(_moderatorRoleId),
            guildUser?.GuildPermissions.Administrator == true,
            DateTimeOffset.UtcNow);
        TrimParticipants(snapshot);
    }

    private static void TrimMessages(ForumPostSnapshot snapshot)
    {
        while (snapshot.Messages.Count > MaxMessagesPerPost)
        {
            // Retain the starter post and the newest staff messages when the snapshot reaches its cap.
            var messageToRemove = snapshot.Messages.Values
                .Where(message => message.Id != snapshot.ThreadId)
                .OrderBy(message => message.Timestamp)
                .FirstOrDefault();
            if (messageToRemove == null)
                break;

            snapshot.Messages.TryRemove(messageToRemove.Id, out _);
        }
    }

    private static void TrimParticipants(ForumPostSnapshot snapshot)
    {
        while (snapshot.Participants.Count > MaxParticipantsPerPost)
        {
            var participantToRemove = snapshot.Participants.Values
                .Where(participant => participant.Id != snapshot.AuthorId)
                .OrderBy(participant => participant.IsModerator || participant.IsAdministrator)
                .ThenBy(participant => participant.LastObservedAt)
                .FirstOrDefault();
            if (participantToRemove == null)
                break;

            snapshot.Participants.TryRemove(participantToRemove.Id, out _);
        }
    }

    private void TrimActivePosts(ulong currentThreadId)
    {
        while (_activePosts.Count > MaxActivePosts)
        {
            var postToRemove = _activePosts.Values
                .Where(snapshot => snapshot.ThreadId != currentThreadId)
                .OrderBy(snapshot => snapshot.LastObservedAt)
                .FirstOrDefault();
            if (postToRemove == null)
                break;

            _activePosts.TryRemove(postToRemove.ThreadId, out _);
        }
    }

    private void TrimRecentlyDeletedPosts(ulong currentThreadId)
    {
        while (_recentlyDeletedPosts.Count > MaxRecentlyDeletedPosts)
        {
            var postToRemove = _recentlyDeletedPosts.Values
                .Where(post => post.Snapshot.ThreadId != currentThreadId)
                .OrderBy(post => post.ExpiresAt)
                .FirstOrDefault();
            if (postToRemove == null)
                break;

            _recentlyDeletedPosts.TryRemove(postToRemove.Snapshot.ThreadId, out _);
        }
    }

    private void TrimRecentlyDeletedThreadIds(ulong currentThreadId)
    {
        while (_recentlyDeletedThreadIds.Count > MaxRecentlyDeletedThreadIds)
        {
            var threadIdToRemove = _recentlyDeletedThreadIds
                .Where(pair => pair.Key != currentThreadId)
                .OrderBy(pair => pair.Value)
                .Select(pair => pair.Key)
                .FirstOrDefault();
            if (threadIdToRemove == default)
                break;

            _recentlyDeletedThreadIds.TryRemove(threadIdToRemove, out _);
        }
    }
}

internal sealed record ForumPostSnapshot(
    ulong ThreadId,
    string Title,
    int MessageCount,
    SocketGuildChannel ParentChannel,
    ulong AuthorId,
    string AuthorName,
    DateTimeOffset LastObservedAt,
    ConcurrentDictionary<ulong, ForumPostParticipant> Participants,
    ConcurrentDictionary<ulong, ForumPostMessage> Messages);

internal sealed record ForumPostParticipant(
    ulong Id,
    string Name,
    bool IsModerator,
    bool IsAdministrator,
    DateTimeOffset LastObservedAt);

internal sealed record ForumPostMessage(
    ulong Id,
    string AuthorName,
    DateTimeOffset Timestamp,
    string Value);

internal sealed record RecentlyDeletedForumPost(
    ForumPostSnapshot Snapshot,
    DateTimeOffset ExpiresAt);
