using DiscordBot.Services.Recruitment.Policy;
namespace DiscordBot.Services.Recruitment.State;

internal static class ListingHistory
{
    public static AuthorRecord Author(StateDocument state, PostRecord post)
    {
        if (!state.Authors.TryGetValue(post.AuthorId, out var author))
            state.Authors[post.AuthorId] = author = new() { UserId = post.AuthorId };
        return author;
    }

    public static GroupHistory Group(AuthorRecord author, ForumKind forum)
    {
        var group = ForumClassifier.GroupOf(forum);
        if (!author.Groups.TryGetValue(group, out var history)) author.Groups[group] = history = new();
        return history;
    }

    public static void RecordDeletion(StateDocument state, PostRecord post, DateTimeOffset now)
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
