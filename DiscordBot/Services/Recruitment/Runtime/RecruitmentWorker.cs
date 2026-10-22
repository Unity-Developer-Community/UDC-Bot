using DiscordBot.Services.Recruitment.Application;
using DiscordBot.Utils;
using System.Threading.Channels;
using System.Collections.Concurrent;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Recruitment.Runtime;

/// <summary>Serializes durable work; gateway callbacks only capture facts and wake the next-due scheduler.</summary>
internal sealed class RecruitmentWorker : IRecruitmentGatewaySink
{
    private readonly BotSettings _settings;
    private readonly RecruitmentEngine _engine;
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

    public event Action? Stopped;
    public void InitializeGateway(bool ready) => Volatile.Write(ref _gatewayReady, ready);
    public void SetGatewayReady(bool ready)
    {
        Volatile.Write(ref _gatewayReady, ready);
        if (!ready) EvidenceGap();
    }
    public bool TryGetOwnerThreads(string owner, out string[] threads) =>
        Volatile.Read(ref _listingOwners).TryGetValue(owner, out threads!);

    public RecruitmentWorker(BotSettings settings, IRecruitmentDiscord discord, TimeProvider time)
    {
        _settings = settings; _time = time;
        _health = RecruitmentHealth.Starting(time.GetUtcNow());
        _engine = new RecruitmentEngine(settings, discord, time,
            id => _pendingThreadWork.GetValueOrDefault(id) > 0, () => Interlocked.Read(ref _evidenceGeneration), () => Volatile.Read(ref _gatewayReady),
            () => _pendingThreadWork.Keys.Any(id => !Volatile.Read(ref _knownThreads).Contains(id)));
        _engine.Changed += PublishHealth;
    }

    public async Task StartAsync()
    {
        if (_worker is not null) return;
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
                _stop.Cancel(); _queue.Writer.TryComplete(); _timer?.Dispose(); Stopped?.Invoke();
                while (_queue.Reader.TryRead(out var work)) work.Cancel();
                _engine.Dispose();
                Volatile.Write(ref _health, Health with { Status = "Stopped", Kind = RecruitmentHealthKind.Stopped, Work = "Stopped", ObservedAt = _time.GetUtcNow() });
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
            if (next.RoutinePendingIdentity || before.RoutinePendingIdentity && next.Kind == RecruitmentHealthKind.Operating)
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
        Volatile.Write(ref _health, Health with { InventoryComplete = false, Kind = RecruitmentHealthKind.RecoveryNeeded, Status = "Gateway evidence incomplete; recovery needed" });
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

    public void NotifyThread(string id) => Enqueue("thread:" + id, ct => _engine.ThreadChangedAsync(id, ct), thread: id);
    public void NotifyCreation(ThreadCreation thread) => Enqueue("created:" + thread.Id, ct => _engine.ThreadCreatedAsync(thread, ct), thread: thread.Id);
    public void NotifyFactsChanged(string id) => Enqueue("facts:" + id, ct => _engine.FactsChangedAsync(id, ct), thread: id);

    public void NotifyThreadDeletion(string id)
    {
        // Discord's delete event proves absence even when setup or a listing write is held.
        if (Volatile.Read(ref _knownThreads).Contains(id))
            Enqueue("deleted:" + id, ct => _engine.ThreadDeletedAsync(id, ct), thread: id);
    }
    public void NotifyMessageDeletion(string channel, IReadOnlySet<string> ids)
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

    public void ScheduleRecovery()
    {
        EvidenceGap();
        Enqueue("recovery", async ct => { _engine.InvalidateAccess(); await _engine.SweepAsync(ct, retryHeld: false); }, WorkKind.Recovery);
    }
    // Modal opening reads only this detached snapshot. Submission reauthorizes on the worker.
    public bool CanOpenAcknowledgement(string guild, string thread, string actor) =>
        _settings.Recruitment.Enabled && !_stop.IsCancellationRequested && guild == _settings.GuildId.ToString() &&
        Volatile.Read(ref _listingOwners).TryGetValue(actor, out var threads) && threads.Contains(thread);

    public string QueueRecovery(string guild, string actor, StaffCommand operation, string target, string reason)
    {
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

    public async Task StopAsync(CancellationToken ct)
    {
        _stop.Cancel(); _timer?.Dispose(); _queue.Writer.TryComplete();
        if (_worker is not null) await _worker.WaitAsync(ct);
    }
}
