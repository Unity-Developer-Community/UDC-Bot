using System.Threading.Channels;
using DiscordBot.Components;
using DiscordBot.Services.Recruitment;
using DiscordBot.Settings.Options;
using DiscordBot.Settings.Validation;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

public sealed class RecruitService(
    RecruitmentStateStore store, RecruitmentObservationCoordinator coordinator, IRecruitmentObserver discord,
    IOptions<RecruitmentOptions> options, TimeProvider time) : IManagedBotService, IComponentHealthContributor
{
    private readonly SemaphoreSlim _lifetime = new(1, 1);
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
            if (!options.Value.Enabled || options.Value.Mode != RecruitmentMode.Observe)
                throw new InvalidOperationException("This build supports enabled Recruitment:Mode Observe only. Advisory and Enforce require later uplift chunks.");
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
            await store.ReleaseAsync();
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
                var dropped = Interlocked.Exchange(ref _dropped, 0);
                if (dropped != 0) await coordinator.RecordGapAsync(token, dropped);
                for (var count = 0; count < 64 && _queue!.Reader.TryRead(out var item); count++)
                {
                    try { await coordinator.HandleAsync(item, token); }
                    catch (Exception) when (store.IsHealthy && !token.IsCancellationRequested)
                    { await coordinator.RecordGapAsync(token); }
                }
                await coordinator.TickAsync(token);
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

    public Task<ComponentHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken) => Task.FromResult(new ComponentHealthSnapshot(
        _fault is not null ? ComponentRuntimeState.Faulted : !IsRunning ? ComponentRuntimeState.Stopped :
        coordinator.HasGaps || Interlocked.Read(ref _dropped) > 0 ? ComponentRuntimeState.Degraded : ComponentRuntimeState.Running,
        _fault ?? (IsRunning ? coordinator.Summary : "Stopped; no recruitment subscriptions or actions."), time.GetUtcNow()));
}
