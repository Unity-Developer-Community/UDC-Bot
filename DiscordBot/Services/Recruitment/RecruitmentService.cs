using DiscordBot.Services.Recruitment.Application;
using DiscordBot.Utils;
using System.Threading.Channels;
using System.Collections.Concurrent;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Recruitment;

/// <summary>Serializes durable work; gateway callbacks only capture facts and wake the next-due scheduler.</summary>
public sealed class RecruitmentService
{
    private readonly DiscordSocketClient? _client;
    private readonly BotSettings _settings;
    private readonly RecruitmentEngine _engine;
    private readonly IRecruitmentDiscord _discord;
    private bool _gatewayReady = true;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, int> _pendingThreadWork = new();
    private readonly Dictionary<string, Func<CancellationToken, Task>> _coalesced = new();
    private readonly object _enqueueGate = new();
    private IReadOnlySet<string> _knownThreads = new HashSet<string>();
    private IReadOnlyDictionary<string, string[]> _listingOwners = new Dictionary<string, string[]>();
    private IReadOnlyDictionary<string, string> _messageOwners = new Dictionary<string, string>();
    private string? _overviewMessage;
    private readonly Channel<Work> _queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(256)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private ITimer? _timer;
    private int _gap;
    private long _evidenceGeneration;
    private string _work = "Starting";
    private string? _job;
    private RecruitmentHealth _health;
    private enum WorkKind { Event, Deadline, Command, Recovery }
    private sealed record Work(WorkKind Kind, Func<CancellationToken, Task> Run, Action Cancel);
    public RecruitmentHealth Health => Volatile.Read(ref _health);

    public RecruitmentService(DiscordSocketClient client, BotSettings settings)
        : this(settings, new DiscordRecruitmentAdapter(client, settings), TimeProvider.System) => _client = client;

    internal RecruitmentService(BotSettings settings, IRecruitmentDiscord discord, TimeProvider time)
    {
        _settings = settings; _time = time; _discord = discord;
        _health = RecruitmentHealth.Starting(time.GetUtcNow());
        _engine = new RecruitmentEngine(settings, discord, time,
            id => _pendingThreadWork.GetValueOrDefault(id) > 0, () => Interlocked.Read(ref _evidenceGeneration), () => Volatile.Read(ref _gatewayReady),
            () => _pendingThreadWork.Keys.Any(id => !Volatile.Read(ref _knownThreads).Contains(id)));
        _engine.Changed += PublishHealth;
    }

    public async Task StartAsync()
    {
        if (_worker is not null) return;
        _gatewayReady = _client is null || _client.GetGuild(_settings.GuildId)?.IsConnected == true;
        Subscribe(true);
        _timer = _time.CreateTimer(_ => Enqueue("due", ct => _engine.RunDueAsync(ct), WorkKind.Deadline), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _worker = Task.Run(async () =>
        {
            try
            {
                await _engine.StartAsync(_stop.Token);
                _work = "Idle";
                await _engine.RefreshOverviewAsync(_stop.Token);
                PublishHealth(); ArmTimer(); started.TrySetResult();
                await foreach (var work in _queue.Reader.ReadAllAsync(_stop.Token))
                {
                    _work = work.Kind.ToString(); PublishHealth();
                    try
                    {
                        if (Interlocked.Exchange(ref _gap, 0) != 0) { _engine.MarkEvidenceGap(); _engine.RequestRecovery(); }
                        await work.Run(_stop.Token);
                        await _engine.RefreshOverviewAsync(_stop.Token);
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { work.Cancel(); break; }
                    catch (Exception ex)
                    {
                        work.Cancel(); EvidenceGap();
                        LoggingService.LogToConsole($"Recruitment {work.Kind} held ({ex.GetType().Name}); inspect /recruitment status.", ExtendedLogSeverity.Warning);
                    }
                    finally { _work = "Idle"; PublishHealth(); ArmTimer(); }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { started.TrySetCanceled(); }
            catch (Exception ex) { started.TrySetException(ex); }
            finally
            {
                _stop.Cancel(); _queue.Writer.TryComplete(); _timer?.Dispose(); Subscribe(false);
                while (_queue.Reader.TryRead(out var work)) work.Cancel();
                _engine.Dispose();
                Volatile.Write(ref _health, Health with { Status = "Stopped", Work = "Stopped", ObservedAt = _time.GetUtcNow() });
            }
        });
        await started.Task;
    }

    private void PublishHealth()
    {
        var next = _engine.Health(_work) with { Job = _job };
        var before = Health;
        Volatile.Write(ref _health, next);
        Volatile.Write(ref _knownThreads, _engine.State is { } state ? state.Listings.Keys.Concat(state.Forums.Values.Select(f => f.Reference.ThreadId).OfType<string>()).ToHashSet() : new HashSet<string>());
        Volatile.Write(ref _listingOwners, _engine.State?.Listings.Values.Where(p => p.Status == ListingStatus.Open)
            .GroupBy(p => p.OwnerId).ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToArray()) ?? new Dictionary<string, string[]>());
        var owners = new Dictionary<string, string>();
        foreach (var post in _engine.State?.Listings.Values ?? Enumerable.Empty<Listing>())
        {
            if (post.PublicMessageId is not null) owners[post.PublicMessageId] = post.Id;
            if (post.StaffMessageId is not null) owners[post.StaffMessageId] = post.Id;
        }
        Volatile.Write(ref _messageOwners, owners);
        Volatile.Write(ref _overviewMessage, _engine.State?.Overview.MessageId);
        foreach (var forum in next.Forums)
        {
            var previous = before.Forums.FirstOrDefault(f => f.Id == forum.Id);
            if (previous?.Fault == forum.Fault) continue;
            LoggingService.LogToConsole($"Recruitment setup [{forum.Purpose}/{forum.Id}]: " +
                (forum.Fault is null ? "recovered" : forum.Fault + (forum.RetryAt.HasValue ? $"; retry {forum.RetryAt:u}" : "; preview and repair required")),
                forum.Fault is null ? ExtendedLogSeverity.Info : ExtendedLogSeverity.Warning);
        }
        if (before.Status != next.Status)
        {
            if (next.RoutinePendingIdentity || before.RoutinePendingIdentity && next.Status == "Operating")
                LoggingService.DebugLog("Recruitment: " + next.Status, ExtendedLogSeverity.Debug);
            else LoggingService.LogToConsole("Recruitment: " + next.Status,
                next.InventoryComplete ? ExtendedLogSeverity.Info : ExtendedLogSeverity.Warning);
        }
    }

    private void ArmTimer()
    {
        if (!Volatile.Read(ref _gatewayReady)) { SetTimer(Timeout.InfiniteTimeSpan); return; }
        var due = _engine.NextDueAt;
        var delay = due.HasValue ? due.Value - _time.GetUtcNow() : Timeout.InfiniteTimeSpan;
        // A bounded continuation yields to facts/commands already queued, including a timely acknowledgement.
        if (due.HasValue && delay < TimeSpan.FromSeconds(1)) delay = TimeSpan.FromSeconds(1);
        SetTimer(delay);
    }

    private void SetTimer(TimeSpan delay)
    {
        if (_stop.IsCancellationRequested) return;
        try { _timer?.Change(delay, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
    }

    internal void EvidenceGap()
    {
        Interlocked.Increment(ref _evidenceGeneration); // Immediately fences an action already awaiting its final read.
        Interlocked.Exchange(ref _gap, 1);
        Volatile.Write(ref _health, Health with { InventoryComplete = false, Status = "Gateway evidence incomplete; recovery needed" });
    }

    private Action TrackThread(string? thread)
    {
        if (thread is null) return () => { };
        _pendingThreadWork.AddOrUpdate(thread, 1, (_, count) => count + 1);
        var finished = 0;
        return () =>
        {
            if (Interlocked.Exchange(ref finished, 1) != 0) return;
            var remaining = _pendingThreadWork.AddOrUpdate(thread, 0, (_, count) => Math.Max(0, count - 1));
            if (remaining == 0) ((ICollection<KeyValuePair<string, int>>)_pendingThreadWork).Remove(new(thread, 0));
        };
    }

    private bool Enqueue(string key, Func<CancellationToken, Task> run, WorkKind kind = WorkKind.Event, string? thread = null)
    {
        lock (_enqueueGate)
        {
            if (_stop.IsCancellationRequested) return false;
            if (_coalesced.ContainsKey(key)) { _coalesced[key] = run; return true; }
            _coalesced[key] = run;
            var finished = TrackThread(thread);
            void Cancel() { lock (_enqueueGate) _coalesced.Remove(key); finished(); }
            if (_queue.Writer.TryWrite(new Work(kind, async ct =>
            {
                Func<CancellationToken, Task>? latest;
                lock (_enqueueGate) _coalesced.Remove(key, out latest);
                // Only work still waiting in the queue fences automatic actions. This event must not fence itself.
                finished();
                if (latest is not null) await latest(ct);
            }, Cancel))) return true;
            Cancel(); EvidenceGap();
            SetTimer(TimeSpan.FromSeconds(1));
            return false;
        }
    }

    internal void NotifyThread(string id) => Enqueue("thread:" + id, ct => _engine.ThreadChangedAsync(id, ct), thread: id);
    internal void NotifyCreation(ThreadCreation thread) => Enqueue("created:" + thread.Id, ct => _engine.ThreadCreatedAsync(thread, ct), thread: thread.Id);
    internal void NotifyFactsChanged(string id) => Enqueue("facts:" + id, ct => _engine.FactsChangedAsync(id, ct), thread: id);

    private Task MessageUpdated(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        if (channel is SocketThreadChannel thread && IsRecruitment(thread) && after.Id == thread.Id &&
            (!before.HasValue || before.Value.Content != after.Content ||
                !before.Value.Attachments.Select(a => a.Id).SequenceEqual(after.Attachments.Select(a => a.Id)))) NotifyFactsChanged(thread.Id.ToString());
        return Task.CompletedTask;
    }

    private Task ThreadCreated(SocketThreadChannel thread)
    {
        if (IsRecruitment(thread)) NotifyCreation(new ThreadCreation(thread.Id.ToString(), thread.ParentChannel.Id.ToString(),
            ((IThreadChannel)thread).OwnerId.ToString(), thread.Name, thread.CreatedAt));
        return Task.CompletedTask;
    }
    private Task ThreadUpdated(Cacheable<SocketThreadChannel, ulong> before, SocketThreadChannel after)
    {
        if (IsRecruitment(after) && (!before.HasValue || before.Value.Flags != after.Flags || before.Value.IsArchived != after.IsArchived ||
            before.Value.IsLocked != after.IsLocked || !before.Value.AppliedTags.SequenceEqual(after.AppliedTags))) NotifyThread(after.Id.ToString());
        return Task.CompletedTask;
    }
    private Task ThreadDeleted(Cacheable<SocketThreadChannel, ulong> thread)
    {
        NotifyThreadDeletion(thread.Id.ToString());
        return Task.CompletedTask;
    }
    internal void NotifyThreadDeletion(string id)
    {
        // Discord's delete event proves absence even when setup or a listing write is held.
        if (Volatile.Read(ref _knownThreads).Contains(id))
            Enqueue("deleted:" + id, ct => _engine.ThreadDeletedAsync(id, ct), thread: id);
    }
    private bool IsRecruitment(SocketThreadChannel thread) => thread.Guild.Id == _settings.GuildId && _settings.Recruitment.Forums.Contains(thread.ParentChannel.Id);
    private Task MessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel) =>
        MessagesDeleted(new[] { message }, channel);
    private Task MessagesDeleted(IReadOnlyCollection<Cacheable<IMessage, ulong>> messages, Cacheable<IMessageChannel, ulong> channel)
    {
        NotifyMessageDeletion(channel.Id.ToString(), messages.Select(m => m.Id.ToString()).ToHashSet());
        return Task.CompletedTask;
    }
    internal void NotifyMessageDeletion(string channel, IReadOnlySet<string> ids)
    {
        var owners = Volatile.Read(ref _messageOwners);
        foreach (var group in ids.Where(owners.ContainsKey).GroupBy(id => owners[id]))
        {
            var affected = group.ToHashSet();
            Enqueue($"messages:{channel}:{group.Key}", ct => _engine.MessageDeletedAsync(channel, affected, ct), thread: group.Key);
        }
        if (Volatile.Read(ref _overviewMessage) is { } overview && ids.Contains(overview))
            Enqueue("overview-deleted", ct => _engine.MessageDeletedAsync(channel, new HashSet<string> { overview }, ct));
    }

    private Task Disconnected(Exception _) { Volatile.Write(ref _gatewayReady, false); EvidenceGap(); return Task.CompletedTask; }
    private Task Ready()
    {
        if (_client?.GetGuild(_settings.GuildId)?.IsConnected == true) { Volatile.Write(ref _gatewayReady, true); ScheduleRecovery(); }
        return Task.CompletedTask;
    }
    private Task GuildAvailable(SocketGuild guild) { if (guild.Id == _settings.GuildId) { Volatile.Write(ref _gatewayReady, true); ScheduleRecovery(); } return Task.CompletedTask; }
    private Task GuildUnavailable(SocketGuild guild) { if (guild.Id == _settings.GuildId) { Volatile.Write(ref _gatewayReady, false); EvidenceGap(); } return Task.CompletedTask; }
    private void ScheduleRecovery()
    {
        EvidenceGap();
        Enqueue("recovery", async ct => { _engine.InvalidateAccess(); await _engine.SweepAsync(ct, retryHeld: false); }, WorkKind.Recovery);
    }
    private Task ChannelUpdated(SocketChannel before, SocketChannel after)
    {
        if (after is not SocketGuildChannel channel || channel.Guild.Id != _settings.GuildId) return Task.CompletedTask;
        var permissionsChanged = before is not SocketGuildChannel prior || !prior.PermissionOverwrites.SequenceEqual(channel.PermissionOverwrites) ||
            (before as INestedChannel)?.CategoryId != (after as INestedChannel)?.CategoryId;
        if (after is SocketForumChannel forum && _settings.Recruitment.Forums.Contains(forum.Id))
        {
            var snapshot = new ForumSnapshot(forum.Id.ToString(), forum.Topic ?? "", (forum.Flags & ChannelFlags.RequireTag) != 0,
                forum.Tags.Select(t => new LiveTag(t.Id.ToString(), t.Name, t.IsModerated, t.Emoji)).ToArray(), forum.Flags);
            if (!permissionsChanged && _discord.IsExpectedForumUpdate(snapshot)) return Task.CompletedTask;
        }
        else if (!permissionsChanged || after.Id != _settings.Recruitment.FeedChannelId && after is not SocketCategoryChannel) return Task.CompletedTask;
        ScheduleRecovery(); return Task.CompletedTask;
    }
    private Task ChannelDestroyed(SocketChannel channel)
    {
        if (channel is SocketGuildChannel guild && guild.Guild.Id == _settings.GuildId &&
            (channel.Id == _settings.Recruitment.FeedChannelId || _settings.Recruitment.Forums.Contains(channel.Id) || channel is SocketCategoryChannel)) ScheduleRecovery();
        return Task.CompletedTask;
    }
    private Task RoleUpdated(SocketRole before, SocketRole after)
    { if (after.Guild.Id == _settings.GuildId && !before.Permissions.Equals(after.Permissions)) ScheduleRecovery(); return Task.CompletedTask; }
    private Task RoleDeleted(SocketRole role) { if (role.Guild.Id == _settings.GuildId) ScheduleRecovery(); return Task.CompletedTask; }
    private Task UserUpdated(SocketUser before, SocketUser after)
    {
        if ((before.AvatarId != after.AvatarId || before.Username != after.Username || before.GlobalName != after.GlobalName) && Volatile.Read(ref _listingOwners).TryGetValue(after.Id.ToString(), out var threads))
            foreach (var id in threads) NotifyFactsChanged(id);
        return Task.CompletedTask;
    }

    private Task MemberUpdated(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        if (after.Guild.Id != _settings.GuildId || before.HasValue && before.Value.Roles.Select(r => r.Id).SequenceEqual(after.Roles.Select(r => r.Id)) && before.Value.DisplayAvatarId == after.DisplayAvatarId && before.Value.Nickname == after.Nickname && before.Value.GlobalName == after.GlobalName && before.Value.Username == after.Username) return Task.CompletedTask;
        if (after.Id == _client?.CurrentUser.Id) ScheduleRecovery();
        else if (Volatile.Read(ref _listingOwners).TryGetValue(after.Id.ToString(), out var threads))
            foreach (var id in threads) NotifyFactsChanged(id);
        return Task.CompletedTask;
    }

    // Use the immutable ownership snapshot so opening a modal does not wait behind Discord work.
    // Submission still authorizes against the persisted listing on the serialized worker.
    public bool CanOpenAcknowledgement(string guild, string thread, string actor) =>
        _settings.Recruitment.Enabled && !_stop.IsCancellationRequested && guild == _settings.GuildId.ToString() &&
        Volatile.Read(ref _listingOwners).TryGetValue(actor, out var threads) && threads.Contains(thread);

    public bool CanReadStatus(string guild, string actor) => guild == _settings.GuildId.ToString() &&
        ulong.TryParse(actor, out var id) && _client?.GetGuild(_settings.GuildId)?.GetUser(id) is { } user &&
        (user.GuildPermissions.Administrator || user.Roles.Any(r => r.Id == _settings.Roles.Moderator));

    public string QueueRecovery(string guild, string actor, StaffCommand operation, string target, string reason)
    {
        if (!CanReadStatus(guild, actor)) return "Moderator access is required in the configured server.";
        if (string.IsNullOrWhiteSpace(reason)) return "A reason is required for this change.";
        var id = Guid.NewGuid().ToString("N")[..8];
        var queued = Enqueue("job:" + id, async ct =>
        {
            _job = id + " running"; PublishHealth();
            try
            {
                var result = await _engine.StaffAsync(guild, actor, operation, target, reason, ct);
                _job = id + ": " + DiscordDisplayText.Literal(result, 300);
            }
            catch (Exception ex)
            {
                _job = id + ": held — " + DiscordDisplayText.Literal(ex.Message, 250);
                throw;
            }
        }, WorkKind.Recovery);
        if (!queued) return "Recruitment is busy or stopped; the job was not queued.";
        return $"Queued recruitment job `{id}`. Use /recruitment status to check progress.";
    }

    public async Task<T> RunAsync<T>(Func<RecruitmentEngine, CancellationToken, Task<T>> command, string? ownerThread = null)
    {
        if (_worker is null || _stop.IsCancellationRequested) throw new InvalidOperationException("Recruitment is starting or stopped.");
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = TrackThread(ownerThread);
        var work = new Work(WorkKind.Command, async ct =>
        {
            finished();
            try { completion.TrySetResult(await command(_engine, ct)); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }, () => { finished(); completion.TrySetCanceled(); });
        if (!_queue.Writer.TryWrite(work)) { finished(); throw new InvalidOperationException("Recruitment is busy. Please try again shortly."); }
        return await completion.Task;
    }

    private void Subscribe(bool subscribe)
    {
        if (_client is null || !_settings.Recruitment.Enabled) return;
        if (subscribe)
        {
            _client.ThreadCreated += ThreadCreated; _client.ThreadUpdated += ThreadUpdated; _client.ThreadDeleted += ThreadDeleted;
            _client.MessageDeleted += MessageDeleted; _client.MessagesBulkDeleted += MessagesDeleted;
            _client.MessageUpdated += MessageUpdated;
            _client.Disconnected += Disconnected; _client.Ready += Ready; _client.GuildAvailable += GuildAvailable; _client.GuildUnavailable += GuildUnavailable;
            _client.ChannelUpdated += ChannelUpdated; _client.ChannelDestroyed += ChannelDestroyed;
            _client.RoleUpdated += RoleUpdated; _client.RoleDeleted += RoleDeleted; _client.GuildMemberUpdated += MemberUpdated; _client.UserUpdated += UserUpdated;
        }
        else
        {
            _client.ThreadCreated -= ThreadCreated; _client.ThreadUpdated -= ThreadUpdated; _client.ThreadDeleted -= ThreadDeleted;
            _client.MessageDeleted -= MessageDeleted; _client.MessagesBulkDeleted -= MessagesDeleted;
            _client.MessageUpdated -= MessageUpdated;
            _client.Disconnected -= Disconnected; _client.Ready -= Ready; _client.GuildAvailable -= GuildAvailable; _client.GuildUnavailable -= GuildUnavailable;
            _client.ChannelUpdated -= ChannelUpdated; _client.ChannelDestroyed -= ChannelDestroyed;
            _client.RoleUpdated -= RoleUpdated; _client.RoleDeleted -= RoleDeleted; _client.GuildMemberUpdated -= MemberUpdated; _client.UserUpdated -= UserUpdated;
        }
    }
    public async Task StopAsync(CancellationToken ct)
    {
        Subscribe(false); _stop.Cancel(); _timer?.Dispose(); _queue.Writer.TryComplete();
        if (_worker is not null) await _worker.WaitAsync(ct);
    }
}
