namespace DiscordBot.Services.Recruitment;

public enum RecruitmentEventKind { Changed, Deleted, Message, Gap }
public sealed record RecruitmentObservationEvent(RecruitmentEventKind Kind, ulong ThreadId = 0,
    ulong ParentId = 0, RecruitmentMessageSnapshot? Message = null);
public sealed record RecruitmentThreadSnapshot(ulong Id, ulong ParentId, ulong AuthorId,
    DateTimeOffset CreatedAtUtc, string Title, ulong[] TagIds, bool Archived, bool Locked,
    bool Pinned, bool HasClosedTag, DateTimeOffset ArchiveTimestampUtc);
public sealed record RecruitmentMessageSnapshot(ulong Id, RecruitmentResponse Response, string? Content);
public sealed record RecruitmentArchivePage(IReadOnlyList<RecruitmentThreadSnapshot> Threads,
    DateTimeOffset? BeforeUtc, bool Complete);
public sealed record RecruitmentMessagePage(IReadOnlyList<RecruitmentMessageSnapshot> Messages, bool Complete);
public sealed record RecruitmentFeedPage(ulong? FoundId, ulong? BeforeId, bool Complete);
public sealed record RecruitmentAuthorFacts(RecruitmentActivity Activity, DateTimeOffset? JoinedAtUtc);

/// <summary>
/// Observe's entire Discord boundary. The only writes are to the configured staff feed.
/// Null thread means a confirmed Unknown Channel response; permission/network failures throw.
/// </summary>
public interface IRecruitmentObserver
{
    IDisposable Subscribe(Action<RecruitmentObservationEvent> receive);
    Task ValidateAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<RecruitmentThreadSnapshot>> GetActiveAsync(ulong forumId, CancellationToken cancellationToken);
    Task<RecruitmentArchivePage> GetArchivedAsync(ulong forumId, DateTimeOffset? before, CancellationToken cancellationToken);
    Task<RecruitmentThreadSnapshot?> GetThreadAsync(ulong threadId, CancellationToken cancellationToken);
    Task<RecruitmentMessageSnapshot?> GetStarterAsync(ulong threadId, CancellationToken cancellationToken);
    Task<RecruitmentMessagePage> GetRepliesAsync(ulong threadId, ulong afterId, CancellationToken cancellationToken);
    Task<RecruitmentAuthorFacts> GetAuthorAsync(ulong authorId, CancellationToken cancellationToken);
    Task<RecruitmentFeedPage> FindFeedAsync(string marker, DateTimeOffset since, ulong? beforeId, CancellationToken cancellationToken);
    Task<ulong> SendFeedAsync(string content, CancellationToken cancellationToken);
    // False means the saved message no longer exists. Refuse to edit any unowned message.
    Task<bool> EditFeedAsync(ulong messageId, string marker, string content, CancellationToken cancellationToken);
}
