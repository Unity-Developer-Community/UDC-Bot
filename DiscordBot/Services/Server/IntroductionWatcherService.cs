using System.Net;
using Discord.Net;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Server;

/// <summary>
/// Keeps a short "say hello" prompt as the latest message in the Introductions channel.
/// After a human posts, waits until the channel has been quiet for DelayMinutes, then posts a
/// new prompt and deletes the previous one.
/// </summary>
public class IntroductionWatcherService
{
    private const string ServiceName = "IntroductionWatcherService";
    private const int HistoryScanLimit = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly Color AccentColour = new(0x12D687);

    private readonly DiscordSocketClient _client;
    private readonly BotSettings _settings;
    private readonly ILoggingService _loggingService;
    private readonly CancellationToken _shutdownToken;
    private readonly ITextChannel _channel = null!;
    private readonly IntroductionPromptSchedule _schedule = null!;

    // Only touched from the background loop.
    private ulong? _promptId;

    public IntroductionWatcherService(DiscordSocketClient client, BotSettings settings, ILoggingService loggingService,
        CancellationTokenSource cts)
    {
        _client = client;
        _settings = settings;
        _loggingService = loggingService;
        _shutdownToken = cts.Token;

        if (!settings.IntroductionWatch.Enabled)
        {
            LoggingService.LogServiceDisabled(ServiceName, nameof(settings.IntroductionWatch.Enabled));
            return;
        }

        if (_client.GetChannel(settings.Channels.Introduction?.Id ?? 0) is not ITextChannel channel)
        {
            LoggingService.LogToConsole($"[{ServiceName}] Introduction channel not found.", LogSeverity.Error);
            return;
        }

        _channel = channel;
        _schedule = new IntroductionPromptSchedule(settings.IntroductionWatch.DelayMinutes);

        _client.MessageReceived += EventGuard.Guarded<SocketMessage>(OnMessageReceived, nameof(OnMessageReceived));
        Task.Run(RunAsync);

        LoggingService.LogServiceEnabled(ServiceName);
    }

    public static MessageComponent BuildPrompt(ChannelInfo? rulesChannel, ChannelInfo? helpChannel) =>
        new ComponentBuilderV2()
            .WithContainer(new ContainerBuilder()
                .WithAccentColor(AccentColour)
                .WithTextDisplay("### 👋 New here? Introduce yourself!\n" +
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
        if (IsHumanIntroMessage(message))
            _schedule.Touch(DateTime.UtcNow);
        return Task.CompletedTask;
    }

    private bool IsHumanIntroMessage(IMessage message) =>
        message.Channel.Id == _channel.Id
        && message is IUserMessage
        && message.Type is MessageType.Default or MessageType.Reply
        && !message.Author.IsUserBotOrWebhook();

    private bool IsPrompt(IMessage message) =>
        message.Author.Id == _client.CurrentUser.Id
        && message.Flags is { } flags
        && flags.HasFlag(MessageFlags.ComponentsV2);

    private async Task RunAsync()
    {
        try
        {
            await RecoverFromHistoryAsync();

            while (!_shutdownToken.IsCancellationRequested)
            {
                await Task.Delay(PollInterval, _shutdownToken);
                try
                {
                    if (_schedule.TryTakeDue(DateTime.UtcNow))
                        await PostPromptAsync();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    await _loggingService.LogChannelAndFile(
                        $"[{ServiceName}] Failed to post the introduction prompt: {e.Message}", ExtendedLogSeverity.Warning);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Finds the existing prompt after a restart, removes any older duplicates, and schedules a
    /// new prompt if there is none or a human posted after it.
    /// </summary>
    private async Task RecoverFromHistoryAsync()
    {
        try
        {
            var messages = (await _channel.GetMessagesAsync(HistoryScanLimit).FlattenAsync())
                .OrderByDescending(m => m.Id)
                .ToList();

            var prompts = messages.Where(IsPrompt).ToList();
            var newestPrompt = prompts.FirstOrDefault();

            if (newestPrompt != null)
            {
                _promptId = newestPrompt.Id;
                foreach (var stale in prompts.Skip(1))
                    await DeleteMessageAsync(stale.Id);
            }

            var humanAfterPrompt = messages.Any(m => IsHumanIntroMessage(m) && (newestPrompt == null || m.Id > newestPrompt.Id));
            if (newestPrompt == null || humanAfterPrompt)
                _schedule.Touch(DateTime.UtcNow);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await _loggingService.LogChannelAndFile(
                $"[{ServiceName}] Could not read Introductions history on startup: {e.Message}", ExtendedLogSeverity.Warning);
        }
    }

    private async Task PostPromptAsync()
    {
        // Skip if the prompt is already the latest message, e.g. the human message that started the timer was deleted.
        var latest = (await _channel.GetMessagesAsync(1).FlattenAsync()).FirstOrDefault();
        if (latest != null && IsPrompt(latest))
        {
            _promptId ??= latest.Id;
            return;
        }

        var sent = await _channel.SendMessageAsync(
            components: BuildPrompt(_settings.Channels.Rules, _settings.Channels.GenericHelp),
            allowedMentions: AllowedMentions.None,
            flags: MessageFlags.ComponentsV2);

        var previous = _promptId;
        _promptId = sent.Id;
        if (previous is { } previousId && previousId != sent.Id)
            await DeleteMessageAsync(previousId);
    }

    private async Task DeleteMessageAsync(ulong messageId)
    {
        try
        {
            await _channel.DeleteMessageAsync(messageId);
        }
        catch (HttpException e) when (e.HttpCode == HttpStatusCode.NotFound)
        {
            // Already deleted.
        }
        catch (Exception e)
        {
            await _loggingService.LogChannelAndFile(
                $"[{ServiceName}] Failed to delete old introduction prompt {messageId}: {e.Message}", ExtendedLogSeverity.Warning);
        }
    }
}
