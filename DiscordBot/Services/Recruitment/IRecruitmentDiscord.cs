namespace DiscordBot.Services.Recruitment;

public sealed record LiveTag(string Id, string Name, bool Moderated, IEmote? Emoji = null);
public sealed record ForumSnapshot(string Id, string Topic, bool RequireTag, IReadOnlyList<LiveTag> Tags, ChannelFlags Flags = ChannelFlags.None);
public sealed record ThreadSnapshot(string Id, string ForumId, string OwnerId, string Title, DateTimeOffset CreatedAt,
    bool OwnerIsBot, bool Pinned, bool Archived, bool Locked, IReadOnlyList<string> Tags, string? Starter,
    DateTimeOffset? AccountCreatedAt, DateTimeOffset? JoinedAt, bool OwnerIsStaff = false, int? MemberRoleCount = null);
public sealed record ArchiveCursor(DateTimeOffset Since, DateTimeOffset? Before = null, DateTimeOffset? StartedAt = null);
public sealed record ThreadInventory(IReadOnlyList<ThreadSnapshot> Threads, bool Complete, IReadOnlyDictionary<string, ArchiveCursor>? Cursors = null);
public sealed record RecruitmentMessage(string Content, string Marker, bool OwnerControls, bool RestoreArchive = false, Embed? Embed = null, string? TextFallback = null)
{
    public string RenderKey => Content + "\n" + OwnerControls + "\n" + EmbedKey(Embed);
    public static string EmbedKey(IEmbed? embed) => embed is null ? "" : Newtonsoft.Json.JsonConvert.SerializeObject(embed.ToEmbedBuilder().Build());
}
public sealed record Delivery(string Id, bool NewlySent);

/// <summary>Null thread means confirmed Discord Unknown Channel, never a cache miss or permission failure.</summary>
public interface IRecruitmentDiscord
{
    Task ValidateAccessAsync(CancellationToken ct);
    Task<bool> ValidateFeedAsync(CancellationToken ct);
    Task<string?> LatestFeedMessageAsync(CancellationToken ct);
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
    Task CloseAsync(ThreadSnapshot thread, string? closedTagId, bool protectPinned, CancellationToken ct, Func<bool>? allowWrite = null);
    Task ReopenAsync(ThreadSnapshot thread, string? closedTagId, CancellationToken ct);
    Task DeleteAsync(ThreadSnapshot thread, string reason, bool protectPinned, CancellationToken ct, Func<bool>? allowWrite = null);
    Task DeleteAdvisoryAsync(string threadId, string messageId, CancellationToken ct, string? marker = null);
}
