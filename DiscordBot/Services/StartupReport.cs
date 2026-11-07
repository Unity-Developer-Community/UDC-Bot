using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Logging;

/// <summary>Where the startup report goes. Separate from <see cref="StartupReport"/> so its logic can be tested without Discord.</summary>
public interface IStartupReportChannel
{
    /// <summary>Posts the card; returns its message ID, or null when it couldn't be posted.</summary>
    Task<ulong?> PostAsync(MessageComponent card);
    Task<bool> EditAsync(ulong messageId, MessageComponent card);
    /// <summary>Sends a plain line, as <see cref="LoggingService.LogToChannel"/> would.</summary>
    Task SendTextAsync(string text);
}

/// <summary>
/// Collects the lines <see cref="LoggingService"/> would post to bot-announcement during startup
/// into one message, posted as "Starting…" on Ready and edited when startup is done: when the Ready
/// handler has resolved its services and every <see cref="BeginStep"/> is disposed, or after
/// <see cref="DefaultCap"/>. An error edits it early (debounced). Lines after that post as usual.
/// Memory is measured when it finishes and again <see cref="DefaultMemoryFollowUp"/> later, which
/// edits the card once more. If the first message can't be posted, the report turns itself off and
/// the lines go to the channel.
/// </summary>
public sealed class StartupReport
{
    public static readonly TimeSpan DefaultCap = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DefaultErrorDebounce = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultMemoryFollowUp = TimeSpan.FromSeconds(60);

    private enum Phase { NotStarted, Collecting, Finished, Disabled }

    private sealed record CapturedLine(StartupReportCard.Entry Entry, string Original);

    private sealed class Step(StartupReport report, string name) : IDisposable
    {
        public string Name { get; } = name;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                report.EndStep(this);
        }
    }

    private sealed class NoStep : IDisposable
    {
        public static readonly NoStep Instance = new();
        public void Dispose() { }
    }

    private readonly IStartupReportChannel _channel;
    private readonly TimeSpan _cap;
    private readonly TimeSpan _errorDebounce;
    private readonly TimeSpan _memoryFollowUp;
    private readonly TimeProvider _time;
    private readonly Func<MemoryStats> _sampleMemory;

    private readonly object _lock = new();
    // Edits run one at a time and render the state as it is when they run, so a late edit can't undo the final one.
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private Phase _phase = Phase.NotStarted;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _readyAt;
    private DateTimeOffset? _finishedAt;
    private bool _timedOut;
    private bool _eagerServicesResolved;
    private bool _errorEditScheduled;
    private bool _finalSent;
    private bool _followUpScheduled;
    private MemoryStats? _memory;
    private MemoryStats? _memoryLater;
    private ulong? _messageId;
    private readonly List<CapturedLine> _lines = [];
    private readonly List<Step> _pending = [];
    private List<string> _pendingAtFinish = [];

    public StartupReport(DiscordSocketClient client, BotSettings settings)
        : this(new DiscordStartupReportChannel(client, settings), DefaultCap, DefaultErrorDebounce, TimeProvider.System,
            DefaultMemoryFollowUp, () => MemoryStats.Capture(client, settings.GuildId))
    {
    }

    internal StartupReport(IStartupReportChannel channel, TimeSpan cap, TimeSpan errorDebounce, TimeProvider time,
        TimeSpan memoryFollowUp, Func<MemoryStats> sampleMemory)
    {
        _channel = channel;
        _cap = cap;
        _errorDebounce = errorDebounce;
        _time = time;
        _memoryFollowUp = memoryFollowUp;
        _sampleMemory = sampleMemory;
    }

    /// <summary>For tests: completes when the final card has been sent (or the report turned off).</summary>
    internal TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>For tests: completes when the second memory sample has been shown.</summary>
    internal TaskCompletionSource FollowUpCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Starts collecting and posts the "Starting…" card. Only the first call does anything.</summary>
    public void Begin(DateTimeOffset processStart, DateTimeOffset gatewayReady)
    {
        lock (_lock)
        {
            if (_phase != Phase.NotStarted)
                return;
            _phase = Phase.Collecting;
            _startedAt = processStart;
            _readyAt = gatewayReady;
        }
        PostStartingAsync().SafeFireAndForget(nameof(StartupReport));
        CapAsync().SafeFireAndForget(nameof(StartupReport));
    }

    /// <summary>Collects a channel line while starting. False when the line should be posted as usual.</summary>
    public bool TryCapture(string message, ExtendedLogSeverity severity)
    {
        var scheduleErrorEdit = false;
        lock (_lock)
        {
            if (_phase != Phase.Collecting)
                return false;
            _lines.Add(new CapturedLine(new StartupReportCard.Entry(_time.GetUtcNow() - _startedAt, severity, message), message));
            if (severity is ExtendedLogSeverity.Error or ExtendedLogSeverity.Critical && !_errorEditScheduled)
                scheduleErrorEdit = _errorEditScheduled = true;
        }
        if (scheduleErrorEdit)
            ErrorEditAsync().SafeFireAndForget(nameof(StartupReport));
        return true;
    }

    /// <summary>
    /// A startup step the report waits for. Call it before starting the work (not inside the task),
    /// and dispose it when the work ends, failed or not. A no-op once the report is finished.
    /// </summary>
    public IDisposable BeginStep(string name)
    {
        lock (_lock)
        {
            if (_phase is Phase.Finished or Phase.Disabled)
                return NoStep.Instance;
            var step = new Step(this, name);
            _pending.Add(step);
            return step;
        }
    }

    /// <summary>The Ready handler has resolved its services: the report finishes once no step is pending.</summary>
    public void MarkEagerServicesResolved()
    {
        lock (_lock)
            _eagerServicesResolved = true;
        TryFinish(timedOut: false);
    }

    private void EndStep(Step step)
    {
        lock (_lock)
            _pending.Remove(step);
        TryFinish(timedOut: false);
    }

    private void TryFinish(bool timedOut)
    {
        lock (_lock)
        {
            if (_phase != Phase.Collecting)
                return;
            if (!timedOut && (!_eagerServicesResolved || _pending.Count > 0))
                return;
            _phase = Phase.Finished;
            _finishedAt = _time.GetUtcNow();
            _timedOut = timedOut;
            _pendingAtFinish = timedOut ? _pending.Select(s => s.Name).ToList() : [];
            _memory = _sampleMemory();
        }
        SendAsync().SafeFireAndForget(nameof(StartupReport));
    }

    private async Task CapAsync()
    {
        await Task.Delay(_cap, _time);
        TryFinish(timedOut: true);
    }

    private async Task ErrorEditAsync()
    {
        await Task.Delay(_errorDebounce, _time);
        lock (_lock)
            _errorEditScheduled = false;
        await SendAsync();
    }

    private async Task PostStartingAsync()
    {
        await _sendGate.WaitAsync();
        try
        {
            var (card, finished) = Render();
            var id = await _channel.PostAsync(card);
            if (id == null)
            {
                await DisableAsync();
                return;
            }
            lock (_lock)
                _messageId = id;
            if (finished)
                MarkFinalSent();
        }
        finally
        {
            // Begin takes the gate first, so a finish or an error during the post edits the card after this.
            _sendGate.Release();
        }
    }

    /// <summary>Shows the current state on the card. After the report finishes, the first edit is the final one and later ones are skipped.</summary>
    private async Task SendAsync()
    {
        await _sendGate.WaitAsync();
        try
        {
            ulong messageId;
            lock (_lock)
            {
                if (_messageId is not { } id || _finalSent || _phase == Phase.Disabled)
                    return;
                messageId = id;
            }

            var (card, finished) = Render();
            if (await _channel.EditAsync(messageId, card))
            {
                if (finished)
                    MarkFinalSent();
                return;
            }
            if (!finished)
                return;
            // The final edit failed (the message was deleted, say): send the report as a new message.
            var newId = await _channel.PostAsync(card);
            lock (_lock)
                _messageId = newId;
            MarkFinalSent();
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>The first card couldn't be posted: post the lines collected so far on their own, and stop collecting.</summary>
    private async Task DisableAsync()
    {
        List<CapturedLine> lines;
        lock (_lock)
        {
            _phase = Phase.Disabled;
            lines = [.. _lines];
            _lines.Clear();
        }
        LoggingService.LogToConsole($"[{nameof(StartupReport)}] Couldn't post the startup report, posting its lines on their own", LogSeverity.Warning);
        foreach (var line in lines)
            await _channel.SendTextAsync(line.Original);
        Completed.TrySetResult();
    }

    private void MarkFinalSent()
    {
        bool scheduleFollowUp;
        lock (_lock)
        {
            _finalSent = true;
            scheduleFollowUp = !_followUpScheduled && _messageId != null;
            _followUpScheduled = true;
        }
        Completed.TrySetResult();
        if (scheduleFollowUp)
            MemoryFollowUpAsync().SafeFireAndForget(nameof(StartupReport));
    }

    /// <summary>Measures memory again a while after startup, to show whether it settles or keeps growing.</summary>
    private async Task MemoryFollowUpAsync()
    {
        await Task.Delay(_memoryFollowUp, _time);
        await _sendGate.WaitAsync();
        try
        {
            ulong messageId;
            lock (_lock)
            {
                _memoryLater = _sampleMemory();
                if (_messageId is not { } id)
                    return;
                messageId = id;
            }
            await _channel.EditAsync(messageId, Render().Card);
        }
        finally
        {
            _sendGate.Release();
            FollowUpCompleted.TrySetResult();
        }
    }

    private (MessageComponent Card, bool Finished) Render()
    {
        StartupReportCard.State state;
        lock (_lock)
        {
            var finished = _phase == Phase.Finished;
            state = new StartupReportCard.State(
                _startedAt, _readyAt, finished ? _finishedAt : null,
                _lines.Select(l => l.Entry).ToList(),
                finished ? _pendingAtFinish : _pending.Select(s => s.Name).ToList(),
                finished && _timedOut,
                finished ? _memory : null,
                _memoryLater,
                finished ? _finishedAt + _memoryFollowUp : null);
        }
        return (StartupReportCard.Build(state), state.FinishedAt != null);
    }
}

/// <summary>Posts the startup report to bot-announcement, resolved on first use.</summary>
internal sealed class DiscordStartupReportChannel(DiscordSocketClient client, BotSettings settings) : IStartupReportChannel
{
    private IMessageChannel? _channel;

    private IMessageChannel? Channel =>
        _channel ??= settings.Channels.BotAnnouncement is { Id: not 0 } info ? client.GetChannel(info.Id) as IMessageChannel : null;

    public async Task<ulong?> PostAsync(MessageComponent card)
    {
        if (Channel is not { } channel)
            return null;
        try
        {
            var message = await channel.SendMessageAsync(components: card, flags: MessageFlags.ComponentsV2, allowedMentions: AllowedMentions.None);
            return message.Id;
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[{nameof(StartupReport)}] Couldn't post the startup report: {e.Message}", LogSeverity.Warning);
            return null;
        }
    }

    public async Task<bool> EditAsync(ulong messageId, MessageComponent card)
    {
        if (Channel is not { } channel)
            return false;
        try
        {
            await channel.ModifyMessageAsync(messageId, m =>
            {
                m.Components = card;
                m.Flags = MessageFlags.ComponentsV2;
                m.AllowedMentions = AllowedMentions.None;
            });
            return true;
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[{nameof(StartupReport)}] Couldn't update the startup report: {e.Message}", LogSeverity.Warning);
            return false;
        }
    }

    public async Task SendTextAsync(string text)
    {
        if (Channel is { } channel)
            await channel.SendMessageAsync(text);
    }
}
