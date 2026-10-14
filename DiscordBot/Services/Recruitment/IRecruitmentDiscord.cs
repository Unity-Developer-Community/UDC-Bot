namespace DiscordBot.Services.Recruitment;

public sealed record LiveTag(string Id, string Name, bool Moderated, IEmote? Emoji = null);
public sealed record ForumSnapshot(string Id, string Topic, bool RequireTag, IReadOnlyList<LiveTag> Tags, ChannelFlags Flags = ChannelFlags.None);
public sealed record ThreadSnapshot(string Id, string ForumId, string OwnerId, string Title, DateTimeOffset CreatedAt,
    bool OwnerIsBot, bool Pinned, bool Archived, bool Locked, IReadOnlyList<string> Tags, string? Starter,
    DateTimeOffset? AccountCreatedAt, DateTimeOffset? JoinedAt);
public sealed record ArchiveCursor(DateTimeOffset Since, DateTimeOffset? Before = null, DateTimeOffset? StartedAt = null);
public sealed record ThreadInventory(IReadOnlyList<ThreadSnapshot> Threads, bool Complete, IReadOnlyDictionary<string, ArchiveCursor>? Cursors = null);
public sealed record ReplyObservation(string ThreadId, string AuthorId, bool IsBot, bool IsWebhook, bool? IsStaff);
public sealed record RecruitmentMessage(string Content, string Marker, bool OwnerControls, byte[]? Image = null, string? ImageDescription = null, bool RestoreArchive = false);
public sealed record Delivery(string Id, bool NewlySent);

/// <summary>Null thread means confirmed Discord Unknown Channel, never a cache miss or permission failure.</summary>
public interface IRecruitmentDiscord
{
    Task ValidateAccessAsync(CancellationToken ct);
    Task<bool> IsModeratorAsync(string guildId, string userId, CancellationToken ct);
    Task<ForumSnapshot> GetForumAsync(string forumId, CancellationToken ct);
    Task<ForumSnapshot> PublishForumAsync(ForumSnapshot expected, string? topic, IReadOnlyList<LiveTag>? tags, bool requireTag, CancellationToken ct);
    Task<ThreadInventory> InventoryAsync(DateTimeOffset floor, IReadOnlySet<string> knownIds, IReadOnlyDictionary<string, ArchiveCursor> cursors, CancellationToken ct);
    Task<ThreadSnapshot?> GetThreadAsync(string threadId, CancellationToken ct);
    Task<bool?> HasReplyAsync(ThreadSnapshot thread, CancellationToken ct);
    Task<string> GetActivityAsync(string userId, CancellationToken ct);
    // Ambiguous sends must search the entire bounded recovery interval or hold; never blindly resend.
    Task<Delivery> UpsertAsync(string channelId, string? messageId, bool recovering, DateTimeOffset since,
        RecruitmentMessage message, Action beforeWrite, CancellationToken ct);
    Task CloseAsync(ThreadSnapshot thread, string? closedTagId, bool protectPinned, CancellationToken ct);
    Task ReopenAsync(ThreadSnapshot thread, string? closedTagId, CancellationToken ct);
    Task DeleteAsync(ThreadSnapshot thread, string reason, bool protectPinned, CancellationToken ct);
    Task DeleteAdvisoryAsync(string threadId, string messageId, CancellationToken ct);
}
