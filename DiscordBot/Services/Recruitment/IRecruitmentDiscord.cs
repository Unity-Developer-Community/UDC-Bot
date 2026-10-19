namespace DiscordBot.Services.Recruitment;

public sealed record LiveTag(string Id, string Name, bool Moderated, IEmote? Emoji = null);
public sealed record ForumSnapshot(string Id, string Topic, bool RequireTag, IReadOnlyList<LiveTag> Tags, ChannelFlags Flags = ChannelFlags.None);
public sealed record ThreadCreation(string Id, string ForumId, string OwnerId, string Title, DateTimeOffset CreatedAt);
public sealed record RecruitmentAuthor(bool IsBot, bool IsStaff);
public sealed record ThreadSnapshot(string Id, string ForumId, string OwnerId, string Title, DateTimeOffset CreatedAt,
    bool OwnerIsBot, bool Pinned, bool Archived, bool Locked, IReadOnlyList<string> Tags, string? Starter,
    DateTimeOffset? AccountCreatedAt, DateTimeOffset? JoinedAt, bool OwnerIsStaff = false, int? MemberRoleCount = null, bool? HasCustomAvatar = null, IReadOnlyList<StarterAttachment>? Attachments = null,
    string? ServerNickname = null, string? GlobalName = null, string? Username = null, string? AvatarUrl = null, string? AvatarIdentity = null);
public sealed record ArchiveCursor(DateTimeOffset Since, DateTimeOffset? Before = null, DateTimeOffset? StartedAt = null);
public sealed record ThreadInventory(IReadOnlyList<ThreadSnapshot> Threads, bool Complete, IReadOnlyDictionary<string, ArchiveCursor>? Cursors = null);
public enum RecruitmentMessageFormat { Standard, ComponentsV2 }
public sealed record RecruitmentMedia(string FileName, byte[] Bytes, string Description);
public sealed record RecruitmentMessage(string Content, string Marker, bool OwnerControls, bool RestoreArchive = false, Embed? Embed = null, string? TextFallback = null, bool AcknowledgementRequired = false, bool ReaderControls = false, RecruitmentMessageFormat Format = RecruitmentMessageFormat.Standard,
    string? RenderIdentity = null, MessageComponent? Components = null, MessageComponent? FallbackComponents = null, RecruitmentMedia? Media = null, bool RestoreLocked = false, ulong? MentionUserId = null)
{
    public string RenderKey => RenderIdentity ?? Content + "\n" + OwnerControls + "\n" + AcknowledgementRequired + "\n" + ReaderControls + "\n" + EmbedKey(Embed);
    public static string EmbedKey(IEmbed? embed) => embed is null ? "" : Newtonsoft.Json.JsonConvert.SerializeObject(embed.ToEmbedBuilder().Build());
}
public sealed record Delivery(string Id, bool NewlySent, string? MediaAttachmentId = null, string? MediaFileName = null);

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
