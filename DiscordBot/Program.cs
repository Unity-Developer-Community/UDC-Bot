using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Services;
using DiscordBot.Settings.Deserialized;
using DiscordBot.Utils;
using Microsoft.Extensions.DependencyInjection;

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

            _commandService = new CommandService(new CommandServiceConfig
            {
                CaseSensitiveCommands = false,
                DefaultRunMode = RunMode.Async
            });

            _services = ConfigureServices();
            _commandHandlingService = _services.GetRequiredService<CommandHandlingService>();
            _services.GetRequiredService<ModerationService>();

            await _commandHandlingService.Initialize();

            _client.Log += Logger;

            await _client.LoginAsync(TokenType.Bot, _settings.Token);
            await _client.StartAsync();

            _client.Ready += () =>
            {
                Console.WriteLine("Bot is connected");

                _client.GetGuild(_settings.GuildId)
                    ?.GetTextChannel(_settings.BotAnnouncementChannel.Id)
                    ?.SendMessageAsync($"Bot Started.");

                //_audio.Music();
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
                .BuildServiceProvider();

        private static Task Logger(LogMessage message)
        {
            var cc = Console.ForegroundColor;
            switch (message.Severity)
            {
                case LogSeverity.Critical:
                case LogSeverity.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                case LogSeverity.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case LogSeverity.Info:
                    Console.ForegroundColor = ConsoleColor.White;
                    break;
                case LogSeverity.Verbose:
                case LogSeverity.Debug:
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    break;
            }

            Console.WriteLine($"{DateTime.Now,-19} [{message.Severity,8}] {message.Source}: {message.Message}");
            Console.ForegroundColor = cc;
            return Task.CompletedTask;
        }

        private static void DeserializeSettings()
        {
            _settings = SerializeUtil.DeserializeFile<Settings.Deserialized.Settings>(@"Settings/Settings.json");
            _rules = SerializeUtil.DeserializeFile<Rules>(@"Settings/Rules.json");
            _userSettings = SerializeUtil.DeserializeFile<UserSettings>(@"Settings/UserSettings.json");
        }
    }
}