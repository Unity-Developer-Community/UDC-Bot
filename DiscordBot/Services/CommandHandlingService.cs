using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Extensions;
using DiscordBot.Utils.Attributes;
using ParameterInfo = Discord.Commands.ParameterInfo;

namespace DiscordBot.Services
{
    public class CommandHandlingService
    {
        public bool IsInitialized { get; private set; }

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
            IsInitialized = true;
        }

        /// <summary> Generates a command list that can provide users with information. Commands require [Command][Summary] and [Priority](If not ordering by name)</summary>
        public async Task<string> GetCommandList(string moduleName, bool orderByName = false, bool includeArgs = true, bool includeModuleName = true)
        {
            // Simple wait.
            while (!IsInitialized)
                await Task.Delay(1000);

            var commandList = new StringBuilder();

            commandList.Append($"__{moduleName} Commands__\n");

            // Generates a list of commands that doesn't include any that have the ``HideFromHelp`` attribute.
            var commands = _commandService.Commands.Where(x => x.Module.Name == moduleName && !x.Attributes.Contains(new HideFromHelpAttribute()));
            // Orders the list either by name or by priority, if no priority is given we push it to the end.
            commands = orderByName ? commands.OrderBy(c => c.Name) : commands.OrderBy(c => (c.Priority > 0 ? c.Priority : 1000));
            
            foreach (var c in commands)
            {
                commandList.Append($"**{(includeModuleName ? moduleName + " " : string.Empty)}{c.Name}** : {c.Summary} {GetArguments(includeArgs, c.Parameters)}\n");
            }
            return commandList.ToString();
        }

        private string GetArguments(bool getArgs, IReadOnlyList<ParameterInfo> arguments)
        {
            if (!getArgs) return string.Empty;

            var args = string.Empty;
            foreach (var info in arguments)
            {
                args += $"`{info.Name}`{(info.IsOptional ? "\\*" : string.Empty)} ";
            }
            if (args.Length > 0)
                args = $"- args: *( {args})*";
            return args;
        }

        private async Task HandleCommand(SocketMessage messageParam)
        {
            // Don't process the command if it was a System Message
            if (!(messageParam is SocketUserMessage message))
                return;

            // Create a number to track where the prefix ends and the command begins
            var argPos = 0;
            var prefix = _settings.Prefix;
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
                // If it was 1 character it was likely someone just typing !
                if (message.Content.Length == 1)
                    return;
                
                await context.Channel.SendMessageAsync(result.ErrorReason).DeleteAfterSeconds(10);
            }
        }
    }
}