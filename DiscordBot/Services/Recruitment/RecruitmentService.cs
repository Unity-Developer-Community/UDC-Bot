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

    public RecruitmentService(DiscordSocketClient client, BotSettings settings, DatabaseService database)
        : this(settings, new DiscordRecruitmentAdapter(client, settings, database), TimeProvider.System) => _client = client;

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
        Volatile.Write(ref _knownThreads, _engine.State?.Listings.Keys.ToHashSet() ?? new HashSet<string>());
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
            LoggingService.LogToConsole("Recruitment: " + next.Status,
                next.InventoryComplete ? ExtendedLogSeverity.Info : ExtendedLogSeverity.Warning);
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
                try { if (latest is not null) await latest(ct); }
                finally { finished(); }
            }, Cancel))) return true;
            Cancel(); EvidenceGap();
            SetTimer(TimeSpan.FromSeconds(1));
            return false;
        }
    }

    internal void NotifyThread(string id) => Enqueue("thread:" + id, ct => _engine.ThreadChangedAsync(id, ct), thread: id);
    internal void NotifyReply(ReplyObservation reply) =>
        Enqueue($"reply:{reply.ThreadId}:{reply.AuthorId}:{reply.IsStaff}", async ct =>
        {
            if (_engine.State?.Listings.ContainsKey(reply.ThreadId) != true) await _engine.ThreadChangedAsync(reply.ThreadId, ct);
            _engine.ObserveReply(reply);
        }, thread: reply.ThreadId);

    private Task ThreadCreated(SocketThreadChannel thread)
    {
        if (IsRecruitment(thread)) NotifyThread(thread.Id.ToString());
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
        // An uncached unrelated deletion costs no REST call. Known identities can be checked on the worker.
        if (!Volatile.Read(ref _knownThreads).Contains(thread.Id.ToString())) return Task.CompletedTask;
        Enqueue("deleted:" + thread.Id, ct => _engine.State?.Listings.ContainsKey(thread.Id.ToString()) == true
            ? _engine.ThreadChangedAsync(thread.Id.ToString(), ct) : Task.CompletedTask);
        return Task.CompletedTask;
    }
    private bool IsRecruitment(SocketThreadChannel thread) => thread.Guild.Id == _settings.GuildId && _settings.Recruitment.Forums.Contains(thread.ParentChannel.Id);
    private Task MessageReceived(SocketMessage message)
    {
        if (message.Channel.Id == _settings.Recruitment.FeedChannelId)
        {
            var overview = message.Author.Id == _client?.CurrentUser.Id && message.Content.Contains("recruitment:overview:");
            if (overview) return Task.CompletedTask;
            Enqueue("feed-activity", _ => { _engine.FeedActivity(message.Id.ToString(), false); return Task.CompletedTask; });
            return Task.CompletedTask;
        }
        if (message.Channel is not SocketThreadChannel thread || !IsRecruitment(thread) || message.Author.IsBot || message.Source == MessageSource.Webhook ||
            message.Author.Id == ((IThreadChannel)thread).OwnerId) return Task.CompletedTask;
        var user = message.Author as SocketGuildUser;
        NotifyReply(new ReplyObservation(thread.Id.ToString(), message.Author.Id.ToString(), false, false,
            user is null ? null : user.GuildPermissions.Administrator || user.Roles.Any(r => r.Id == _settings.Roles.Moderator)));
        return Task.CompletedTask;
    }
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
    private Task MemberUpdated(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        if (after.Guild.Id == _settings.GuildId && after.Id == _client?.CurrentUser.Id &&
            (!before.HasValue || !before.Value.Roles.Select(r => r.Id).SequenceEqual(after.Roles.Select(r => r.Id)))) ScheduleRecovery();
        return Task.CompletedTask;
    }

    public bool CanReadStatus(string guild, string actor) => guild == _settings.GuildId.ToString() &&
        ulong.TryParse(actor, out var id) && _client?.GetGuild(_settings.GuildId)?.GetUser(id) is { } user &&
        (user.GuildPermissions.Administrator || user.Roles.Any(r => r.Id == _settings.Roles.Moderator));

    public string QueueRecovery(string guild, string actor, string operation, string target, string reason)
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
                _job = id + ": " + RecruitmentPresentation.Safe(result, 300);
            }
            catch (Exception ex)
            {
                _job = id + ": held — " + RecruitmentPresentation.Safe(ex.Message, 250);
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
            try { completion.TrySetResult(await command(_engine, ct)); }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { finished(); }
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
            _client.MessageReceived += MessageReceived; _client.MessageDeleted += MessageDeleted; _client.MessagesBulkDeleted += MessagesDeleted;
            _client.Disconnected += Disconnected; _client.Ready += Ready; _client.GuildAvailable += GuildAvailable; _client.GuildUnavailable += GuildUnavailable;
            _client.ChannelUpdated += ChannelUpdated; _client.ChannelDestroyed += ChannelDestroyed;
            _client.RoleUpdated += RoleUpdated; _client.RoleDeleted += RoleDeleted; _client.GuildMemberUpdated += MemberUpdated;
        }
        else
        {
            _client.ThreadCreated -= ThreadCreated; _client.ThreadUpdated -= ThreadUpdated; _client.ThreadDeleted -= ThreadDeleted;
            _client.MessageReceived -= MessageReceived; _client.MessageDeleted -= MessageDeleted; _client.MessagesBulkDeleted -= MessagesDeleted;
            _client.Disconnected -= Disconnected; _client.Ready -= Ready; _client.GuildAvailable -= GuildAvailable; _client.GuildUnavailable -= GuildUnavailable;
            _client.ChannelUpdated -= ChannelUpdated; _client.ChannelDestroyed -= ChannelDestroyed;
            _client.RoleUpdated -= RoleUpdated; _client.RoleDeleted -= RoleDeleted; _client.GuildMemberUpdated -= MemberUpdated;
        }
    }
    public async Task StopAsync(CancellationToken ct)
    {
        Subscribe(false); _stop.Cancel(); _timer?.Dispose(); _queue.Writer.TryComplete();
        if (_worker is not null) await _worker.WaitAsync(ct);
    }
}
