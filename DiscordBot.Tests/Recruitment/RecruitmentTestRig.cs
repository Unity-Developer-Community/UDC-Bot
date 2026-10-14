using DiscordBot.Services.Recruitment;
using DiscordBot.Settings;

namespace DiscordBot.Tests.Recruitment;

internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan duration) => Now += duration;
}

internal sealed class RecruitmentRig : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "udc-recruitment-tests-" + Guid.NewGuid().ToString("N"));
    public HashSet<string> PendingThreadWork { get; } = new();
    public ManualClock Clock { get; } = new();
    public BotSettings Settings { get; }
    public FakeRecruitmentDiscord Discord { get; } = new();
    public RecruitmentEngine Engine { get; private set; }
    public RecruitmentState State => Engine.State!;
    public RecruitmentRig()
    {
        Settings = new BotSettings { GuildId = 1, ServerRootPath = Root, AssetsRootPath = Path.Combine(AppContext.BaseDirectory, "Assets"),
            Recruitment = new RecruitmentSettings { Enabled = true, FeedChannelId = 99,
                Forums = new RecruitmentForums { PaidRecruitingId = 11, PaidForHireId = 12, HobbyRecruitingId = 13, HobbyForHireId = 14 } } };
        foreach (var id in Settings.Recruitment.Forums.Mapping().Values)
            Discord.Forums[id.ToString()] = new ForumSnapshot(id.ToString(), "", false, Array.Empty<LiveTag>());
        Engine = new RecruitmentEngine(Settings, Discord, Clock, PendingThreadWork.Contains);
    }
    public Task Start() => Engine.StartAsync(default);
    public Task Sweep() => Engine.SweepAsync(default);
    public async Task Restart()
    { Engine.Dispose(); Engine = new RecruitmentEngine(Settings, Discord, Clock, PendingThreadWork.Contains); await Start(); }
    public async Task<Listing> Post(string id = "100", string owner = "200", string forum = "11", bool live = true, DateTimeOffset? created = null)
    {
        var thread = new ThreadSnapshot(id, forum, owner, "Example listing", created ?? Clock.Now, false, false, false, false,
            Array.Empty<string>(), "Budget AUD 500 for a short Unity project", Clock.Now.AddYears(-2), Clock.Now.AddMonths(-3));
        Discord.Threads[id] = thread;
        await Engine.ObserveAsync(thread, live, default);
        await Sweep();
        return State.Listings[id];
    }
    public Task<string> Ack(string id = "100", string owner = "200", string? code = null) =>
        Engine.AcknowledgeAsync("1", id, owner, code ?? State.Listings[id].ValidCodes.Last(), default);
    public Task<string> Staff(string operation, string target = "100", string reason = "Test recovery", string actor = "900") =>
        Engine.StaffAsync("1", actor, operation, target, reason, default);
    public void Dispose() { Engine.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}

internal sealed class FakeRecruitmentDiscord : IRecruitmentDiscord
{
    public Dictionary<string, ForumSnapshot> Forums { get; } = new();
    public Dictionary<string, ThreadSnapshot> Threads { get; } = new();
    public Dictionary<string, (string Id, RecruitmentMessage Message)> Messages { get; } = new();
    public List<(string Thread, string Kind)> Actions { get; } = new();
    public HashSet<string> UnknownThreads { get; } = new();
    public HashSet<string> Replies { get; } = new();
    public bool InventoryComplete { get; set; } = true;
    public bool FailStaff { get; set; }
    public bool LoseNextSendResponse { get; set; }
    public bool LoseNextCloseResponse { get; set; }
    public bool LoseNextDeleteResponse { get; set; }
    public bool FailPermissions { get; set; }
    public string? FailForum { get; set; }
    public bool? FailForumRequireTag { get; set; }
    public bool LoseNextForumResponse { get; set; }
    public List<(string Id, bool Tags, bool RequireTag)> ForumPublications { get; } = new();
    public Action? BeforeDelete { get; set; }
    public int Sends { get; private set; }
    public int ForumWrites { get; private set; }
    private int _nextId = 1000;
    public Task ValidateAccessAsync(CancellationToken ct) => FailPermissions ? Task.FromException(new InvalidOperationException("Missing permissions")) : Task.CompletedTask;
    public Task<bool> IsModeratorAsync(string guildId, string userId, CancellationToken ct) => Task.FromResult(guildId == "1" && userId == "900");
    public Task<ForumSnapshot> GetForumAsync(string id, CancellationToken ct) => Task.FromResult(Forums[id]);
    public Task<ForumSnapshot> PublishForumAsync(ForumSnapshot expected, string? topic, IReadOnlyList<LiveTag>? tags, bool requireTag, CancellationToken ct)
    {
        var id = expected.Id;
        if (FailForum == id && (FailForumRequireTag is null || FailForumRequireTag == requireTag)) throw new InvalidOperationException("Forum write failed");
        ForumWrites++;
        ForumPublications.Add((id, tags is not null, requireTag));
        Forums[id] = expected with { Topic = topic ?? expected.Topic, RequireTag = requireTag,
            Flags = requireTag ? expected.Flags | Discord.ChannelFlags.RequireTag : expected.Flags & ~Discord.ChannelFlags.RequireTag,
            Tags = tags?.Select(t => t with { Id = t.Id == "" ? (++_nextId).ToString() : t.Id }).ToArray() ?? expected.Tags };
        if (LoseNextForumResponse) { LoseNextForumResponse = false; throw new TimeoutException("Forum response lost"); }
        return Task.FromResult(Forums[id]);
    }
    public Task<ThreadInventory> InventoryAsync(DateTimeOffset floor, IReadOnlySet<string> knownIds, IReadOnlyDictionary<string, ArchiveCursor> cursors, CancellationToken ct) => Task.FromResult(new ThreadInventory(Threads.Values.Where(t => t.CreatedAt >= floor).ToArray(), InventoryComplete));
    public Task<ThreadSnapshot?> GetThreadAsync(string id, CancellationToken ct) => UnknownThreads.Contains(id)
        ? Task.FromException<ThreadSnapshot?>(new InvalidOperationException("Forbidden")) : Task.FromResult(Threads.GetValueOrDefault(id));
    public Task<bool?> HasReplyAsync(ThreadSnapshot thread, CancellationToken ct) => Task.FromResult<bool?>(Replies.Contains(thread.Id));
    public Task<string> GetActivityAsync(string userId, CancellationToken ct) => Task.FromResult("unknown");
    public Task<Delivery> UpsertAsync(string channelId, string? messageId, bool recovering, DateTimeOffset since, RecruitmentMessage message, Action beforeWrite, CancellationToken ct)
    {
        if (FailStaff && channelId == "99") throw new InvalidOperationException("Feed unavailable");
        var key = channelId + ":" + message.Marker;
        if (Messages.TryGetValue(key, out var existing))
        {
            if (existing.Message.Content != message.Content) beforeWrite();
            Messages[key] = (existing.Id, message); return Task.FromResult(new Delivery(existing.Id, false));
        }
        beforeWrite();
        var id = (++_nextId).ToString(); Messages[key] = (id, message); Sends++;
        if (LoseNextSendResponse) { LoseNextSendResponse = false; throw new InvalidOperationException("Ambiguous send"); }
        return Task.FromResult(new Delivery(id, true));
    }
    public Task CloseAsync(ThreadSnapshot thread, string? closedTagId, bool protectPinned, CancellationToken ct)
    {
        Threads[thread.Id] = thread with { Archived = true, Locked = true }; Actions.Add((thread.Id, "close"));
        if (LoseNextCloseResponse) { LoseNextCloseResponse = false; throw new InvalidOperationException("Ambiguous close"); }
        return Task.CompletedTask;
    }
    public Task ReopenAsync(ThreadSnapshot thread, string? closedTagId, CancellationToken ct)
    { Threads[thread.Id] = thread with { Archived = false, Locked = false }; Actions.Add((thread.Id, "reopen")); return Task.CompletedTask; }
    public Task DeleteAsync(ThreadSnapshot thread, string reason, bool protectPinned, CancellationToken ct)
    {
        var id = thread.Id;
        BeforeDelete?.Invoke(); Threads.Remove(id); Actions.Add((id, "delete"));
        if (LoseNextDeleteResponse) { LoseNextDeleteResponse = false; throw new InvalidOperationException("Ambiguous delete"); }
        return Task.CompletedTask;
    }
    public Task DeleteAdvisoryAsync(string id, string messageId, CancellationToken ct)
    { foreach (var key in Messages.Where(x => x.Value.Id == messageId).Select(x => x.Key).ToArray()) Messages.Remove(key); return Task.CompletedTask; }
}
