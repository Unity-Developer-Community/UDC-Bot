namespace DiscordBot.Services.Recruitment;

/// <summary>Null thread means confirmed Discord Unknown Channel, never a cache miss or permission failure.</summary>
public interface IRecruitmentDiscord
{
    Task ValidateAccessAsync(CancellationToken ct);
    Task<bool> ValidateFeedAsync(CancellationToken ct);
    Task<bool> IsMemberAsync(string guildId, string userId, CancellationToken ct);
    Task<RecruitmentAuthor?> GetAuthorAsync(string userId, CancellationToken ct);
    Task<bool> IsAdministratorAsync(string guildId, string userId, CancellationToken ct);
    Task<bool> IsModeratorAsync(string guildId, string userId, CancellationToken ct);
    bool IsExpectedForumUpdate(ForumSnapshot snapshot);
    Task<ForumSnapshot> GetForumAsync(string forumId, CancellationToken ct);
    Task<ForumSnapshot> PublishForumAsync(ForumSnapshot expected, string? topic, IReadOnlyList<LiveTag>? tags, bool requireTag, CancellationToken ct);
    Task<ThreadInventory> InventoryAsync(DateTimeOffset floor, IReadOnlySet<string> knownIds, IReadOnlyDictionary<string, ArchiveCursor> cursors, CancellationToken ct);
    Task<ThreadSnapshot?> GetThreadAsync(string threadId, CancellationToken ct, bool refresh = false);
    Task<string> CreateReferenceAsync(string forumId, string closedTagId, string marker, CancellationToken ct);
    Task<bool> SecureReferenceAsync(string forumId, string threadId, bool botOwned, string marker, CancellationToken ct);
    void InvalidateThread(string threadId);
    void InvalidateFacts(string threadId);
    void InvalidateCache();
    // Ambiguous sends must search the entire bounded recovery interval or hold; never blindly resend.
    Task<Delivery> UpsertAsync(string channelId, string? messageId, bool recovering, DateTimeOffset since,
        RecruitmentMessage message, Action beforeWrite, CancellationToken ct);
    // False means the exact delivered message is absent. Persist restoration intent before writes,
    // and clear it only after restoration is confirmed.
    Task<bool> PinAdvisoryAsync(string threadId, string messageId, bool restoreArchive, bool restoreLocked,
        Action<bool, bool> beforeWrite, CancellationToken ct);
    Task CloseAsync(ThreadSnapshot thread, string? closedTagId, bool protectPinned, CancellationToken ct, Func<bool>? allowWrite = null);
    Task ReopenAsync(ThreadSnapshot thread, string? closedTagId, CancellationToken ct);
    Task DeleteAsync(ThreadSnapshot thread, string reason, bool protectPinned, CancellationToken ct, Func<bool>? allowWrite = null);
    Task DeleteAdvisoryAsync(string threadId, string messageId, CancellationToken ct, string? marker = null);
}
