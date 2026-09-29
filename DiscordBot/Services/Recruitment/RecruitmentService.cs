using System.Threading.Channels;
using System.Collections.Concurrent;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Recruitment;

/// <summary>One bounded queue owns all recruitment state and external operations.</summary>
public sealed class RecruitmentService
{
    private readonly DiscordSocketClient _client;
    private readonly BotSettings _settings;
    private readonly DiscordRecruitmentAdapter _discord;
    private readonly RecruitmentEngine _engine;
    private readonly ConcurrentDictionary<string, int> _pendingThreadWork = new();
    private readonly Channel<Work> _queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(256)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private Timer? _timer;
    private DateTimeOffset _connectedSince;
    private int _gap;
    private long _evidenceGeneration;
    private int _sweepQueued;
    private sealed record Work(Func<CancellationToken, Task> Run, Action Cancel);

    public RecruitmentService(DiscordSocketClient client, BotSettings settings, DatabaseService database)
    {
        _client = client; _settings = settings;
        _discord = new DiscordRecruitmentAdapter(client, settings, database);
        _engine = new RecruitmentEngine(settings, _discord, pendingThreadWork: id => _pendingThreadWork.GetValueOrDefault(id) > 0);
    }

    public async Task StartAsync()
    {
        if (_worker is not null) return;
        _connectedSince = DateTimeOffset.UtcNow;
        if (_settings.Recruitment.Enabled)
        {
            _client.ThreadCreated += ThreadCreated;
            _client.ThreadUpdated += ThreadUpdated;
            _client.ThreadDeleted += ThreadDeleted;
            _client.MessageReceived += MessageReceived;
            _client.Disconnected += Disconnected;
            _client.Connected += Connected;
        }
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _worker = Task.Run(async () =>
        {
            try
            {
                await _engine.StartAsync(_stop.Token);
                LoggingService.LogToConsole("Recruitment: " + _engine.Status, ExtendedLogSeverity.Info);
                started.TrySetResult();
                await foreach (var work in _queue.Reader.ReadAllAsync(_stop.Token))
                {
                    try
                    {
                        if (Interlocked.Exchange(ref _gap, 0) != 0) _engine.MarkEvidenceGap();
                        await work.Run(_stop.Token);
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { work.Cancel(); break; }
                    catch (Exception ex)
                    {
                        work.Cancel();
                        EvidenceGap();
                        LoggingService.LogToConsole($"Recruitment operation held ({ex.GetType().Name}). Use /recruitment status or reconcile.", ExtendedLogSeverity.Warning);
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { started.TrySetCanceled(); }
            catch (Exception ex) { started.TrySetException(ex); }
            finally
            {
                while (_queue.Reader.TryRead(out var work)) work.Cancel();
                _engine.Dispose();
            }
        });
        await started.Task;
        if (_settings.Recruitment.Enabled && !_queue.Reader.Completion.IsCompleted && !_stop.IsCancellationRequested)
            _timer = new Timer(_ => ScheduleSweep(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private void EvidenceGap()
    {
        Interlocked.Increment(ref _evidenceGeneration);
        Interlocked.Exchange(ref _gap, 1);
    }
    private Action TrackThread(string? threadId)
    {
        if (threadId is null) return () => { };
        _pendingThreadWork.AddOrUpdate(threadId, 1, (_, count) => count + 1);
        var finished = 0;
        return () =>
        {
            if (Interlocked.Exchange(ref finished, 1) == 0)
            {
                var remaining = _pendingThreadWork.AddOrUpdate(threadId, 0, (_, count) => Math.Max(0, count - 1));
                if (remaining == 0) ((ICollection<KeyValuePair<string, int>>)_pendingThreadWork).Remove(new(threadId, 0));
            }
        };
    }
    private void Event(Func<CancellationToken, Task> run, string? threadId = null)
    {
        var finished = TrackThread(threadId);
        if (!_queue.Writer.TryWrite(new Work(async ct => { try { await run(ct); } finally { finished(); } }, finished)))
        { finished(); EvidenceGap(); }
    }
    private void ScheduleSweep()
    {
        if (Interlocked.Exchange(ref _sweepQueued, 1) != 0) return;
        if (!_queue.Writer.TryWrite(new Work(async ct =>
        {
            try { await _engine.SweepAsync(ct); }
            finally { Interlocked.Exchange(ref _sweepQueued, 0); }
        }, () => Interlocked.Exchange(ref _sweepQueued, 0))))
        { Interlocked.Exchange(ref _sweepQueued, 0); EvidenceGap(); }
    }
    private Task ThreadCreated(SocketThreadChannel thread)
    {
        if (thread.Guild.Id != _settings.GuildId || !_settings.Recruitment.Forums.Contains(thread.ParentChannel.Id)) return Task.CompletedTask;
        var fromCreation = thread.CreatedAt >= _connectedSince;
        var generation = Interlocked.Read(ref _evidenceGeneration);
        Event(async ct =>
        {
            var live = await _discord.GetThreadAsync(thread.Id.ToString(), ct);
            if (live is not null) await _engine.ObserveAsync(live, fromCreation && generation == Interlocked.Read(ref _evidenceGeneration), ct);
        });
        ScheduleSweep();
        return Task.CompletedTask;
    }
    private Task ThreadUpdated(Cacheable<SocketThreadChannel, ulong> before, SocketThreadChannel after)
    {
        if (after.Guild.Id == _settings.GuildId && _settings.Recruitment.Forums.Contains(after.ParentChannel.Id)) ScheduleSweep();
        return Task.CompletedTask;
    }
    private Task ThreadDeleted(Cacheable<SocketThreadChannel, ulong> thread)
    {
        Event(ct => _engine.ReconcileThreadAsync(thread.Id.ToString(), ct));
        ScheduleSweep(); return Task.CompletedTask;
    }
    private Task MessageReceived(SocketMessage message)
    {
        if (message.Channel is not SocketThreadChannel thread || thread.Guild.Id != _settings.GuildId ||
            !_settings.Recruitment.Forums.Contains(thread.ParentChannel.Id)) return Task.CompletedTask;
        var user = message.Author as SocketGuildUser;
        var reply = new ReplyObservation(thread.Id.ToString(), message.Author.Id.ToString(), message.Author.IsBot,
            message.Source == MessageSource.Webhook, user is null ? null : user.GuildPermissions.Administrator || user.Roles.Any(r => r.Id == _settings.Roles.Moderator));
        Event(async ct =>
        {
            if (_engine.State is not null && !_engine.State.Listings.ContainsKey(reply.ThreadId))
            {
                var live = await _discord.GetThreadAsync(reply.ThreadId, ct);
                if (live is not null) await _engine.ObserveAsync(live, false, ct);
            }
            _engine.ObserveReply(reply);
        }, reply.ThreadId);
        return Task.CompletedTask;
    }
    private Task Disconnected(Exception _)
    { EvidenceGap(); return Task.CompletedTask; }
    private Task Connected()
    { _connectedSince = DateTimeOffset.UtcNow; EvidenceGap(); ScheduleSweep(); return Task.CompletedTask; }

    public async Task<T> RunAsync<T>(Func<RecruitmentEngine, CancellationToken, Task<T>> command, string? ownerThread = null)
    {
        if (_worker is null) throw new InvalidOperationException("Recruitment is starting. Try again shortly.");
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = TrackThread(ownerThread);
        var work = new Work(async ct =>
        {
            try { completion.TrySetResult(await command(_engine, ct)); }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { finished(); }
        }, () => { finished(); completion.TrySetCanceled(); });
        if (!_queue.Writer.TryWrite(work))
        { finished(); throw new InvalidOperationException("Recruitment is busy. Please try again shortly."); }
        return await completion.Task;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _timer?.Dispose();
        _client.ThreadCreated -= ThreadCreated;
        _client.ThreadUpdated -= ThreadUpdated;
        _client.ThreadDeleted -= ThreadDeleted;
        _client.MessageReceived -= MessageReceived;
        _client.Disconnected -= Disconnected;
        _client.Connected -= Connected;
        _queue.Writer.TryComplete();
        if (_worker is null) return;
        try { await _worker.WaitAsync(ct); }
        catch (OperationCanceledException) { _stop.Cancel(); await _worker; }
    }
}
