using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Extensions;
using DiscordBot.Modules;
using DiscordBot.Services;
using DiscordBot.Settings.Deserialized;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using IMessage = Discord.IMessage;

namespace DiscordBot
{
    public class Program
    {
        public static string CommandList;

        private DiscordSocketClient _client;

        private CommandService _commandService;
        private IServiceProvider _services;
        private IServiceCollection _serviceCollection;
        private ILoggingService _loggingService;
        private DatabaseService _databaseService;
        private UserService _userService;
        private PublisherService _publisherService;
        private UpdateService _updateService;
        private AudioService _audioService;
        private AnimeService _animeService;
        private FeedService _feedService;
        private CurrencyService _currencyService;
        private ReactRoleService _reactRoleService;

        private static PayWork _payWork;
        private static Rules _rules;
        private static Settings.Deserialized.Settings _settings;
        private static UserSettings _userSettings;

        public static void Main(string[] args) =>
            new Program().MainAsync().GetAwaiter().GetResult();

        private async Task MainAsync()
        {
            DeserializeSettings();

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose, AlwaysDownloadUsers = true, MessageCacheSize = 50
            });

            _commandService =
                new CommandService(new CommandServiceConfig {CaseSensitiveCommands = false, DefaultRunMode = RunMode.Async});
            _loggingService = new LoggingService(_client, _settings);
            _databaseService = new DatabaseService(_loggingService, _settings);
            _publisherService = new PublisherService(_client, _databaseService, _settings);
            _animeService = new AnimeService(_client, _loggingService, _settings);
            _feedService = new FeedService(_client, _settings);
            _updateService = new UpdateService(_client, _loggingService, _publisherService, _databaseService, _animeService, _settings,
                _feedService);
            _userService = new UserService(_client, _databaseService, _loggingService, _updateService, _settings, _userSettings);

            _audioService = new AudioService(_loggingService, _client, _settings);
            _currencyService = new CurrencyService();
            _serviceCollection = new ServiceCollection();
            _reactRoleService = new ReactRoleService(_client, _loggingService, _settings);

            _serviceCollection.AddSingleton(_loggingService);
            _serviceCollection.AddSingleton(_databaseService);
            _serviceCollection.AddSingleton(_userService);
            //_serviceCollection.AddSingleton(_work);
            //TODO: rework work service
            _serviceCollection.AddSingleton(_publisherService);
            _serviceCollection.AddSingleton(_updateService);
            _serviceCollection.AddSingleton(_audioService);
            _serviceCollection.AddSingleton(_animeService);
            _serviceCollection.AddSingleton(_settings);
            _serviceCollection.AddSingleton(_rules);
            _serviceCollection.AddSingleton(_payWork);
            _serviceCollection.AddSingleton(_userSettings);
            _serviceCollection.AddSingleton(_currencyService);
            _serviceCollection.AddSingleton(_reactRoleService);
            _services = _serviceCollection.BuildServiceProvider();


            await InstallCommands();

            _client.Log += Logger;
            // await InitCommands();

            await _client.LoginAsync(TokenType.Bot, _settings.Token);
            await _client.StartAsync();

            _client.Ready += () =>
            {
                Console.WriteLine("Bot is connected");
                //_audio.Music();
                return Task.CompletedTask;
            };

            await Task.Delay(-1);
        }

        private static Task Logger(LogMessage message)
        {
            ConsoleColor cc = Console.ForegroundColor;
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

        public async Task InstallCommands()
        {
            // Hook the MessageReceived Event into our Command Handler
            _client.MessageReceived += HandleCommand;
            _client.MessageReceived += _userService.UpdateXp;
            _client.MessageReceived += _userService.Thanks;
            _client.MessageUpdated += _userService.ThanksEdited;
            _client.MessageReceived += _userService.CodeCheck;
            _client.MessageReceived += _userService.ScoldForAtEveryoneUsage;
            //_client.MessageReceived += _userService.UselessAskingCheck; //to do declared at method

            //_client.MessageReceived += _work.OnMessageAdded;
            _client.MessageDeleted += MessageDeleted;
            _client.UserJoined += UserJoined;
            _client.GuildMemberUpdated += UserUpdated;
            _client.UserLeft += UserLeft;

            // Discover all of the commands in this assembly and load them.
            await _commandService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

            StringBuilder commandList = new StringBuilder();

            commandList.Append("__Role Commands__\n");
            foreach (var c in _commandService.Commands.Where(x => x.Module.Name == "role").OrderBy(c => c.Name))
            {
                commandList.Append($"**role {c.Name}** : {c.Summary}\n");
            }
            
            commandList.Append("\n");
            commandList.Append("__General Commands__\n");
            
            foreach (var c in _commandService.Commands.Where(x => x.Module.Name == "UserModule").OrderBy(c => c.Name))
            {
                commandList.Append($"**{c.Name}** : {c.Summary}\n");
            }

            CommandList = commandList.ToString();

            ReactionRoleModule.GenerateCommandList(_commandService);
        }

        private async Task MessageDeleted(Cacheable<IMessage, ulong> message, ISocketMessageChannel channel)
        {
            if (message.Value.Author.IsBot || channel.Id == _settings.BotAnnouncementChannel.Id)
                return;

            var content = message.Value.Content;
            if (content.Length > 800)
                content = content.Substring(0, 800);

            EmbedBuilder builder = new EmbedBuilder()
                .WithColor(new Color(200, 128, 128))
                .WithTimestamp(message.Value.Timestamp)
                .WithFooter(footer =>
                {
                    footer
                        .WithText($"In channel {message.Value.Channel.Name}");
                })
                .WithAuthor(author =>
                {
                    author
                        .WithName($"{message.Value.Author.Username}");
                })
                .AddField("Deleted message", content);
            Embed embed = builder.Build();

            await _loggingService.LogAction(
                $"User {message.Value.Author.Username}#{message.Value.Author.DiscriminatorValue} has " +
                $"deleted the message\n{content}\n from channel #{channel.Name}", true, false);
            await _loggingService.LogAction(" ", false, true, embed);
        }

        private async Task UserJoined(SocketGuildUser user)
        {
            ulong general = _settings.GeneralChannel.Id;
            var socketTextChannel = _client.GetChannel(general) as SocketTextChannel;

            _databaseService.AddNewUser(user);

            //Check for existing mute
            if (_userService._mutedUsers.HasUser(user.Id))
            {
                await user.AddRoleAsync(socketTextChannel?.Guild.GetRole(_settings.MutedRoleId));
                await _loggingService.LogAction(
                    $"Currently muted user rejoined - {user.Mention} - `{user.Username}#{user.DiscriminatorValue}` - ID : `{user.Id}`");
                await socketTextChannel.SendMessageAsync(
                    $"{user.Mention} tried to rejoin the server to avoid their mute. Mute time increased by 72 hours.");
                _userService._mutedUsers.AddCooldown(user.Id, hours: 72);
                return;
            }


            await _loggingService.LogAction(
                $"User Joined - {user.Mention} - `{user.Username}#{user.DiscriminatorValue}` - ID : `{user.Id}`");

            Embed em = _userService.WelcomeMessage(user.GetAvatarUrl(), user.Username, user.DiscriminatorValue);

            if (socketTextChannel != null)
            {
                await socketTextChannel.SendMessageAsync(string.Empty, false, em);
            }

            string globalRules = _rules.Channel.First(x => x.Id == 0).Content;
            IDMChannel dm = await user.GetOrCreateDMChannelAsync();
            await dm.SendMessageAsync(
                "Hello and welcome to Unity Developer Community !\nHope you enjoy your stay.\nHere are some rules to respect to keep the community friendly, please read them carefully.\n" +
                "Please also read the additional informations in the **#welcome** channel." +
                "You can get all the available commands on the server by typing !help in the **#bot-commands** channel.");
            await dm.SendMessageAsync(globalRules);

            //TODO: add users when bot was offline
        }

        private async Task UserUpdated(SocketGuildUser oldUser, SocketGuildUser user)
        {
            if (oldUser.Nickname != user.Nickname)
            {
                await _loggingService.LogAction(
                    $"User {oldUser.Nickname ?? oldUser.Username}#{oldUser.DiscriminatorValue} changed his " +
                    $"username to {user.Nickname ?? user.Username}#{user.DiscriminatorValue}");
                _databaseService.UpdateUserName(user.Id, user.Nickname);
            }

            if (oldUser.AvatarId != user.AvatarId)
            {
                var avatar = user.GetAvatarUrl();
                _databaseService.UpdateUserAvatar(user.Id, avatar);
            }
        }

        private async Task UserLeft(SocketGuildUser user)
        {
            DateTime joinDate;
            DateTime.TryParse(_databaseService.GetUserJoinDate(user.Id), out joinDate);
            TimeSpan timeStayed = DateTime.Now - joinDate;
            await _loggingService.LogAction(
                $"User Left - After {(timeStayed.Days > 1 ? Math.Floor((double) timeStayed.Days).ToString() + " days" : " ")}" +
                $" {Math.Floor((double) timeStayed.Hours).ToString()} hours {user.Mention} - `{user.Username}#{user.DiscriminatorValue}` - ID : `{user.Id}`");
            _databaseService.DeleteUser(user.Id);
        }

        public async Task HandleCommand(SocketMessage messageParam)
        {
            // Don't process the command if it was a System Message
            if (!(messageParam is SocketUserMessage message))
                return;

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;
            char prefix = _settings.Prefix;
            // Determine if the message is a command, based on if it starts with '!' or a mention prefix
            if (!(message.HasCharPrefix(prefix, ref argPos) || message.HasMentionPrefix(_client.CurrentUser, ref argPos)))
                return;
            // Create a Command Context
            var context = new CommandContext(_client, message);
            // Execute the command. (result does not indicate a return value,
            // rather an object stating if the command executed successfully)
            var result = await _commandService.ExecuteAsync(context, argPos, _services);
            if (!result.IsSuccess)
            {
                await context.Channel.SendMessageAsync(result.ErrorReason).DeleteAfterSeconds(10);
            }
        }

        private static void DeserializeSettings()
        {
            using (var file = File.OpenText(@"Settings/Settings.json"))
            {
                _settings = JsonConvert.DeserializeObject<Settings.Deserialized.Settings>(file.ReadToEnd());
            }

            using (var file = File.OpenText(@"Settings/PayWork.json"))
            {
                _payWork = JsonConvert.DeserializeObject<PayWork>(file.ReadToEnd());
            }

            using (var file = File.OpenText(@"Settings/Rules.json"))
            {
                _rules = JsonConvert.DeserializeObject<Rules>(file.ReadToEnd());
            }

            using (var file = File.OpenText(@"Settings/UserSettings.json"))
            {
                _userSettings = JsonConvert.DeserializeObject<UserSettings>(file.ReadToEnd());
            }
        }
    }
}