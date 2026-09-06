namespace DiscordBot.Services.Recruitment;

public sealed class RecruitmentForumPublication
{
    public ulong? ClosedTagId { get; set; }
    public RecruitmentGuidelineReceipt? Confirmed { get; set; }
    public RecruitmentGuidelineReceipt? Candidate { get; set; }
    public string? ExpectedTopicHash { get; set; }
    public DateTimeOffset? CheckedAtUtc { get; set; }
    public string? Error { get; set; }
}

public sealed class RecruitmentGuidelineReceipt
{
    public string Code { get; set; } = "";
    public DateTimeOffset WeekStartUtc { get; set; }
    public string TopicHash { get; set; } = "";
    public string TemplateHash { get; set; } = "";
    public DateTimeOffset? PublishedAtUtc { get; set; }
}

public sealed class RecruitmentPostAdvisory
{
    public string Generation { get; set; } = "";
    public long Version { get; set; }
    public DateTimeOffset? SendRequestedAtUtc { get; set; }
    public ulong? SearchBeforeId { get; set; }
    public DateTimeOffset? NextCheckAtUtc { get; set; }
    public bool NeedsFreshWindow { get; set; }
    public int IncorrectAttempts { get; set; }
    public DateTimeOffset? RetryCodeAtUtc { get; set; }
    public string? Error { get; set; }
    public string? RenderError { get; set; }
    public RecruitmentOwnerConfirmation? Confirmation { get; set; }
    public RecruitmentOwnerAction? PendingAction { get; set; }
}

public sealed class RecruitmentOwnerConfirmation
{
    public string Token { get; set; } = "";
    public RecruitmentActionKind Kind { get; set; }
    public long Version { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

public sealed class RecruitmentOwnerAction
{
    public string Id { get; set; } = "";
    public RecruitmentActionKind Kind { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed record RecruitmentForumTag(ulong Id, string Name, bool Moderated, ulong? EmojiId, string? EmojiName);
public sealed record RecruitmentForumSetup(ulong Id, string Topic, IReadOnlyList<RecruitmentForumTag> Tags);
public sealed record RecruitmentPublicPost(ulong Id, ulong ParentId, ulong AuthorId, bool Pinned,
    bool Archived, bool Locked, bool? OrdinaryAuthor, ulong[] Tags);
public sealed record RecruitmentPublicMessage(ulong Id, DateTimeOffset CreatedAtUtc);
public sealed record RecruitmentAdvisorySearch(RecruitmentPublicMessage? Found, ulong? BeforeId, bool Complete);
public sealed record RecruitmentGuidelinePreview(string Topic, string CurrentTopicHash, string CurrentTagHash);
public sealed record RecruitmentOwnerContext(ulong GuildId, ulong ThreadId, ulong UserId);

/// <summary>Public mutations are isolated from Observe and called only in the managed Advisory lifetime.</summary>
public interface IRecruitmentPublisher
{
    Task<RecruitmentForumSetup> GetForumAsync(ulong forumId, CancellationToken token);
    Task AppendClosedTagAsync(ulong forumId, string expectedTagHash, CancellationToken token);
    Task PublishTopicAsync(ulong forumId, string expectedTopicHash, string topic, CancellationToken token);
    Task<RecruitmentPublicPost?> GetPostAsync(ulong threadId, CancellationToken token);
    Task<RecruitmentAdvisorySearch> FindAdvisoryAsync(ulong threadId, string marker, ulong? beforeId, CancellationToken token);
    Task<RecruitmentPublicMessage> SendAdvisoryAsync(ulong threadId, RecruitmentAdvisoryView view, byte[]? image, CancellationToken token);
    Task<bool> EditAdvisoryAsync(ulong threadId, ulong messageId, RecruitmentAdvisoryView view, CancellationToken token);
    Task ApplyOwnerActionAsync(RecruitmentPublicPost post, RecruitmentActionKind action, ulong? closedTagId, string actionId, CancellationToken token);
}
