using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Service;
using DiscordBot.Services;
using DiscordBot.Services.Rendering;
using DiscordBot.Services.Tips;
using DiscordBot.Settings;
using DiscordBot.Utils;
using Microsoft.Extensions.DependencyInjection;
using RunMode = Discord.Commands.RunMode;

namespace DiscordBot;

public class Program
{
    private bool _isInitialized = false;

    private static Rules _rules;
    private static BotSettings _settings;
    private static UserSettings _userSettings;
    private DiscordSocketClient _client;
    private CommandHandlingService _commandHandlingService;

    private CommandService _commandService;
    private InteractionService _interactionService;
    private IServiceProvider _services;

    private UnityHelpService _unityHelpService;
    private RecruitService _recruitService;

    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--render-smoke", StringComparison.OrdinalIgnoreCase))
        {
            var outputPath = args.Length > 1 ? args[1] : null;
            var assetsRootPath = args.Length > 2
                ? args[2]
                : Path.Combine(AppContext.BaseDirectory, "Assets");
            return ProfileCardRenderSmoke.Run(assetsRootPath, outputPath);
        }

        if (args.Length > 0 && args[0].Equals("--render-stress", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadPositiveArgument(args, 1, 100, out var iterations) ||
                !TryReadPositiveArgument(args, 2, 4, out var parallelism))
            {
                Console.Error.WriteLine("Usage: --render-stress [iterations] [parallelism] [assets-root]");
                return 2;
            }

            var assetsRootPath = args.Length > 3
                ? args[3]
                : Path.Combine(AppContext.BaseDirectory, "Assets");
            return ProfileCardRenderSmoke.RunStress(assetsRootPath, iterations, parallelism);
        }

        new Program().MainAsync().GetAwaiter().GetResult();
        return 0;
    }

    private static bool TryReadPositiveArgument(
        IReadOnlyList<string> arguments,
        int index,
        int defaultValue,
        out int value)
    {
        if (arguments.Count <= index)
        {
            value = defaultValue;
            return true;
        }

        return int.TryParse(arguments[index], out value) && value > 0;
    }

    private async Task MainAsync()
    {
        DeserializeSettings();

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            LogLevel = LogSeverity.Verbose,
            AlwaysDownloadUsers = true,
            MessageCacheSize = 1024,
            GatewayIntents = GatewayIntents.All,
        });
        _client.Log += LoggingService.DiscordNetLogger;

        await _client.LoginAsync(TokenType.Bot, _settings.Token);
        await _client.StartAsync();

        _client.Ready += () =>
        {
            // Ready can be called additional times if the bot disconnects for long enough,
            // so we need to make sure we only initialize commands and such for the bot once if it manages to re-establish connection
            if (_isInitialized) return Task.CompletedTask;

            _interactionService = new InteractionService(_client);
            _commandService = new CommandService(new CommandServiceConfig
            {
                CaseSensitiveCommands = false,
                DefaultRunMode = RunMode.Async
            });

            _services = ConfigureServices();
            _commandHandlingService = _services.GetRequiredService<CommandHandlingService>();
            _services.GetRequiredService<ModerationService>();

            // Announce, and Log bot started to track issues a bit easier
            var logger = _services.GetRequiredService<ILoggingService>();
            logger.LogChannelAndFile("Bot Started.", ExtendedLogSeverity.Positive);

            LoggingService.LogToConsole("Bot is connected.", ExtendedLogSeverity.Positive);
            _isInitialized = true;

            _unityHelpService = _services.GetRequiredService<UnityHelpService>();
            _recruitService = _services.GetRequiredService<RecruitService>();
            _services.GetRequiredService<IntroductionWatcherService>();
            _services.GetRequiredService<BirthdayAnnouncementService>();
            _services.GetRequiredService<KarmaResetService>();

            return Task.CompletedTask;
        };

        await Task.Delay(-1);
    }

    private IServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddSingleton(_settings)
            .AddSingleton(_rules)
            .AddSingleton(_userSettings)
            .AddSingleton(_client)
            .AddSingleton(_commandService)
            .AddSingleton(_interactionService)
            .AddSingleton<CommandHandlingService>()
            .AddSingleton<ILoggingService, LoggingService>()
            .AddSingleton<DatabaseService>()
            .AddSingleton(new ImageRenderOptions(_settings.AssetsRootPath))
            .AddSingleton<IAvatarDownloader>(services =>
            {
                var handler = new SocketsHttpHandler
                {
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                    MaxConnectionsPerServer = 8,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10)
                };
                var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                return new AvatarDownloader(client, services.GetRequiredService<ImageRenderOptions>());
            })
            .AddSingleton<IProfileCardRenderer, ProfileCardRenderer>()
            .AddSingleton<UserService>()
            .AddSingleton<IntroductionWatcherService>()
            .AddSingleton<ModerationService>()
            .AddSingleton<FeedService>()
            .AddSingleton<UnityHelpService>()
            .AddSingleton<RecruitService>()
            .AddSingleton<UpdateService>()
            .AddSingleton<CurrencyService>()
            .AddSingleton<ReminderService>()
            .AddSingleton<WeatherService>()
            .AddSingleton<AirportService>()
            .AddSingleton<TipService>()
            .AddSingleton<CannedResponseService>()
            .AddSingleton<UserExtendedService>()
            .AddSingleton<BirthdayAnnouncementService>()
            .AddSingleton<CasinoService>()
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
