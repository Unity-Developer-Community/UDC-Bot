namespace DiscordBot.Services.Recruitment;

public enum RecruitmentActionOrigin { Owner, Automatic, Moderator }

/// <summary>One durable intent. Completion and its history/count effects commit in the same state transaction.</summary>
public sealed class RecruitmentLifecycleAction
{
    public string Id { get; set; } = "";
    public RecruitmentActionKind Kind { get; set; }
    public RecruitmentActionOrigin Origin { get; set; }
    public RecruitmentCloseReason Reason { get; set; }
    public ulong ActorId { get; set; }
    public string Note { get; set; } = "";
    public long ExpectedVersion { get; set; }
    public bool ReviewRequiredAtRequest { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? AttemptedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public int? TimeoutCountAfter { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPending => CompletedAtUtc is null && CancelledAtUtc is null;
}

public sealed class RecruitmentCooldownWaiver
{
    public DateTimeOffset? ThroughCreatedAtUtc { get; set; }
    public DateTimeOffset? ThroughDeletedAtUtc { get; set; }
    public DateTimeOffset GrantedAtUtc { get; set; }
    public ulong ActorId { get; set; }
    public string Reason { get; set; } = "";
}

public sealed record RecruitmentAuditRecord(string Id, DateTimeOffset AtUtc, ulong ActorId, string Operation, string Reason);

internal static class RecruitmentHistory
{
    public static RecruitmentAuthorRecord Author(RecruitmentStateDocument state, RecruitmentPostRecord post)
    {
        if (!state.Authors.TryGetValue(post.AuthorId, out var author))
            state.Authors[post.AuthorId] = author = new() { UserId = post.AuthorId };
        return author;
    }

    public static RecruitmentGroupHistory Group(RecruitmentAuthorRecord author, RecruitmentForumKind forum)
    {
        var group = RecruitmentForumClassifier.GroupOf(forum);
        if (!author.Groups.TryGetValue(group, out var history)) author.Groups[group] = history = new();
        return history;
    }

    public static void RecordDeletion(RecruitmentStateDocument state, RecruitmentPostRecord post, DateTimeOffset now)
    {
        post.DeletedObservedAtUtc ??= now;
        post.DeletionTimeUncertain = false;
        if (post.AcceptedAtUtc is null) return;
        var author = Author(state, post);
        var group = Group(author, post.Forum);
        group.LastAcceptedDeletedAtUtc = Later(group.LastAcceptedDeletedAtUtc, post.DeletedObservedAtUtc);
        author.LastActivityAtUtc = now;
    }

    public static DateTimeOffset? Later(DateTimeOffset? first, DateTimeOffset? second) =>
        first is null ? second : second is null || first >= second ? first : second;
}
