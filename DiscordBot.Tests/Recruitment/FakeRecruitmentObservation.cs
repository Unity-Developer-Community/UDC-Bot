using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

internal sealed class FakeRecruitmentObservation(RecruitmentStateStore store, FakePublisher publisher) : IRecruitmentObserver
{
    public bool FailFeed;
    public int FeedSends;
    public Dictionary<ulong, string> Feed { get; } = [];
    public Dictionary<ulong, DateTimeOffset> CreatedTimes { get; } = [];
    public RecruitmentMessagePage Replies { get; set; } = new([], true);
    public Action<RecruitmentObservationEvent>? Receive;
    public IDisposable Subscribe(Action<RecruitmentObservationEvent> receive)
    {
        Receive = receive;
        return new Subscription(() => Receive = null);
    }
    public Task ValidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public async Task<IReadOnlyList<RecruitmentThreadSnapshot>> GetActiveAsync(ulong forumId, CancellationToken cancellationToken)
    {
        List<RecruitmentThreadSnapshot> result = [];
        foreach (var post in publisher.Posts.Values.Where(post => post.ParentId == forumId && !post.Archived))
            result.Add((await GetThreadAsync(post.Id, cancellationToken))!);
        return result;
    }
    public async Task<RecruitmentArchivePage> GetArchivedAsync(ulong forumId, DateTimeOffset? before, CancellationToken cancellationToken)
    {
        List<RecruitmentThreadSnapshot> result = [];
        foreach (var post in publisher.Posts.Values.Where(post => post.ParentId == forumId && post.Archived))
            result.Add((await GetThreadAsync(post.Id, cancellationToken))!);
        return new(result, null, true);
    }
    public async Task<RecruitmentThreadSnapshot?> GetThreadAsync(ulong threadId, CancellationToken cancellationToken)
    {
        var live = await publisher.GetPostAsync(threadId, cancellationToken);
        if (live is null) return null;
        var state = await store.LoadAsync(cancellationToken);
        var post = state?.Posts.GetValueOrDefault(threadId);
        var created = post?.CreatedAtUtc ?? CreatedTimes.GetValueOrDefault(threadId, RecruitmentTestData.Now);
        bool closed = publisher.Forums[live.ParentId].Tags.Any(tag => live.Tags.Contains(tag.Id) && tag.Name == "Closed");
        return new(threadId, live.ParentId, live.AuthorId, created, post?.Title ?? "Listing", live.Tags,
            live.Archived, live.Locked, live.Pinned, closed, created);
    }
    public Task<RecruitmentMessageSnapshot?> GetStarterAsync(ulong threadId, CancellationToken cancellationToken) =>
        Task.FromResult<RecruitmentMessageSnapshot?>(new(threadId, new(123, RecruitmentTestData.Now, false, false, false, false), "Budget $40/hour"));
    public Task<RecruitmentMessagePage> GetRepliesAsync(ulong threadId, ulong afterId, CancellationToken cancellationToken) => Task.FromResult(Replies);
    public Task<RecruitmentAuthorFacts> GetAuthorAsync(ulong authorId, CancellationToken cancellationToken) => Task.FromResult(new RecruitmentAuthorFacts(RecruitmentActivity.Unknown, null));
    public Task<RecruitmentFeedPage> FindFeedAsync(string marker, DateTimeOffset since, ulong? beforeId, CancellationToken cancellationToken)
    {
        if (FailFeed) throw new IOException("feed unavailable");
        ulong found = Feed.FirstOrDefault(entry => entry.Value.Contains(marker, StringComparison.Ordinal)).Key;
        return Task.FromResult(new RecruitmentFeedPage(found == 0 ? null : found, null, true));
    }
    public Task<ulong> SendFeedAsync(string content, CancellationToken cancellationToken)
    {
        if (FailFeed) throw new IOException("feed unavailable");
        ulong id = (ulong)(10000 + ++FeedSends);
        Feed[id] = content;
        return Task.FromResult(id);
    }
    public Task<bool> EditFeedAsync(ulong messageId, string marker, string content, CancellationToken cancellationToken)
    {
        if (FailFeed) throw new IOException("feed unavailable");
        if (!Feed.ContainsKey(messageId)) return Task.FromResult(false);
        Feed[messageId] = content;
        return Task.FromResult(true);
    }
    private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
}
