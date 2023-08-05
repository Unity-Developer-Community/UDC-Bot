using System.IO;
using Discord.WebSocket;
using DiscordBot.Settings;
using Newtonsoft.Json;

namespace DiscordBot.Services;

public class ReactRoleService
{
    private const string ServiceName = "ReactRoleService";
    
    private const string ReactionSettingsPath = @"Settings/ReactionRoles.json";
    private readonly DiscordSocketClient _client;
    private readonly Dictionary<ulong, GuildEmote> _guildEmotes = new Dictionary<ulong, GuildEmote>();
    // GuildRoles uses EmojiID as Key
    private readonly Dictionary<ulong, IRole> _guildRoles = new Dictionary<ulong, IRole>();
    private readonly ILoggingService _loggingService;

    private readonly Dictionary<IGuildUser, ReactRoleUserData> _pendingUserUpdate = new Dictionary<IGuildUser, ReactRoleUserData>();

    // Dictionaries to simplify lookup
    private readonly Dictionary<ulong, IUserMessage> _reactMessages = new Dictionary<ulong, IUserMessage>();

    private readonly BotSettings _settings;

    private bool _isRunning;

    public UserReactMessage NewMessage;

    public ReactRoleSettings ReactSettings;

    public ReactRoleService(DiscordSocketClient client, ILoggingService logging, BotSettings settings)
    {
        _loggingService = logging;
        _settings = settings;

        _client = client;

        if (!_settings.ReactRoleServiceEnabled)
        {
            LoggingService.LogServiceDisabled(ServiceName, nameof(_settings.ReactRoleServiceEnabled));
            return;
        }
        LoggingService.LogServiceEnabled(ServiceName);

        _client.ReactionAdded += ReactionAdded;
        _client.ReactionRemoved += ReactionRemoved;
        
        Task.Run(async () => _isRunning = await StartService());
    }

    // These are for the Modules to reference if/when setting up new message roles.
    public bool IsPreparingMessage => NewMessage != null;

    /// <summary>
    ///     Loads settings, this should just be message ids, and emotes/role ids
    /// </summary>
    private void LoadSettings()
    {
        try
        {
            // If file doesn't exist, we make an empty file with the default values.
            if (!File.Exists(ReactionSettingsPath))
            {
                var reactSettings = new ReactRoleSettings();
                var settingsContent = JsonConvert.SerializeObject(reactSettings, Formatting.Indented);
                File.WriteAllText(ReactionSettingsPath, settingsContent);
            }
            else
            {
                using var file = File.OpenText(ReactionSettingsPath);
                ReactSettings = JsonConvert.DeserializeObject<ReactRoleSettings>(file.ReadToEnd());
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogToConsole($"[{ServiceName}] Failed to Deserialize 'ReactionRoles.Json' err: {ex.Message}", LogSeverity.Error);
            _isRunning = false;
        }
    }

    private void SaveSettings()
    {
        try
        {
            var settingsContent = JsonConvert.SerializeObject(ReactSettings, Formatting.Indented);
            File.WriteAllText(ReactionSettingsPath, settingsContent);
        }
        catch (Exception ex)
        {
            LoggingService.LogToConsole($"[{ServiceName}] Failed to Serialize 'ReactionRoles.Json' err: {ex.Message}", LogSeverity.Error);
            _isRunning = false;
        }
    }

    private async Task<bool> StartService()
    {
        LoadSettings();

        // Escape early since we have nothing to process
        if (ReactSettings.UserReactRoleList == null)
        {
            _isRunning = true;
            return true;
        }

        // Get our Emotes
        var serverGuild = _client.GetGuild(_settings.GuildId);
        if (serverGuild == null)
        {
            LoggingService.LogToConsole($"[{ServiceName}] ReactRoleService failed to start, could not return guild information.", LogSeverity.Warning);
            await _loggingService.LogAction($"[{ServiceName}] ReactRoleService failed to start.");
            return false;
        }

        for (var messageIndex = 0; messageIndex < ReactSettings.UserReactRoleList.Count; messageIndex++)
        {
            var reactMessage = ReactSettings.UserReactRoleList[messageIndex];
            // Channel used for message
            var messageChannel = _client.GetChannel(reactMessage.ChannelId) as IMessageChannel;
            if (messageChannel == null)
            {
                LoggingService.LogToConsole($"[{ServiceName}] ReactRoleService: Channel {reactMessage.ChannelId} does not exist.", LogSeverity.Warning);
                continue;
            }

            // Get The Message for this group of reactions
            if (!_reactMessages.ContainsKey(reactMessage.MessageId)) _reactMessages.Add(reactMessage.MessageId, await messageChannel.GetMessageAsync(reactMessage.MessageId) as IUserMessage);
            for (var i = 0; i < reactMessage.RoleCount(); i++)
            {
                // We check if emote exists
                var emote = serverGuild.Emotes.First(guildEmote => guildEmote.Id == reactMessage.Reactions[i].EmojiId);

                // Add a Reference to our Roles to simplify lookup
                if (!_guildRoles.ContainsKey(reactMessage.Reactions[i].EmojiId)) _guildRoles.Add(reactMessage.Reactions[i].EmojiId, serverGuild.GetRole(reactMessage.Reactions[i].RoleId));
                // Same for the Emojis, saves look-arounds
                if (!_guildEmotes.ContainsKey(reactMessage.Reactions[i].EmojiId)) _guildEmotes.Add(reactMessage.Reactions[i].EmojiId, emote);
                // If our message doesn't have the emote, we add it.
                if (!_reactMessages[reactMessage.MessageId].Reactions.ContainsKey(_guildEmotes[reactMessage.Reactions[i].EmojiId]))
                {
                    LoggingService.LogToConsole($"[{ServiceName}] Added Reaction to Message {reactMessage.MessageId} which was missing.", LogSeverity.Info);
                    // We could add these in bulk, but that'd require a bit more setup
                    await _reactMessages[reactMessage.MessageId].AddReactionAsync(emote);
                }
            }
        }

        _isRunning = true;
        return true;
    }

    private async Task ReactionAdded(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        if (!_isRunning)
            return;
        if (_reactMessages.ContainsKey(message.Id)) await ReactionChangedAsync(reaction.User.Value as IGuildUser, reaction.Emote as Emote, true);
    }

    private async Task ReactionRemoved(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        if (!_isRunning)
            return;
        if (_reactMessages.ContainsKey(message.Id))
            if (_reactMessages.ContainsKey(message.Id))
                await ReactionChangedAsync(reaction.User.Value as IGuildUser, reaction.Emote as Emote, false);
    }

    private async Task ReactionChangedAsync(IGuildUser user, Emote emote, bool state)
    {
        if (!IsUserValid(user)) return;

        if (_guildRoles.TryGetValue(emote.Id, out var targetRole)) await UpdatePendingRolesAsync(user, targetRole, state);
    }

    /// <summary>
    ///     Updates users roles in bulk, this prevents discord from crying.
    ///     We check the last time they tried to change the role (if they've clicked multiple) to save API calls, Discord will
    ///     quickly complain if we hit them with to many requests.
    /// </summary>
    private async Task UpdateUserRoles(IGuildUser user)
    {
        //TODO Implement a watcher, prevent people from changing their roles every few minutes. Not super important.
        if (!_pendingUserUpdate.TryGetValue(user, out var userData)) return;

        // Wait for a bit to give user to choose all their roles.
        // we add a bit more to the end just so we don't always hit a second delay if they only selected 1 emote.
        await Task.Delay((int)ReactSettings.RoleAddDelay + 250);
        while ((DateTime.Now - userData.LastChange).TotalMilliseconds < ReactSettings.RoleAddDelay) await Task.Delay(2000);

        // Strip out any changes we don't need to prevent additional calls
        // If changes were made to either add or remove, we make those changes.
        if (userData.RolesToAdd.Count > 0)
        {
            for (var i = userData.RolesToAdd.Count - 1; i >= 0; i--)
                if (user.RoleIds.Contains(userData.RolesToAdd[i].Id))
                    userData.RolesToAdd.RemoveAt(i);
            await user.AddRolesAsync(userData.RolesToAdd);
        }

        if (userData.RolesToRemove.Count > 0)
        {
            for (var i = userData.RolesToRemove.Count - 1; i >= 0; i--)
                if (!user.RoleIds.Contains(userData.RolesToRemove[i].Id))
                    userData.RolesToRemove.RemoveAt(i);
            await user.RemoveRolesAsync(userData.RolesToRemove);
        }

        if (ReactSettings.LogUpdates)
            await _loggingService.LogAction($"[{ServiceName}] {user.Username} Updated Roles.", false);

        _pendingUserUpdate.Remove(user);
    }

    /// <summary>
    ///     If a user is reacting to messages, they're added to a pending list of updates. Any time they react within the
    ///     timeframe, it resets, and updates what roles need to be set.
    /// </summary>
    private async Task UpdatePendingRolesAsync(IGuildUser user, IRole role, bool state)
    {
        // We check if the user has pending updates, if they don't we add them
        if (!_pendingUserUpdate.ContainsKey(user))
        {
            _pendingUserUpdate.Add(user, new ReactRoleUserData());
            await UpdateUserRoles(user);
        }

        var userData = _pendingUserUpdate[user];
        userData.LastChange = DateTime.Now;
        // Add our change, make sure it isn't in our RemoveList
        if (state)
        {
            userData.RolesToAdd.Add(role);
            userData.RolesToRemove.Remove(role);
        }
        else
        {
            userData.RolesToAdd.Remove(role);
            userData.RolesToRemove.Add(role);
        }
    }

    private bool IsUserValid(IUser user) => !user.IsBot;

    private class ReactRoleUserData
    {
        public readonly List<IRole> RolesToAdd = new List<IRole>();
        public readonly List<IRole> RolesToRemove = new List<IRole>();
        public DateTime LastChange = DateTime.Now;
    }

    #region ModuleCommands

    public bool SetReactRoleDelay(uint delay)
    {
        if (ReactSettings == null) return false;
        ReactSettings.RoleAddDelay = delay;
        SaveSettings();
        return true;
    }

    public bool SetReactLogState(bool state)
    {
        ReactSettings.LogUpdates = state;
        SaveSettings();
        return true;
    }

    /// <summary> Saves the prepared message if one is being prepared. </summary>
    public bool StoreNewMessage()
    {
        if (!IsPreparingMessage)
            return false;

        // Make sure it isn't null (New config)
        ReactSettings.UserReactRoleList ??= new List<UserReactMessage>();

        ReactSettings.UserReactRoleList.Add(NewMessage);
        SaveSettings();

        NewMessage = null;

        return true;
    }

    /// <summary> Restarts the Service by clearing all containers and restoring them from the configuration file. </summary>
    public async Task<bool> Restart()
    {
        _isRunning = false;
        _reactMessages.Clear();
        _guildRoles.Clear();
        _guildEmotes.Clear();
        //TODO This may have users in it, we could push changes before the restart?
        _pendingUserUpdate.Clear();

        await StartService();
        return _isRunning;
    }

    #endregion
}