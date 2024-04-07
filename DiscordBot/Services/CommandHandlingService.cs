using System.Reflection;
using System.Text;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Attributes;
using DiscordBot.Settings;
using IResult = Discord.Interactions.IResult;
using ParameterInfo = Discord.Commands.ParameterInfo;
using PreconditionGroupResult = Discord.Commands.PreconditionGroupResult;

namespace DiscordBot.Services;

public class CommandHistoryInfo
{
    public string Command { get; set; }
    public string User { get; set; }
    public ulong UserId { get; set; }
    public string Channel { get; set; }
    public DateTime Time { get; set; }
    public string Error { get; set; } = string.Empty;
}

public class CommandHandlingService
{
    private const string ServiceName = "CommandHandlingService";
    public bool IsInitialized { get; private set; }
    
    private readonly DiscordSocketClient _client;
    private readonly CommandService _commandService;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _services;
    private readonly ILoggingService _loggingService;

    private const char DefaultPrefix = '!';
    private readonly char _commandPrefix;

    // While not the most attractive solution, it works, and is fairly cheap compared to the last solution.
    // Tuple of string moduleName, bool orderByName = false, bool includeArgs = true, bool includeModuleName = true for a dictionary
    private readonly Dictionary<(string moduleName, bool orderByName, bool includeArgs, bool includeModuleName), string> _commandList = new();
    private readonly Dictionary<(string moduleName, bool orderByName, bool includeArgs, bool includeModuleName), List<string>> _commandListMessages = new();
    
    // A Collection to store the command history
    private const int MaxCommandHistory = 200;
    private readonly List<CommandHistoryInfo> _commandHistory = new List<CommandHistoryInfo>(MaxCommandHistory);

    public CommandHandlingService(
        DiscordSocketClient client,
        CommandService commandService,
        InteractionService interactionService,
        IServiceProvider services,
        BotSettings settings,
        ILoggingService loggingService
    )
    {
        _client = client;
        _commandService = commandService;
        _interactionService = interactionService;
        _services = services;
        _loggingService = loggingService;

        // Events
        _client.MessageReceived += HandleCommand;
        _client.InteractionCreated += HandleInteraction;
        
        if (settings.GuildId == default)
        {
            _loggingService.Log(LogBehaviour.Console | LogBehaviour.File, $"{ServiceName}: GuildId not set, commands will not be registered.", ExtendedLogSeverity.Critical);
            return;
        }
        
        _commandPrefix = settings.Prefix;
        if (_commandPrefix == default)
        {
            _commandPrefix = DefaultPrefix;
            _loggingService.Log(LogBehaviour.Console | LogBehaviour.File, $"{ServiceName}: Prefix not set, defaulting to {DefaultPrefix}", ExtendedLogSeverity.Warning);
        }
        else
        {
            _loggingService.Log(LogBehaviour.Console, $"{ServiceName}: Prefix set to {_commandPrefix}", ExtendedLogSeverity.Positive);
        }

        // Initialize the command service
        Task.Run(async () =>
        {
            try
            {
                // Discover all of the commands in this assembly and load them.
                var addedEnumerable = await _commandService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
                var commandModulesAdded = addedEnumerable.ToList();
                await _loggingService.Log(LogBehaviour.Console, $"{ServiceName}: Loaded {commandModulesAdded.Count} 'Normal' modules. ({commandModulesAdded.Sum(x => x.Commands.Count)} commands)", ExtendedLogSeverity.Positive);

                var addedInteractivity = await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
                var moduleInfos = addedInteractivity.ToList();
                await _loggingService.Log(LogBehaviour.Console, $"{ServiceName}: Loaded {moduleInfos.Count} 'Interactivity' modules.", ExtendedLogSeverity.Positive);
                await _loggingService.Log(LogBehaviour.Console, $"{ServiceName}: {moduleInfos.Sum(x => x.SlashCommands.Count)} 'Slash' commands.", ExtendedLogSeverity.Positive);
                await _loggingService.Log(LogBehaviour.Console, $"{ServiceName}: {moduleInfos.Sum(x => x.ContextCommands.Count)} 'Context' commands.", ExtendedLogSeverity.Positive);
                await _loggingService.Log(LogBehaviour.Console, $"{ServiceName}: {moduleInfos.Sum(x => x.AutocompleteCommands.Count)} 'AutoComplete' commands.", ExtendedLogSeverity.Positive);
                await _loggingService.Log(LogBehaviour.Console, $"{ServiceName}: {moduleInfos.Sum(x => x.ModalCommands.Count)} 'Modal' commands.", ExtendedLogSeverity.Positive);
                await _loggingService.Log(LogBehaviour.Console, $"{ServiceName}: {moduleInfos.Sum(x => x.ComponentCommands.Count)} 'Component' commands.", ExtendedLogSeverity.Positive);
                
                //TODO Consider global commands? Maybe an attribute?
                await _interactionService.RegisterCommandsToGuildAsync(settings.GuildId);

                IsInitialized = true;
            }
            catch (Exception e)
            {
                await _loggingService.Log(LogBehaviour.Console | LogBehaviour.File, $"[{ServiceName}] Failed to initialize service while adding modules.\nException: {e}", ExtendedLogSeverity.Critical);
            }
        });
    }
    
    #region Command Lists
    
    /// <summary> Generates a command list that can provide users with information. Commands require [Command][Summary] and [Priority](If not ordering by name)
    /// The results are cached, so this method can be called frequently without performance issues.</summary>
    /// <returns> List of strings that can be sent to the user without worry of being over the message length limit.</returns>
    public List<string> GetCommandListMessages(string moduleName, bool orderByName = false, bool includeArgs = true, bool includeModuleName = true)
    {
        var tupleKey = (moduleName, orderByName, includeArgs, includeModuleName);
        if (!_commandListMessages.TryGetValue(tupleKey, out List<string> commandResults))
        {
            GenerateCommandListOutputs(tupleKey);
            commandResults = _commandListMessages[tupleKey];
        }
        return commandResults;
    }

    /// <summary> Generates a command list that can provide users with information. Commands require [Command][Summary] and [Priority](If not ordering by name)
    /// The results are cached, so this method can be called frequently without performance issues.</summary>  <remarks>Strongly suggest using GetCommandListMessages</remarks>
    /// <returns>A large string with all the formatted commands, may be over text limits and shouldn't be sent directly to user.</returns>
    public string GetCommandList(string moduleName, bool orderByName = false, bool includeArgs = true, bool includeModuleName = true)
    {
        var tupleKey = (moduleName, orderByName, includeArgs, includeModuleName);
        if (!_commandList.TryGetValue(tupleKey, out string commandResults))
        {
            GenerateCommandListOutputs(tupleKey);
            commandResults = _commandList[tupleKey];
        }
        return commandResults;
    }
    
    private void GenerateCommandListOutputs(
        (string moduleName, bool orderByName, bool includeArgs, bool includeModuleName) input)
    {
        // If we don't have the command list, we need to build it.
        var commandList = new StringBuilder();
        commandList.Append($"__{input.moduleName} Commands__\n");
        
        // Gets all of the commands in the module, and sorts them by priority.
        var commands = GetOrganizedCommandInfo(input);
        
        foreach (var c in commands)
        {
            commandList.Append($"**{(input.includeModuleName ? input.moduleName + " " : string.Empty)}{c.Name}** : {c.Summary} {GetArguments(input.includeArgs, c.Parameters)}\n");
        }
            
        string commandListString = commandList.ToString();
        _commandList[input]  = commandListString;
        _commandListMessages[input] = commandListString.MessageSplitToSize();
    }

    /// <summary> Returns a string that will fit in a discord message with commands that fit the search query. These results aren't cached making them a lot more expensive than GetCommandList, not enough to worry about, but not something you want to call in a service unless user is asking for the results directly.</summary>
    public List<string> SearchForCommand((string moduleName, bool orderByName, bool includeArgs, bool includeModuleName) input, string search)
    {
        // If we don't have the command list, we need to build it.
        var commandList = new StringBuilder();
        
        // Gets all of the commands in the module, and sorts them by priority.
        var commands = GetOrganizedCommandInfo(input, search);

        foreach (var c in commands)
        {
            if (c.Name.ToLower().Contains(search.ToLower()))
            {
                commandList.Append($"**{(input.includeModuleName ? input.moduleName + " " : string.Empty)}{c.Name}** : {c.Summary} {GetArguments(input.includeArgs, c.Parameters)}\n");
            }
        }

        return commandList.ToString().MessageSplitToSize();
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

    private IEnumerable<CommandInfo> GetOrganizedCommandInfo(
        (string moduleName, bool orderByName, bool includeArgs, bool includeModuleName) input, string search = "", bool onlyNormalUsers = true)
    {
        // Prepare attributes before linq
        var hideFromHelp = new HideFromHelpAttribute();
        var requireModerator = new RequireModeratorAttribute();
        var requireAdmin = new RequireAdminAttribute();
        
        // Generates a list of commands that doesn't include any that have the ``HideFromHelp`` attribute.
        // Adds commands that use the same Module, and contains the search query if given.
        var commands = 
            _commandService.Commands.Where(x =>
            x.Module.Name == input.moduleName && 
            !x.Attributes.Contains(hideFromHelp) &&
            (search == string.Empty || x.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))
        );
        // We try to hide commands that have moderator or admin requirements if onlyNormalUsers is true.
        commands = onlyNormalUsers
            ? commands.Where(x => !x.Preconditions.Any(y => y.TypeId == requireModerator.TypeId || y.TypeId == requireAdmin.TypeId))
            : commands;
        
        // Orders the list either by name or by priority, if no priority is given we push it to the end.
        commands = input.orderByName
            ? commands.OrderBy(c => c.Name)
            : commands.OrderBy(c => (c.Priority > 0 ? c.Priority : 1000));

        return commands;
    }

    #endregion

    private async Task HandleCommand(SocketMessage messageParam)
    {
        // Don't process the command if it was a System Message
        if (messageParam is not SocketUserMessage message)
            return;

        // Create a number to track where the prefix ends and the command begins
        var argPos = 0;
        // Determine if the message is a command, based on if it starts with '!' or a mention prefix
        if (!(message.HasCharPrefix(_commandPrefix, ref argPos) || message.HasMentionPrefix(_client.CurrentUser, ref argPos)))
            return;
        // Create a Command Context
        var context = new CommandContext(_client, message);
        // Execute the command. (result does not indicate a return value,
        // rather an object stating if the command executed successfully)
        var result = await _commandService.ExecuteAsync(context, argPos, _services);

        if (result.IsSuccess)
        {
            AddToCommandHistory(message);
            return;
        }

        // If the whole message is only ! or ? or space
        if (message.Content.All(letter => letter is '!' or '?' or ' '))
            return;

        var resultString = result.ErrorReason;
        if (result is PreconditionGroupResult groupResult)
        {
            resultString = groupResult.PreconditionResults.First().ErrorReason;

            // Pre-condition doesn't have a reason, we don't respond.
            if (resultString == string.Empty)
                return;
        }
        
        AddToCommandHistory(message, resultString);
        await context.Channel.SendMessageAsync(resultString).DeleteAfterSeconds(10);
    }

    private async Task HandleInteraction(SocketInteraction arg)
    {
        try
        {
            // Execute the command by creating a context for the command to execute on.
            var ctx = new SocketInteractionContext(_client, arg);
            // Execute the command and retrieve the result.
            IResult result = await _interactionService.ExecuteCommandAsync(ctx, _services);
            //TODO maybe do something if result is anything but success
            
            // TODO: (James) Need to "AddToCommandHistory" for interactions
        }
        catch (Exception ex)
        {
            LoggingService.LogToConsole(ex.ToString(), LogSeverity.Error);
        }
    }
    
    public void AddToCommandHistory(SocketUserMessage message, string error = default)
    {
        _commandHistory.Add(new CommandHistoryInfo()
        {
            Command = message.Content,
            User = message.Author.Username,
            UserId = message.Author.Id,
            Channel = message.Channel.Name,
            Error = error == null ? error : string.Empty,
            Time = DateTime.Now
        });
        if (_commandHistory.Count > MaxCommandHistory)
            _commandHistory.RemoveAt(0);
    }
    
    public async Task<string> GetCommandHistory(int count = 10)
    {
        if (count > _commandHistory.Count)
            count = _commandHistory.Count;
        if (count == 0)
            count = 10;
        
        var commandHistory = new StringBuilder();
        for (var i = _commandHistory.Count - 1; i >= 0 && count > 0; i--, count--)
        {
            var command = _commandHistory[i];
            commandHistory.AppendLine($"{command.Time} - {command.User}[{command.UserId}] used {command.Command} in {command.Channel} {(string.IsNullOrEmpty(command.Error) ? string.Empty : $"Error: {command.Error}")}");
        }
        return commandHistory.ToString();
    }
}