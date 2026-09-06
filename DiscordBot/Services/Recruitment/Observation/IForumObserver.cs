using DiscordBot.Services.Recruitment.State;
namespace DiscordBot.Services.Recruitment.Observation;

public enum EventKind { Changed, Deleted, Message, Gap }
public sealed record ObservationEvent(EventKind Kind, ulong ThreadId = 0,
    ulong ParentId = 0, MessageSnapshot? Message = null);
public sealed record ThreadSnapshot(ulong Id, ulong ParentId, ulong AuthorId,
    DateTimeOffset CreatedAtUtc, string Title, ulong[] TagIds, bool Archived, bool Locked,
    bool Pinned, bool HasClosedTag, DateTimeOffset ArchiveTimestampUtc);
public sealed record MessageSnapshot(ulong Id, ReplyEvidence Response, string? Content);
public sealed record ArchivePage(IReadOnlyList<ThreadSnapshot> Threads,
    DateTimeOffset? BeforeUtc, bool Complete);
public sealed record MessagePage(IReadOnlyList<MessageSnapshot> Messages, bool Complete);
public sealed record FeedPage(ulong? FoundId, ulong? BeforeId, bool Complete);
public sealed record AuthorFacts(ActivityStatus Activity, DateTimeOffset? JoinedAtUtc);

/// <summary>
/// Observe's entire Discord boundary. The only writes are to the configured staff feed.
/// Null thread means a confirmed Unknown Channel response; permission/network failures throw.
/// </summary>
public interface IForumObserver
{
    IDisposable Subscribe(Action<ObservationEvent> receive);
    Task ValidateAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ThreadSnapshot>> GetActiveAsync(ulong forumId, CancellationToken cancellationToken);
    Task<ArchivePage> GetArchivedAsync(ulong forumId, DateTimeOffset? before, CancellationToken cancellationToken);
    Task<ThreadSnapshot?> GetThreadAsync(ulong threadId, CancellationToken cancellationToken);
    Task<MessageSnapshot?> GetStarterAsync(ulong threadId, CancellationToken cancellationToken);
    Task<MessagePage> GetRepliesAsync(ulong threadId, ulong afterId, CancellationToken cancellationToken);
    Task<AuthorFacts> GetAuthorAsync(ulong authorId, CancellationToken cancellationToken);
    Task<FeedPage> FindFeedAsync(string marker, DateTimeOffset since, ulong? beforeId, CancellationToken cancellationToken);
    Task<ulong> SendFeedAsync(string content, CancellationToken cancellationToken);
    // False means the saved message no longer exists. Refuse to edit any unowned message.
    Task<bool> EditFeedAsync(ulong messageId, string marker, string content, CancellationToken cancellationToken);
}
