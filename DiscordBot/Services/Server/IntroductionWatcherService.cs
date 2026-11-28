using System.Net;
using Discord.Net;
using Discord.WebSocket;
using DiscordBot.Services.Moderation;
using DiscordBot.Settings;

namespace DiscordBot.Services.Server;

/// <summary>
/// Keeps a short "say hello" prompt as the latest message in the Introductions channel.
/// After a human posts, waits until the channel has been quiet for DelayMinutes, then posts a
/// new prompt and deletes the previous one.
/// </summary>
/// <remarks>
/// The loop must never stall: every step runs under <see cref="StepTimeout"/>, and every few minutes the channel
/// history is read again (<see cref="SyncInterval"/>) so a missed message event or a lost schedule catches up.
/// Admins can see the state and restart the loop with <c>/admin intro</c>.
/// </remarks>
public class IntroductionWatcherService
{
    private const string ServiceName = "IntroductionWatcherService";
    private const int HistoryScanLimit = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    /// <summary>The longest one step (posting a prompt, or reading history) may take before it is abandoned.</summary>
    private static readonly TimeSpan StepTimeout = TimeSpan.FromMinutes(2);
    /// <summary>How often the channel history is read again when nothing is scheduled.</summary>
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LogTimeout = TimeSpan.FromSeconds(30);
    /// <summary>The same warning is sent to the log channel at most this often; repeats go to the console.</summary>
    private static readonly TimeSpan RepeatWarningInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RestartWait = TimeSpan.FromSeconds(10);
    private static readonly Color AccentColour = new(0x12D687);
    // Also how a prompt is recognised in channel history, so changing it orphans any existing prompt.
    public const string PromptHeading = "### 👋 New here? Introduce yourself!";

    private readonly DiscordSocketClient _client;
    private readonly BotSettings _settings;
    private readonly ILoggingService _loggingService;
    private readonly BotDeletionRegistry _botDeletions;
    private readonly CancellationToken _shutdownToken;
    private readonly IntroductionPromptSchedule? _schedule;

    private readonly object _runLock = new();
    private readonly SemaphoreSlim _restartGate = new(1, 1);
    private CancellationTokenSource? _runCts;
    private Task _runTask = Task.CompletedTask;
    private volatile ITextChannel? _channel;
    private bool _announced;

    // Touched by the running loop; a restart waits for the old loop before resetting it.
    private ulong? _promptId;

    private readonly object _stateLock = new();
    private DateTime? _startedUtc;
    private DateTime? _lastTickUtc;
    private DateTime? _lastPostUtc;
    private DateTime? _lastSyncUtc;
    private string? _lastError;
    private DateTime? _lastErrorUtc;
    private string? _lastWarningLogged;
    private DateTime _lastWarningLoggedUtc;

    public IntroductionWatcherService(DiscordSocketClient client, BotSettings settings, ILoggingService loggingService,
        BotDeletionRegistry botDeletions, CancellationTokenSource cts)
    {
        _client = client;
        _settings = settings;
        _loggingService = loggingService;
        _botDeletions = botDeletions;
        _shutdownToken = cts.Token;

        if (!settings.IntroductionWatch.Enabled)
        {
            LoggingService.LogServiceDisabled(ServiceName, nameof(settings.IntroductionWatch.Enabled));
            return;
        }

        _schedule = new IntroductionPromptSchedule(settings.IntroductionWatch.DelayMinutes);
        _client.MessageReceived += EventGuard.Guarded<SocketMessage>(OnMessageReceived, nameof(OnMessageReceived));
        Start();
    }

    /// <summary>What the watcher is doing, for <c>/admin intro status</c>.</summary>
    public sealed record WatcherStatus(
        bool Enabled,
        ulong GuildId,
        bool Running,
        ulong? ChannelId,
        ulong? PromptId,
        DateTime? DueUtc,
        DateTime? StartedUtc,
        DateTime? LastTickUtc,
        DateTime? LastPostUtc,
        DateTime? LastSyncUtc,
        string? LastError,
        DateTime? LastErrorUtc);

    public WatcherStatus Status
    {
        get
        {
            bool running;
            lock (_runLock) running = !_runTask.IsCompleted;
            lock (_stateLock)
                return new WatcherStatus(_schedule != null, _settings.GuildId, running, _channel?.Id, _promptId, _schedule?.DueUtc,
                    _startedUtc, _lastTickUtc, _lastPostUtc, _lastSyncUtc, _lastError, _lastErrorUtc);
        }
    }

    /// <summary>The status as a few lines of Discord markdown, with relative times.</summary>
    public static string DescribeStatus(WatcherStatus status)
    {
        if (!status.Enabled)
            return "**Introductions prompt watcher:** off (`IntroductionWatch.Enabled` is false).";

        static string When(DateTime? utc) => utc is { } t ? $"<t:{new DateTimeOffset(t, TimeSpan.Zero).ToUnixTimeSeconds()}:R>" : "never";
        var lines = new List<string>
        {
            $"**Introductions prompt watcher:** {(status.Running ? "running" : "stopped")}" +
            (status.ChannelId is { } channel ? $" in <#{channel}>" : " (channel not found yet)"),
            $"Prompt: {(status.PromptId is { } prompt && status.ChannelId is { } ch ? $"https://discord.com/channels/{status.GuildId}/{ch}/{prompt}" : "none known")}",
            $"Next prompt: {(status.DueUtc is { } due ? When(due) : "nothing scheduled")}",
            $"Loop started {When(status.StartedUtc)} · last check {When(status.LastTickUtc)} · last history read {When(status.LastSyncUtc)} · last prompt posted {When(status.LastPostUtc)}",
        };
        if (status.LastError != null)
            lines.Add($"Last problem {When(status.LastErrorUtc)}: {status.LastError}");
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Stops the loop (abandoning a stuck step), forgets the current prompt and schedule, and starts again: the
    /// channel is looked up again and its history read, as at startup. Does nothing when the feature is off.
    /// </summary>
    public async Task<WatcherStatus> RestartAsync()
    {
        if (_schedule == null)
            return Status;

        await _restartGate.WaitAsync(_shutdownToken);
        try
        {
            CancellationTokenSource? oldCts;
            Task oldTask;
            lock (_runLock)
            {
                oldCts = _runCts;
                oldTask = _runTask;
            }
            oldCts?.Cancel();
            try
            {
                await oldTask.WaitAsync(RestartWait, _shutdownToken);
            }
            catch (TimeoutException)
            {
                LoggingService.LogToConsole($"[{ServiceName}] The old loop did not stop within {RestartWait.TotalSeconds:0} s; starting a new one anyway.", ExtendedLogSeverity.Warning);
            }
            // A loop that crashed rethrows here, and that is when a restart is needed most. Shutdown stays fatal.
            catch (Exception e) when (!_shutdownToken.IsCancellationRequested)
            {
                RecordError($"Previous loop failed: {e.Message}");
                LoggingService.LogToConsole($"[{ServiceName}] The old loop had failed ({e.Message}); starting a new one.", ExtendedLogSeverity.Warning);
            }

            _promptId = null;
            _schedule.Clear();
            Start();
            return Status;
        }
        finally
        {
            _restartGate.Release();
        }
    }

    private void Start()
    {
        lock (_runLock)
        {
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken);
            var token = _runCts.Token;
            _runTask = Task.Run(() => RunAsync(token), CancellationToken.None);
            _runTask.SafeFireAndForget(ServiceName);
        }
        lock (_stateLock) _startedUtc = DateTime.UtcNow;
    }

    public static MessageComponent BuildPrompt(ChannelInfo? rulesChannel, ChannelInfo? helpChannel) =>
        new ComponentBuilderV2()
            .WithContainer(new ContainerBuilder()
                .WithAccentColor(AccentColour)
                .WithTextDisplay($"{PromptHeading}\n" +
                                 "We'd love to know a little about you.\n" +
                                 "**Some ideas:** a nickname, what you're making or learning, and what brought you here.")
                .WithSeparator(SeparatorSpacingSize.Small, true)
                .WithTextDisplay("**Before you post**\n" +
                                 $"- Keep it to an introduction. Questions go in {ChannelLink(helpChannel, "the help channels")}.\n" +
                                 "- No ads, \"DM me\" requests, or personal contact details.\n" +
                                 "-# A few lines is plenty, and only share what you're comfortable with. " +
                                 $"Full rules: {ChannelLink(rulesChannel, "#rules")}"))
            .Build();

    private static string ChannelLink(ChannelInfo? channel, string fallback) =>
        channel is { Id: not 0 } ? $"<#{channel.Id}>" : fallback;

    private Task OnMessageReceived(SocketMessage message)
    {
        if (_channel is { } channel && IsHumanIntroMessage(message, channel.Id))
            _schedule!.Touch(DateTime.UtcNow);
        return Task.CompletedTask;
    }

    private static bool IsHumanIntroMessage(IMessage message, ulong channelId) =>
        message.Channel.Id == channelId
        && message is IUserMessage
        && message.Type is MessageType.Default or MessageType.Reply
        && !message.Author.IsUserBotOrWebhook();

    private bool IsPrompt(IMessage message) =>
        message.Author.Id == _client.CurrentUser.Id
        && message.Flags is { } flags
        && flags.HasFlag(MessageFlags.ComponentsV2)
        && IsPromptComponents(message.Components);

    /// <summary>True if the components start with a container whose first text is the prompt heading.</summary>
    public static bool IsPromptComponents(IEnumerable<IMessageComponent> components) =>
        components.FirstOrDefault() is ContainerComponent container
        && container.Components.OfType<TextDisplayComponent>().FirstOrDefault() is { } text
        && text.Content.StartsWith(PromptHeading, StringComparison.Ordinal);

    /// <summary>
    /// When a prompt is due according to the channel history, or null when nothing needs one: a human message after
    /// the newest prompt makes one due <paramref name="delay"/> after that message (now at the earliest), and a channel
    /// with no prompt gets one after <paramref name="delay"/>.
    /// </summary>
    public static DateTime? DueFromHistory(DateTime nowUtc, TimeSpan delay, bool hasPrompt, DateTime? latestHumanAfterPromptUtc)
    {
        if (latestHumanAfterPromptUtc is { } human)
        {
            var due = human + delay;
            return due > nowUtc ? due : nowUtc;
        }
        return hasPrompt ? null : nowUtc + delay;
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            var channel = await ResolveChannelAsync(token);
            await RunStepAsync("read the Introductions history", t => SyncFromHistoryAsync(channel, t), retryPost: false, token);
            var nextSync = DateTime.UtcNow + SyncInterval;

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(PollInterval, token);
                var now = DateTime.UtcNow;
                lock (_stateLock) _lastTickUtc = now;

                if (_schedule!.TryTakeDue(now))
                {
                    await RunStepAsync("post the introduction prompt", t => PostPromptAsync(channel, t), retryPost: true, token);
                }
                else if (now >= nextSync && _schedule.DueUtc == null)
                {
                    nextSync = now + SyncInterval;
                    await RunStepAsync("read the Introductions history", t => SyncFromHistoryAsync(channel, t), retryPost: false, token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Restarted, or the bot is shutting down.
        }
    }

    /// <summary>Waits until the Introductions channel is in the cache; it can be missing for a while after a reconnect.</summary>
    private async Task<ITextChannel> ResolveChannelAsync(CancellationToken token)
    {
        var channelId = _settings.Channels.Introduction?.Id ?? 0;
        var warned = false;
        while (true)
        {
            if (_client.GetChannel(channelId) is ITextChannel channel)
            {
                _channel = channel;
                if (!_announced)
                {
                    _announced = true;
                    LoggingService.LogServiceEnabled(ServiceName);
                    _loggingService.LogAction($"[{ServiceName}] Watching <#{channel.Id}>: a new prompt {_settings.IntroductionWatch.DelayMinutes} min after the channel goes quiet.",
                        ExtendedLogSeverity.Info).SafeFireAndForget(ServiceName);
                }
                return channel;
            }

            if (!warned)
            {
                warned = true;
                RecordError($"Introduction channel {channelId} not found");
                await LogWarningAsync($"Introduction channel {channelId} not found; checking again every {PollInterval.TotalSeconds:0} s.");
            }
            await Task.Delay(PollInterval, token);
        }
    }

    /// <summary>
    /// Runs one step under <see cref="StepTimeout"/>. A step that throws or times out is logged; a failed post is
    /// retried unless Discord refused it with a 4xx, which won't fix itself.
    /// </summary>
    private async Task RunStepAsync(string what, Func<CancellationToken, Task> step, bool retryPost, CancellationToken runToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(runToken);
        stepCts.CancelAfter(StepTimeout);
        try
        {
            // WaitAsync returns even if the step ignores its token and never finishes.
            await step(stepCts.Token).WaitAsync(StepTimeout, runToken);
        }
        // Filter on the run token, not the exception type: an HTTP or step timeout throws a cancellation exception,
        // which would otherwise end the loop as if it were being stopped.
        catch (Exception e) when (!runToken.IsCancellationRequested)
        {
            var timedOut = e is TimeoutException or OperationCanceledException;
            var retry = retryPost && (timedOut || e is not HttpException http || (int)http.HttpCode >= 500);
            if (retry)
                _schedule!.Retry(DateTime.UtcNow + RetryDelay);
            var reason = timedOut ? $"timed out after {StepTimeout.TotalMinutes:0} minute(s)" : e.Message;
            var next = !retryPost ? "trying again later" : retry ? $"retrying in {RetryDelay.TotalMinutes:0} minute(s)" : $"not retrying now; the next history read (every {SyncInterval.TotalMinutes:0} min) schedules it again";
            RecordError($"Failed to {what}: {reason}");
            await LogWarningAsync($"Failed to {what} ({next}): {reason}");
        }
    }

    private void RecordError(string error)
    {
        lock (_stateLock)
        {
            _lastError = error;
            _lastErrorUtc = DateTime.UtcNow;
        }
    }

    // Logging to the channel can fail or hang during the same outage that caused the warning, so it is bounded and
    // never throws out of the loop. The same warning goes to the channel at most once per RepeatWarningInterval.
    private async Task LogWarningAsync(string message)
    {
        message = $"[{ServiceName}] {message}";
        bool repeat;
        lock (_stateLock)
        {
            var now = DateTime.UtcNow;
            repeat = message == _lastWarningLogged && now - _lastWarningLoggedUtc < RepeatWarningInterval;
            if (!repeat)
            {
                _lastWarningLogged = message;
                _lastWarningLoggedUtc = now;
            }
        }
        if (repeat)
        {
            LoggingService.LogToConsole(message, ExtendedLogSeverity.Warning);
            return;
        }

        try
        {
            await _loggingService.LogChannelAndFile(message, ExtendedLogSeverity.Warning).WaitAsync(LogTimeout);
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"{message} (could not log to channel/file: {e.Message})", ExtendedLogSeverity.Warning);
        }
    }

    private static RequestOptions Options(CancellationToken token) => new() { CancelToken = token };

    /// <summary>
    /// Adopts the newest prompt, removes older duplicates, and schedules a prompt when a human posted after it or
    /// there is none (<see cref="DueFromHistory"/>). Never overrides a due time set by a newer message.
    /// </summary>
    private async Task SyncFromHistoryAsync(ITextChannel channel, CancellationToken token)
    {
        var messages = (await channel.GetMessagesAsync(HistoryScanLimit, options: Options(token)).FlattenAsync())
            .OrderByDescending(m => m.Id)
            .ToList();
        token.ThrowIfCancellationRequested();

        var prompts = messages.Where(IsPrompt).ToList();
        var newestPrompt = prompts.FirstOrDefault();
        if (newestPrompt != null)
        {
            _promptId = newestPrompt.Id;
            foreach (var stale in prompts.Skip(1))
                await DeleteMessageAsync(channel, stale.Id, token);
        }

        var latestHuman = messages.FirstOrDefault(m => IsHumanIntroMessage(m, channel.Id) && (newestPrompt == null || m.Id > newestPrompt.Id));
        var now = DateTime.UtcNow;
        if (DueFromHistory(now, _schedule!.Delay, newestPrompt != null, latestHuman?.Timestamp.UtcDateTime) is { } due)
            _schedule.ScheduleIfIdle(due);
        lock (_stateLock) _lastSyncUtc = now;
    }

    private async Task PostPromptAsync(ITextChannel channel, CancellationToken token)
    {
        // Skip if the prompt is already the latest message, e.g. the human message that started the timer was deleted.
        var latest = (await channel.GetMessagesAsync(1, options: Options(token)).FlattenAsync()).FirstOrDefault();
        token.ThrowIfCancellationRequested();
        if (latest != null && IsPrompt(latest))
        {
            _promptId ??= latest.Id;
            return;
        }

        var sent = await channel.SendMessageAsync(
            components: BuildPrompt(_settings.Channels.Rules, _settings.Channels.GenericHelp),
            allowedMentions: AllowedMentions.None,
            flags: MessageFlags.ComponentsV2,
            options: Options(token));

        var previous = _promptId;
        _promptId = sent.Id;
        lock (_stateLock) _lastPostUtc = DateTime.UtcNow;
        if (previous is { } previousId && previousId != sent.Id)
            await DeleteMessageAsync(channel, previousId, token);
    }

    private async Task DeleteMessageAsync(ITextChannel channel, ulong messageId, CancellationToken token)
    {
        // Register first: the prompt is often uncached (fetched over REST after a restart), so the
        // audit log would otherwise post an "uncached message deleted" card for it.
        _botDeletions.Register([messageId], DateTimeOffset.UtcNow);
        try
        {
            await channel.DeleteMessageAsync(messageId, Options(token));
        }
        catch (HttpException e) when (e.HttpCode == HttpStatusCode.NotFound)
        {
            // Already deleted.
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            await LogWarningAsync($"Failed to delete old introduction prompt {messageId}: {e.Message}");
        }
    }
}
