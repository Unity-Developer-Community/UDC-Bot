using DiscordBot.Services.Recruitment;
using DiscordBot.Settings;

namespace DiscordBot.Tests.Recruitment;

internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
    private readonly List<ManualTimer> _timers = new();
    public override DateTimeOffset GetUtcNow() => Now;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_timers) _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }
    public void Advance(TimeSpan duration)
    {
        Now += duration;
        ManualTimer[] timers;
        lock (_timers) timers = _timers.ToArray();
        foreach (var timer in timers) timer.Fire();
    }
    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _due;
        private TimeSpan _period;
        private bool _disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (this)
            {
                if (_disposed) return false;
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.Now + dueTime;
                _period = period;
                return true;
            }
        }
        public void Fire()
        {
            lock (this)
            {
                if (_disposed || !_due.HasValue || _due > clock.Now) return;
                _due = _period == Timeout.InfiniteTimeSpan ? null : clock.Now + _period;
            }
            callback(state);
        }
        public void Dispose() { lock (this) { _disposed = true; _due = null; } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
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
        Engine.AcknowledgeAsync("1", id, owner, code ?? State.Listings[id].ValidCodes.LastOrDefault() ?? State.RotationCode, default);
    public Task<string> Staff(string operation, string target = "100", string reason = "Test recovery", string actor = "900") =>
        Engine.StaffAsync("1", actor, operation, operation == "repair" && target == "100" ? "" : target, reason, default);
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
    public int AccessReads { get; private set; }
    public int ForumReads { get; private set; }
    public int Inventories { get; private set; }
    public int ReplyReads { get; private set; }
    public int Upserts { get; private set; }
    public int ThreadReads { get; private set; }
    public Func<Task>? BeforeAccess { get; set; }
    public bool FeedEmbeds { get; set; } = true;
    public bool FailFeed { get; set; }
    public bool FailOverview { get; set; }
    public bool FailCleanup { get; set; }
    public List<string> DeletedMessages { get; } = new();
    public bool InventoryComplete { get; set; } = true;
    public bool FailStaff { get; set; }
    public bool LoseNextSendResponse { get; set; }
    public bool LoseNextCloseResponse { get; set; }
    public bool LoseNextDeleteResponse { get; set; }
    public bool FailPermissions { get; set; }
    public string? FailForum { get; set; }
    public bool? FailForumRequireTag { get; set; }
    public bool LoseNextForumResponse { get; set; }
    public bool LoseNextReferenceResponse { get; set; }
    public bool FailReferencePin { get; set; }
    public int ReferenceCreates { get; private set; }
    public int ReferenceChecks { get; private set; }
    public List<(string Id, bool Tags, bool RequireTag)> ForumPublications { get; } = new();
    public Action? BeforeDelete { get; set; }
    public int Sends { get; private set; }
    public int ForumWrites { get; private set; }
    private int _nextId = 1000;
    public async Task ValidateAccessAsync(CancellationToken ct)
    {
        AccessReads++;
        if (BeforeAccess is not null) await BeforeAccess().WaitAsync(ct);
        if (FailPermissions) throw new InvalidOperationException("Missing permissions");
    }
    public Task<bool> ValidateFeedAsync(CancellationToken ct) => FailFeed ? Task.FromException<bool>(new InvalidOperationException("Feed privacy invalid")) : Task.FromResult(FeedEmbeds);
    public Task<string?> LatestFeedMessageAsync(CancellationToken ct) => Task.FromResult(Messages.Where(m => m.Key.StartsWith("99:")).Select(m => m.Value.Id).OrderByDescending(ulong.Parse).FirstOrDefault());
    public HashSet<string> MissingMembers { get; } = new();
    public Task<RecruitmentAuthor?> GetAuthorAsync(string userId, CancellationToken ct) => Task.FromResult<RecruitmentAuthor?>(
        MissingMembers.Contains(userId) ? null : new RecruitmentAuthor(userId == "999", userId == "900"));
    public Task<bool> IsMemberAsync(string guildId, string userId, CancellationToken ct) => Task.FromResult(guildId == "1" && !MissingMembers.Contains(userId));
    public HashSet<string> Administrators { get; } = new() { "901" };
    public Task<bool> IsAdministratorAsync(string guildId, string userId, CancellationToken ct) => Task.FromResult(guildId == "1" && Administrators.Contains(userId) && !MissingMembers.Contains(userId));
    public Task<bool> IsModeratorAsync(string guildId, string userId, CancellationToken ct) => Task.FromResult(guildId == "1" && userId == "900");
    public bool IsExpectedForumUpdate(ForumSnapshot snapshot) => Forums.GetValueOrDefault(snapshot.Id) == snapshot;
    public Task<ForumSnapshot> GetForumAsync(string id, CancellationToken ct) { ForumReads++; return Task.FromResult(Forums[id]); }
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
    public Task<ThreadInventory> InventoryAsync(DateTimeOffset floor, IReadOnlySet<string> knownIds, IReadOnlyDictionary<string, ArchiveCursor> cursors, CancellationToken ct) { Inventories++; return Task.FromResult(new ThreadInventory(Threads.Values.Where(t => t.CreatedAt >= floor && !knownIds.Contains(t.Id)).ToArray(), InventoryComplete)); }
    public Task<ThreadSnapshot?> GetThreadAsync(string id, CancellationToken ct, bool refresh = false)
    { ThreadReads++; return UnknownThreads.Contains(id) ? Task.FromException<ThreadSnapshot?>(new InvalidOperationException("Forbidden")) : Task.FromResult(Threads.GetValueOrDefault(id)); }
    public void InvalidateThread(string id) { }
    public void InvalidateCache() { }
    public void InvalidateFacts(string id) { }
    public Task<string> CreateReferenceAsync(string forum, string closedTagId, string marker, CancellationToken ct)
    {
        ReferenceCreates++;
        var id = (++_nextId).ToString();
        Threads[id] = new ThreadSnapshot(id, forum, "999", DiscordRecruitmentAdapter.ReferenceTitle, DateTimeOffset.UtcNow,
            true, false, false, false, new[] { closedTagId }, DiscordRecruitmentAdapter.ReferenceIntroduction(marker), null, null);
        if (LoseNextReferenceResponse) { LoseNextReferenceResponse = false; throw new TimeoutException("Reference response lost"); }
        return Task.FromResult(id);
    }
    public Task<bool> SecureReferenceAsync(string forum, string id, bool botOwned, string marker, CancellationToken ct)
    {
        ReferenceChecks++;
        if (FailReferencePin) throw new TimeoutException("Reference pin failed");
        if (UnknownThreads.Contains(id)) throw new InvalidOperationException("Forbidden");
        if (!Threads.TryGetValue(id, out var thread)) return Task.FromResult(false);
        if (thread.ForumId != forum || (botOwned ? thread.OwnerId != "999" || thread.Starter?.Contains(marker) != true : !thread.OwnerIsStaff))
            throw new InvalidOperationException("Reference identity mismatch");
        Threads[id] = thread with { Pinned = true, Locked = true, Archived = false };
        return Task.FromResult(true);
    }
    public Task<bool?> HasReplyAsync(ThreadSnapshot thread, CancellationToken ct) { ReplyReads++; return Task.FromResult<bool?>(Replies.Contains(thread.Id)); }
    public Task<Delivery> UpsertAsync(string channelId, string? messageId, bool recovering, DateTimeOffset since, RecruitmentMessage message, Action beforeWrite, CancellationToken ct)
    {
        Upserts++;
        if (FailOverview && message.Marker.StartsWith("recruitment:overview:")) throw new TimeoutException("Overview unavailable");
        if (FailStaff && channelId == "99") throw new InvalidOperationException("Feed unavailable");
        var key = channelId + ":" + message.Marker;
        if (Messages.TryGetValue(key, out var existing))
        {
            if (existing.Message.RenderKey != message.RenderKey) beforeWrite();
            Messages[key] = (existing.Id, message); return Task.FromResult(new Delivery(existing.Id, false));
        }
        beforeWrite();
        var id = (++_nextId).ToString(); Messages[key] = (id, message); Sends++;
        if (LoseNextSendResponse) { LoseNextSendResponse = false; throw new InvalidOperationException("Ambiguous send"); }
        return Task.FromResult(new Delivery(id, true));
    }
    public HashSet<string> MessagePins { get; } = new();
    public int PinWrites { get; private set; }
    public Exception? PinFailure { get; set; }
    public bool LosePinResponse { get; set; }
    public Action? BeforePinWrite { get; set; }
    public Task<bool> PinAdvisoryAsync(string threadId, string messageId, bool restoreArchive, bool restoreLocked,
        Action<bool, bool> beforeWrite, CancellationToken ct)
    {
        if (!Messages.Values.Any(m => m.Id == messageId)) return Task.FromResult(false);
        if (MessagePins.Contains(messageId)) return Task.FromResult(true);
        if (PinFailure is not null) throw PinFailure;
        BeforePinWrite?.Invoke();
        var thread = Threads[threadId];
        beforeWrite(restoreArchive || thread.Archived, restoreArchive ? restoreLocked : thread.Archived && thread.Locked);
        PinWrites++; MessagePins.Add(messageId);
        if (LosePinResponse) { LosePinResponse = false; throw new TimeoutException("Pin response lost"); }
        return Task.FromResult(true);
    }
    public Task CloseAsync(ThreadSnapshot thread, string? closedTagId, bool protectPinned, CancellationToken ct, Func<bool>? allowWrite = null)
    {
        if (allowWrite?.Invoke() == false) throw new InvalidOperationException("Evidence changed");
        Threads[thread.Id] = thread with { Archived = true, Locked = true }; Actions.Add((thread.Id, "close"));
        if (LoseNextCloseResponse) { LoseNextCloseResponse = false; throw new InvalidOperationException("Ambiguous close"); }
        return Task.CompletedTask;
    }
    public Task ReopenAsync(ThreadSnapshot thread, string? closedTagId, CancellationToken ct)
    { Threads[thread.Id] = thread with { Archived = false, Locked = false }; Actions.Add((thread.Id, "reopen")); return Task.CompletedTask; }
    public Task DeleteAsync(ThreadSnapshot thread, string reason, bool protectPinned, CancellationToken ct, Func<bool>? allowWrite = null)
    {
        var id = thread.Id;
        BeforeDelete?.Invoke();
        if (allowWrite?.Invoke() == false) throw new InvalidOperationException("Evidence changed");
        Threads.Remove(id); Actions.Add((id, "delete"));
        if (LoseNextDeleteResponse) { LoseNextDeleteResponse = false; throw new InvalidOperationException("Ambiguous delete"); }
        return Task.CompletedTask;
    }
    public Task DeleteAdvisoryAsync(string id, string messageId, CancellationToken ct, string? marker = null)
    {
        if (FailCleanup) throw new TimeoutException("Cleanup delayed");
        DeletedMessages.Add(messageId);
        foreach (var key in Messages.Where(x => x.Value.Id == messageId && (marker is null || x.Value.Message.Content.Contains(marker))).Select(x => x.Key).ToArray()) Messages.Remove(key); return Task.CompletedTask; }
}
