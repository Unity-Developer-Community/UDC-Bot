using System.Threading.Channels;
using DiscordBot.Components;
using DiscordBot.Services.Recruitment;
using DiscordBot.Settings.Options;
using DiscordBot.Settings.Validation;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

public sealed class RecruitService(
    RecruitmentStateStore store, RecruitmentObservationCoordinator coordinator, IRecruitmentObserver discord,
    IOptions<RecruitmentOptions> options, TimeProvider time, RecruitmentAdvisoryCoordinator? advisory = null) : IManagedBotService, IComponentHealthContributor
{
    private readonly SemaphoreSlim _lifetime = new(1, 1);
    private readonly SemaphoreSlim _work = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private IDisposable? _subscription;
    private Channel<RecruitmentObservationEvent>? _queue;
    private long _dropped;
    private string? _fault;
    public string ComponentId => ComponentIds.Recruitment;
    public bool IsRunning => _worker is { IsCompleted: false };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifetime.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning) return;
            if (!options.Value.Enabled || options.Value.Mode == RecruitmentMode.Enforce)
                throw new InvalidOperationException("This build supports enabled Observe or Advisory only; Enforce requires a later chunk.");
            if (options.Value.Mode == RecruitmentMode.Advisory && advisory is null)
                throw new InvalidOperationException("Advisory services are unavailable.");
            var errors = RecruitmentOptionsValidator.ValidateValues(options.Value);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            _fault = null;
            _dropped = 0;
            _cancellation?.Dispose();
            // Startup's token is not the lifetime token. Stop owns cancellation after successful start.
            _cancellation = new();
            _queue = Channel.CreateBounded<RecruitmentObservationEvent>(new BoundedChannelOptions(256)
            { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
            _subscription?.Dispose();
            var queue = _queue;
            _subscription = discord.Subscribe(item =>
            {
                if (!queue.Writer.TryWrite(item)) Interlocked.Increment(ref _dropped);
            });
            try
            {
                await coordinator.InitializeAsync(cancellationToken);
                if (options.Value.Mode == RecruitmentMode.Advisory) await advisory!.InitializeAsync(cancellationToken);
                _worker = RunAsync(_cancellation.Token);
            }
            catch
            {
                _subscription.Dispose();
                _subscription = null;
                _queue.Writer.TryComplete();
                await store.ReleaseAsync();
                throw;
            }
        }
        finally { _lifetime.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifetime.WaitAsync(cancellationToken);
        try
        {
            _subscription?.Dispose();
            _subscription = null;
            _queue?.Writer.TryComplete();
            if (_cancellation is not null) await _cancellation.CancelAsync();
            // Drain owned calls before releasing the writer, even if stop's deadline expires.
            if (_worker is not null) await _worker;
            await _work.WaitAsync(CancellationToken.None);
            try { await store.ReleaseAsync(); }
            finally { _work.Release(); }
            _worker = null;
            _cancellation?.Dispose();
            _cancellation = null;
        }
        finally { _lifetime.Release(); }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                await _work.WaitAsync(token);
                try { await ProcessWorkAsync(token); }
                finally { _work.Release(); }
                await Task.Delay(TimeSpan.FromSeconds(30), time, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) { _fault = $"Observation stopped: {e.GetType().Name}. Check recruitment state and Discord access before restarting."; }
        finally
        {
            _subscription?.Dispose();
            _queue?.Writer.TryComplete();
        }
    }

    private async Task ProcessWorkAsync(CancellationToken token)
    {
        var dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped != 0) await RecordGapAsync(token, dropped);
        for (var count = 0; count < 64 && _queue!.Reader.TryRead(out var item); count++)
        {
            try
            {
                await coordinator.HandleAsync(item, token);
                if (item.Kind == RecruitmentEventKind.Gap && options.Value.Mode == RecruitmentMode.Advisory)
                    await advisory!.RecordGapAsync(token);
            }
            catch (Exception) when (store.IsHealthy && !token.IsCancellationRequested)
            { await RecordGapAsync(token); }
        }
        await coordinator.TickAsync(token);
        if (options.Value.Mode == RecruitmentMode.Advisory) await advisory!.TickAsync(token);
    }

    private async Task RecordGapAsync(CancellationToken token, long dropped = 0)
    {
        await coordinator.RecordGapAsync(token, dropped);
        if (options.Value.Mode == RecruitmentMode.Advisory) await advisory!.RecordGapAsync(token);
    }

    public bool IsAdvisoryRunning => IsRunning && options.Value.Mode == RecruitmentMode.Advisory &&
        _cancellation is { IsCancellationRequested: false };

    public async Task<T> ExecuteAdvisoryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        // Commands share the worker's gate and cancellation. Stop drains both before releasing state ownership.
        var lifetime = _cancellation;
        if (!IsAdvisoryRunning || lifetime is null) throw new InvalidOperationException("Advisory is stopped or unavailable.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        CancellationToken token = linked.Token;
        await _work.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!IsAdvisoryRunning) throw new InvalidOperationException("Advisory is stopped or unavailable.");
            return await action(token);
        }
        finally { _work.Release(); }
    }

    public Task<ComponentHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken)
    {
        var state = ComponentRuntimeState.Stopped;
        string summary = "Stopped; no recruitment subscriptions or actions.";
        if (_fault is not null)
        {
            state = ComponentRuntimeState.Faulted;
            summary = _fault;
        }
        else if (IsRunning)
        {
            bool gaps = coordinator.HasGaps || Interlocked.Read(ref _dropped) > 0;
            summary = coordinator.Summary;
            if (options.Value.Mode == RecruitmentMode.Advisory)
            {
                gaps |= advisory!.HasGaps;
                summary += " " + advisory.Summary;
            }
            state = gaps ? ComponentRuntimeState.Degraded : ComponentRuntimeState.Running;
        }
        return Task.FromResult(new ComponentHealthSnapshot(state, summary, time.GetUtcNow()));
    }
}
