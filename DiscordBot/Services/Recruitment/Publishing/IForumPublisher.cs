using DiscordBot.Services.Recruitment.Presentation;
using DiscordBot.Services.Recruitment.State;
namespace DiscordBot.Services.Recruitment.Publishing;

public sealed record ForumTag(ulong Id, string Name, bool Moderated, ulong? EmojiId, string? EmojiName);
public sealed record ForumSetup(ulong Id, string Topic, IReadOnlyList<ForumTag> Tags);
public sealed record PublicPost(ulong Id, ulong ParentId, ulong AuthorId, bool Pinned,
    bool Archived, bool Locked, bool? OrdinaryAuthor, ulong[] Tags);
public sealed record PublicMessage(ulong Id, DateTimeOffset CreatedAtUtc);
public sealed record AdvisorySearch(PublicMessage? Found, ulong? BeforeId, bool Complete);
public sealed record GuidelinePreview(string Topic, string CurrentTopicHash, string CurrentTagHash);

/// <summary>Public mutations are isolated from Observe and called only in the managed Advisory/Enforce lifetime.</summary>
public interface IForumPublisher
{
    Task<ForumSetup> GetForumAsync(ulong forumId, CancellationToken token);
    Task AppendClosedTagAsync(ulong forumId, string expectedTagHash, CancellationToken token);
    Task PublishTopicAsync(ulong forumId, string expectedTopicHash, string topic, CancellationToken token);
    Task<PublicPost?> GetPostAsync(ulong threadId, CancellationToken token);
    Task<AdvisorySearch> FindAdvisoryAsync(ulong threadId, string marker, ulong? beforeId, CancellationToken token);
    Task<PublicMessage> SendAdvisoryAsync(ulong threadId, AdvisoryView view, byte[]? image, CancellationToken token);
    Task<bool> EditAdvisoryAsync(ulong threadId, ulong messageId, AdvisoryView view, CancellationToken token);
    Task ApplyLifecycleActionAsync(PublicPost post, ActionKind action, ulong? closedTagId, string actionId, CancellationToken token);
}
