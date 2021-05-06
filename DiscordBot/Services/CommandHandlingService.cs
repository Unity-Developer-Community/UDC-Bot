using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Extensions;
using DiscordBot.Modules;

namespace DiscordBot.Services
{
    public class CommandHandlingService
    {
        public static string CommandList;
        
        private readonly DiscordSocketClient _client;
        private readonly CommandService _commandService;
        private readonly IServiceProvider _services;
        private readonly Settings.Deserialized.Settings _settings;

        public CommandHandlingService(
            DiscordSocketClient client,
            CommandService commandService,
            IServiceProvider services,
            Settings.Deserialized.Settings settings
        )
        {
            _client = client;
            _commandService = commandService;
            _services = services;
            _settings = settings;
            
            /*
             Event subscriptions
            */
            _client.MessageReceived += HandleCommand;
        }

        public async Task Initialize()
        {
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
            
            // Generates an individual command list for the Reaction Roles
            ReactionRoleModule.GenerateCommandList(_commandService);
        }

        private async Task HandleCommand(SocketMessage messageParam)
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
    }
}
