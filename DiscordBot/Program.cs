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

    public static void Main(string[] args) =>
        new Program().MainAsync().GetAwaiter().GetResult();

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

            // Announce, and Log bot started to track issues a bit easier
            var logger = _services.GetRequiredService<ILoggingService>();
            logger.LogChannelAndFile("Bot Started.", ExtendedLogSeverity.Positive);

            LoggingService.LogToConsole("Bot is connected.", ExtendedLogSeverity.Positive);
            _isInitialized = true;
                
            _unityHelpService = _services.GetRequiredService<UnityHelpService>();
            _recruitService = _services.GetRequiredService<RecruitService>();
            _services.GetRequiredService<IntroductionWatcherService>();
            _services.GetRequiredService<BirthdayAnnouncementService>();
            
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
            .AddSingleton<UserService>()
            .AddSingleton<IntroductionWatcherService>()
            .AddSingleton<ModerationService>()
            .AddSingleton<PublisherService>()
            .AddSingleton<FeedService>()
            .AddSingleton<UnityHelpService>()
            .AddSingleton<RecruitService>()
            .AddSingleton<UpdateService>()
            .AddSingleton<CurrencyService>()
            .AddSingleton<ReactRoleService>()
            .AddSingleton<ReminderService>()
            .AddSingleton<WeatherService>()
            .AddSingleton<AirportService>()
            .AddSingleton<TipService>()
            .AddSingleton<CannedResponseService>()
            .AddSingleton<UserExtendedService>()
            .AddSingleton<BirthdayAnnouncementService>()
            .BuildServiceProvider();

    private static void DeserializeSettings()
    {
        _settings = SerializeUtil.DeserializeFile<BotSettings>(@"Settings/Settings.json");
        _rules = SerializeUtil.DeserializeFile<Rules>(@"Settings/Rules.json");
        _userSettings = SerializeUtil.DeserializeFile<UserSettings>(@"Settings/UserSettings.json");
    }
}
