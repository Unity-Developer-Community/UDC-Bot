using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Services;
using DiscordBot.Services.Logging;
using DiscordBot.Settings.Deserialized;
using DiscordBot.Utils;
using Microsoft.Extensions.DependencyInjection;
using RunMode = Discord.Commands.RunMode;

namespace DiscordBot
{
    public class Program
    {
        private static Rules _rules;
        private static Settings.Deserialized.Settings _settings;
        private static UserSettings _userSettings;
        private DiscordSocketClient _client;
        private CommandHandlingService _commandHandlingService;

        private CommandService _commandService;
        private InteractionService _interactionService;
        private IServiceProvider _services;

        public static void Main(string[] args) =>
            new Program().MainAsync().GetAwaiter().GetResult();

        private async Task MainAsync()
        {
            DeserializeSettings();

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose,
                AlwaysDownloadUsers = true,
                MessageCacheSize = 50,
                GatewayIntents = GatewayIntents.All,
            });
            // Logging Service explicitly for Gateway events
            _client.Log += LoggingService.DiscordNetLogger;

            await _client.LoginAsync(TokenType.Bot, _settings.Token);
            await _client.StartAsync();

            _client.Ready += () =>
            {
                _interactionService = new InteractionService(_client);
                _commandService = new CommandService(new CommandServiceConfig
                {
                    CaseSensitiveCommands = false,
                    DefaultRunMode = RunMode.Async
                });

                _services = ConfigureServices();
                _commandHandlingService = _services.GetRequiredService<CommandHandlingService>();
                
                _client.GetGuild(_settings.GuildId)
                    ?.GetTextChannel(_settings.BotAnnouncementChannel.Id)
                    ?.SendMessageAsync($"Bot Started.");
                
                LoggingService.LogToConsole($"Bot is connected.", LogSeverity.Info);
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
                .AddSingleton<ModerationService>()
                .AddSingleton<PublisherService>()
                .AddSingleton<FeedService>()
                .AddSingleton<UpdateService>()
                .AddSingleton<CurrencyService>()
                .AddSingleton<ReactRoleService>()
                .AddSingleton<ReminderService>()
                .BuildServiceProvider();

        private static void DeserializeSettings()
        {
            _settings = SerializeUtil.DeserializeFile<Settings.Deserialized.Settings>(@"Settings/Settings.json");
            _rules = SerializeUtil.DeserializeFile<Rules>(@"Settings/Rules.json");
            _userSettings = SerializeUtil.DeserializeFile<UserSettings>(@"Settings/UserSettings.json");
        }
    }
}