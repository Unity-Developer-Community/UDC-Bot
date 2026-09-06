using System.Threading.Channels;
using DiscordBot.Components;
using DiscordBot.Services.Recruitment.Actions;
using DiscordBot.Services.Recruitment.Observation;
using DiscordBot.Services.Recruitment.Publishing;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Settings.Options;
using DiscordBot.Settings.Validation;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment;

public sealed class RecruitmentService(
    StateStore store, ObservationCoordinator coordinator, IForumObserver discord,
    IOptions<RecruitmentOptions> options, TimeProvider time, PublicCoordinator? publicCoordinator = null,
    EnforcementCoordinator? enforcement = null, HistoryRetention? retention = null) : IManagedBotService, IComponentHealthContributor
{
    private readonly SemaphoreSlim _lifetime = new(1, 1);
    private readonly SemaphoreSlim _work = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private IDisposable? _subscription;
    private Channel<ObservationEvent>? _queue;
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
            if (!options.Value.Enabled)
                throw new InvalidOperationException("Recruitment is disabled in settings.");
            if (options.Value.Mode != RecruitmentMode.Observe && publicCoordinator is null)
                throw new InvalidOperationException("Advisory services are unavailable.");
            if (options.Value.Mode == RecruitmentMode.Enforce && enforcement is null)
                throw new InvalidOperationException("Enforcement services are unavailable.");
            var errors = RecruitmentOptionsValidator.ValidateValues(options.Value);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            _fault = null;
            _dropped = 0;
            _cancellation?.Dispose();
            // Startup's token is not the lifetime token. Stop owns cancellation after successful start.
            _cancellation = new();
            _queue = Channel.CreateBounded<ObservationEvent>(new BoundedChannelOptions(256)
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
                if (options.Value.Mode != RecruitmentMode.Observe) await publicCoordinator!.InitializeAsync(cancellationToken);
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
                if (item.Kind == EventKind.Gap && options.Value.Mode != RecruitmentMode.Observe)
                    await publicCoordinator!.RecordGapAsync(token);
            }
            catch (Exception) when (store.IsHealthy && !token.IsCancellationRequested)
            { await RecordGapAsync(token); }
        }
        await coordinator.TickAsync(token);
        if (options.Value.Mode != RecruitmentMode.Observe) await publicCoordinator!.TickAsync(token);
        if (options.Value.Mode == RecruitmentMode.Enforce) await enforcement!.TickAsync(token);
        if (retention is not null) await retention.TickAsync(token);
    }

    private async Task RecordGapAsync(CancellationToken token, long dropped = 0)
    {
        await coordinator.RecordGapAsync(token, dropped);
        if (options.Value.Mode != RecruitmentMode.Observe) await publicCoordinator!.RecordGapAsync(token);
    }

    public bool IsPublicRunning => IsRunning && options.Value.Mode != RecruitmentMode.Observe &&
        _cancellation is { IsCancellationRequested: false };

    public Task<T> ExecutePublicAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        if (!IsPublicRunning) throw new InvalidOperationException("Public recruitment is stopped or unavailable.");
        return ExecuteManagedAsync(token =>
        {
            if (!IsPublicRunning) throw new InvalidOperationException("Public recruitment is stopped or unavailable.");
            return action(token);
        }, cancellationToken);
    }

    public async Task<T> ExecuteManagedAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        // Commands share the worker's gate and cancellation. Stop drains both before releasing state ownership.
        var lifetime = _cancellation;
        if (!IsRunning || lifetime is null || lifetime.IsCancellationRequested) throw new InvalidOperationException("Recruitment is stopped or unavailable.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        CancellationToken token = linked.Token;
        await _work.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!IsRunning) throw new InvalidOperationException("Recruitment is stopped or unavailable.");
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
            if (options.Value.Mode != RecruitmentMode.Observe)
            {
                gaps |= publicCoordinator!.HasGaps;
                summary += " " + publicCoordinator.Summary;
            }
            if (options.Value.Mode == RecruitmentMode.Enforce)
            {
                gaps |= enforcement!.HasGaps;
                summary += " " + enforcement.Summary;
            }
            state = gaps ? ComponentRuntimeState.Degraded : ComponentRuntimeState.Running;
        }
        return Task.FromResult(new ComponentHealthSnapshot(state, summary, time.GetUtcNow()));
    }
}
