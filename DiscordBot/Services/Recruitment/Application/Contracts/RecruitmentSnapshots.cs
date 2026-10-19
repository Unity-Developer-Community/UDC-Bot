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
