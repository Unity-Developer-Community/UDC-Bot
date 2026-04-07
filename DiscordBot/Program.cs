using System.Diagnostics;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Service;
using DiscordBot.Services;
using DiscordBot.Services.Tips;
using DiscordBot.Settings;
using DiscordBot.Utils;
using Microsoft.Extensions.DependencyInjection;
using RunMode = Discord.Commands.RunMode;

namespace DiscordBot;

public class Program
{
    private int _isInitialized = 0;

    private static Rules _rules = null!;
    private static BotSettings _settings = null!;
    private static UserSettings _userSettings = null!;
    private DiscordSocketClient _client = null!;

    private CommandService _commandService = null!;
    private InteractionService _interactionService = null!;
    private IServiceProvider _services = null!;

    private readonly CancellationTokenSource _cts = new();

    public static void Main(string[] args) =>
        new Program().MainAsync().GetAwaiter().GetResult();

    private async Task MainAsync()
    {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _cts.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _cts.Cancel();

        DeserializeSettings();

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            LogLevel = LogSeverity.Verbose,
            AlwaysDownloadUsers = true,
            MessageCacheSize = 1024,
            GatewayIntents = GatewayIntents.Guilds
                           | GatewayIntents.GuildMembers
                           | GatewayIntents.GuildMessages
                           | GatewayIntents.GuildMessageReactions
                           | GatewayIntents.DirectMessages
                           | GatewayIntents.MessageContent,
        });
        _client.Log += LoggingService.DiscordNetLogger;

        await _client.LoginAsync(TokenType.Bot, _settings.Token);
        await _client.StartAsync();

        _client.Ready += () =>
        {
            // Ready can be called additional times if the bot disconnects for long enough,
            // so we need to make sure we only initialize commands and such for the bot once if it manages to re-establish connection
            if (Interlocked.CompareExchange(ref _isInitialized, 1, 0) != 0) return Task.CompletedTask;

            _interactionService = new InteractionService(_client);
            _commandService = new CommandService(new CommandServiceConfig
            {
                CaseSensitiveCommands = false,
                DefaultRunMode = RunMode.Async
            });

            _services = ConfigureServices();
            _services.GetRequiredService<CommandHandlingService>();

            // Announce, and Log bot started to track issues a bit easier
            var logger = _services.GetRequiredService<ILoggingService>();
            logger.LogChannelAndFile("Bot Started.", ExtendedLogSeverity.Positive);

            LoggingService.LogToConsole("Bot is connected.", ExtendedLogSeverity.Positive);

            _services.GetRequiredService<UnityHelpService>();
            _services.GetRequiredService<RecruitService>();
            _services.GetRequiredService<BirthdayAnnouncementService>();
            _services.GetRequiredService<AuditLogService>();
            _services.GetRequiredService<WelcomeService>();
            _services.GetRequiredService<XpService>();
            _services.GetRequiredService<KarmaService>();
            _services.GetRequiredService<CodeCheckService>();
            _services.GetRequiredService<EveryoneScoldService>();
            _services.GetRequiredService<MikuService>();
            _services.GetRequiredService<KarmaResetService>();

            return Task.CompletedTask;
        };

        try
        {
            await Task.Delay(Timeout.Infinite, _cts.Token);
        }
        catch (TaskCanceledException) { }

        LoggingService.LogToConsole("Shutdown signal received, stopping...", ExtendedLogSeverity.Warning);
        using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await _client.StopAsync().WaitAsync(shutdownTimeout.Token); }
        catch (OperationCanceledException) { LoggingService.LogToConsole("Client stop timed out.", ExtendedLogSeverity.Warning); }
        LoggingService.LogToConsole("Bot stopped.", ExtendedLogSeverity.Positive);
    }

    private IServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddHttpClient()
            .AddSingleton(_cts)
            .AddSingleton(_settings)
            .AddSingleton(_rules)
            .AddSingleton(_userSettings)
            .AddSingleton(_client)
            .AddSingleton(_commandService)
            .AddSingleton(_interactionService)
            .AddSingleton<CommandHandlingService>()
            .AddSingleton<ILoggingService, LoggingService>()
            .AddSingleton<DatabaseService>()
            .AddSingleton<WelcomeService>()
            .AddSingleton<XpService>()
            .AddSingleton<KarmaService>()
            .AddSingleton<CodeCheckService>()
            .AddSingleton<EveryoneScoldService>()
            .AddSingleton<MikuService>()
            .AddSingleton<ServerService>()
            .AddSingleton<DuelService>()
            .AddSingleton<ProfileCardService>()
            .AddSingleton<AuditLogService>()
            .AddSingleton<EmbedParsingService>()
            .AddSingleton<ReleaseNotesParser>()
            .AddSingleton<FeedService>()
            .AddSingleton<UnityHelpService>()
            .AddSingleton<RecruitService>()
            .AddSingleton<UpdateService>()
            .AddSingleton<SearchService>()
            .AddSingleton<CurrencyService>()
            .AddSingleton<ReminderService>()
            .AddSingleton<WeatherService>()
            .AddSingleton<AirportService>()
            .AddSingleton<TipService>()
            .AddSingleton<CannedResponseService>()
            .AddSingleton<UserExtendedService>()
            .AddSingleton<BirthdayAnnouncementService>()
            .AddSingleton<CasinoService>()
            .AddSingleton<TransactionFormatter>()
            .AddSingleton<GameService>()
            .AddSingleton<KarmaResetService>()
            .BuildServiceProvider();

    private static void DeserializeSettings()
    {
        _settings = SerializeUtil.DeserializeFile<BotSettings>(@"Settings/Settings.json");
        _rules = SerializeUtil.DeserializeFile<Rules>(@"Settings/Rules.json");
        _userSettings = SerializeUtil.DeserializeFile<UserSettings>(@"Settings/UserSettings.json");
    }
}
