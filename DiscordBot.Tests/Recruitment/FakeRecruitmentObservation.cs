using DiscordBot.Services.Recruitment.Observation;
using DiscordBot.Services.Recruitment.State;

namespace DiscordBot.Tests.Recruitment;

internal sealed class FakeRecruitmentObservation(StateStore store, FakePublisher publisher) : IForumObserver
{
    public bool FailFeed;
    public int FeedSends;
    public Dictionary<ulong, string> Feed { get; } = [];
    public Dictionary<ulong, DateTimeOffset> CreatedTimes { get; } = [];
    public MessagePage Replies { get; set; } = new([], true);
    public Action<ObservationEvent>? Receive;
    public IDisposable Subscribe(Action<ObservationEvent> receive)
    {
        Receive = receive;
        return new Subscription(() => Receive = null);
    }
    public Task ValidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public async Task<IReadOnlyList<ThreadSnapshot>> GetActiveAsync(ulong forumId, CancellationToken cancellationToken)
    {
        List<ThreadSnapshot> result = [];
        foreach (var post in publisher.Posts.Values.Where(post => post.ParentId == forumId && !post.Archived))
            result.Add((await GetThreadAsync(post.Id, cancellationToken))!);
        return result;
    }
    public async Task<ArchivePage> GetArchivedAsync(ulong forumId, DateTimeOffset? before, CancellationToken cancellationToken)
    {
        List<ThreadSnapshot> result = [];
        foreach (var post in publisher.Posts.Values.Where(post => post.ParentId == forumId && post.Archived))
            result.Add((await GetThreadAsync(post.Id, cancellationToken))!);
        return new(result, null, true);
    }
    public async Task<ThreadSnapshot?> GetThreadAsync(ulong threadId, CancellationToken cancellationToken)
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
    public Task<MessageSnapshot?> GetStarterAsync(ulong threadId, CancellationToken cancellationToken) =>
        Task.FromResult<MessageSnapshot?>(new(threadId, new(123, RecruitmentTestData.Now, false, false, false, false), "Budget $40/hour"));
    public Task<MessagePage> GetRepliesAsync(ulong threadId, ulong afterId, CancellationToken cancellationToken) => Task.FromResult(Replies);
    public Task<AuthorFacts> GetAuthorAsync(ulong authorId, CancellationToken cancellationToken) => Task.FromResult(new AuthorFacts(ActivityStatus.Unknown, null));
    public Task<FeedPage> FindFeedAsync(string marker, DateTimeOffset since, ulong? beforeId, CancellationToken cancellationToken)
    {
        if (FailFeed) throw new IOException("feed unavailable");
        ulong found = Feed.FirstOrDefault(entry => entry.Value.Contains(marker, StringComparison.Ordinal)).Key;
        return Task.FromResult(new FeedPage(found == 0 ? null : found, null, true));
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
