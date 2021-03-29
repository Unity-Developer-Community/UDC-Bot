using Discord;
using Discord.WebSocket;
using DiscordBot.Extensions;
using DiscordBot.Settings.Deserialized;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace DiscordBot.Services
{
    public class ReactRoleService
    {
        private const string ReactionSettingsPath = @"Settings/ReactionRoles.json";

        public ReactRoleSettings ReactSettings;

        private bool _isRunning = false;

        private readonly Settings.Deserialized.Settings _settings;
        private readonly DiscordSocketClient _client;
        private readonly ILoggingService _loggingService;

        // Dictionaries to simplify lookup
        private readonly Dictionary<ulong, IUserMessage> _reactMessages = new Dictionary<ulong, IUserMessage>();
        // GuildRoles uses EmojiID as Key
        private readonly Dictionary<ulong, IRole> _guildRoles = new Dictionary<ulong, IRole>();
        private readonly Dictionary<ulong, GuildEmote> _guildEmotes = new Dictionary<ulong, GuildEmote>();

        private readonly Dictionary<IGuildUser, ReactRoleUserData> _pendingUserUpdate = new Dictionary<IGuildUser, ReactRoleUserData>();

        public class ReactRoleUserData
        {
            public IGuildUser User;
            public DateTime LastChange = DateTime.Now;
            public List<IRole> RolesToAdd = new List<IRole>();
            public List<IRole> RolesToRemove = new List<IRole>();
            public ReactRoleUserData(IGuildUser id)
            {
                User = id;
            }
        }

        // These are for the Modules to reference if/when setting up new message roles.
        public bool IsPreparingMessage => NewMessage != null;

        public UserReactMessage NewMessage;

        public ReactRoleService(DiscordSocketClient client, ILoggingService logging, Settings.Deserialized.Settings settings)
        {
            _loggingService = logging;
            _settings = settings;

            _client = client;
            _client.ReactionAdded += ReactionAdded;
            _client.ReactionRemoved += ReactionRemoved;

            // Event so we can Initialize
            _client.Ready += ClientIsReady;
        }
        /// <summary>
        /// Loads settings, this should just be message ids, and emotes/role ids
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                // If file doesn't exist, we make an empty file with the default values.
                if (!File.Exists(ReactionSettingsPath))
                {
                    var reactSettings = new ReactRoleSettings();
                    var settingsContent = JsonConvert.SerializeObject(value: reactSettings, formatting: Formatting.Indented);
                    File.WriteAllText(path: ReactionSettingsPath, contents: settingsContent);
                }
                else
                {
                    using var file = File.OpenText(ReactionSettingsPath);
                    ReactSettings = JsonConvert.DeserializeObject<ReactRoleSettings>(file.ReadToEnd());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to Deserialize 'ReactionRoles.Json' err: {ex.Message}");
                _isRunning = false;
            }
        }

        private bool SaveSettings()
        {
            try
            {
                string settingsContent = JsonConvert.SerializeObject(value: ReactSettings, formatting: Formatting.Indented);
                File.WriteAllText(path: ReactionSettingsPath, contents: settingsContent);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to Serialize 'ReactionRoles.Json' err: {ex.Message}");
                _isRunning = false;
            }

            return false;
        }

        private async Task<bool> StartService()
        {
            LoadSettings();

            if (ReactSettings.UserReactRoleList == null)
            {
                _isRunning = true;
                return true;
            }

            // Get our Emotes
            var serverGuild = _client.GetGuild(_settings.guildId);
            foreach (var reactMessage in ReactSettings.UserReactRoleList)
            {
                // Channel used for message
                var messageChannel = _client.GetChannel(reactMessage.ChannelId) as IMessageChannel;
                // Get The Message for this group of reactions
                if (!_reactMessages.ContainsKey(reactMessage.MessageId))
                { 
                    _reactMessages.Add(reactMessage.MessageId, await messageChannel.GetMessageAsync(reactMessage.MessageId) as IUserMessage);
                }
                for (int i = 0; i < reactMessage.RoleCount(); i++)
                {
                    // Add a Reference to our Roles to simplify lookup
                    if (!_guildRoles.ContainsKey(reactMessage.Reactions[i].EmojiId))
                    {
                        _guildRoles.Add(reactMessage.Reactions[i].EmojiId, serverGuild.GetRole(reactMessage.Reactions[i].RoleId));
                    }
                    // Same for the Emojis, saves look-arounds
                    if (!_guildEmotes.ContainsKey(reactMessage.Reactions[i].EmojiId))
                    {
                        _guildEmotes.Add(reactMessage.Reactions[i].EmojiId, await serverGuild.GetEmoteAsync(reactMessage.Reactions[i].EmojiId));
                    }
                    // If our message doesn't have the emote, we add it.
                    if (!_reactMessages[reactMessage.MessageId].Reactions.ContainsKey(_guildEmotes[reactMessage.Reactions[i].EmojiId]))
                    {
                        // We could add these in bulk, but that'd require a bit more setup
                        await _reactMessages[reactMessage.MessageId].AddReactionAsync(_guildEmotes[reactMessage.Reactions[i].EmojiId]);
                    }
                }
            }
            _isRunning = true;
            return true;
        }

        private async Task ClientIsReady()
        {
            _isRunning = await StartService();
        }

        private async Task ReactionAdded(Cacheable<IUserMessage, ulong> message, ISocketMessageChannel channel, SocketReaction reaction)
        {
            if (!_isRunning)
                return;
            if (_reactMessages.ContainsKey(message.Id))
            {
                ReactionChanged(reaction.User.Value as IGuildUser, reaction.Emote as Emote, true);
            }
        }
        private async Task ReactionRemoved(Cacheable<IUserMessage, ulong> message, ISocketMessageChannel channel, SocketReaction reaction)
        {
            if (!_isRunning)
                return;
            if (_reactMessages.ContainsKey(message.Id))
            {
                if (_reactMessages.ContainsKey(message.Id))
                {
                    ReactionChanged(reaction.User.Value as IGuildUser, reaction.Emote as Emote, false);
                }
            }
        }
        private void ReactionChanged(IGuildUser user, Emote emote, bool state)
        {
            if (!IsUserValid(user)) return;

            if (_guildRoles.TryGetValue(emote.Id, out var targetRole))
            {
                UpdatePendingRoles(user, targetRole, state);
            }
        }

        /// <summary>
        /// Updates users roles in bulk, this prevents discord from crying.
        /// We check the last time they tried to change the role (if they've clicked multiple) to save API calls, Discord will quickly complain if we hit them with to many requests.
        /// </summary>
        private async void UpdateUserRoles(IGuildUser user)
        {
            //TODO Implement a watcher, prevent people from changing their roles every few minutes. Not super important.
            if (!_pendingUserUpdate.TryGetValue(user, out var userData)) return;

            // Wait for a bit to give user to choose all their roles.
            // we add a bit more to the end just so we don't always hit a second delay if they only selected 1 emote.
            await Task.Delay((int)ReactSettings.RoleAddDelay + 250);
            while (((DateTime.Now - userData.LastChange).TotalMilliseconds < ReactSettings.RoleAddDelay))
            {
                await Task.Delay(2000);
            }

            // Strip out any changes we don't need to prevent additional calls
            // If changes were made to either add or remove, we make those changes.
            if (userData.RolesToAdd.Count > 0)
            {
                for (int i = userData.RolesToAdd.Count - 1; i >= 0; i--)
                {
                    if (user.RoleIds.Contains(userData.RolesToAdd[i].Id))
                    {
                        userData.RolesToAdd.RemoveAt(i);
                    }
                }
                await user.AddRolesAsync(userData.RolesToAdd);
            }
            if (userData.RolesToRemove.Count > 0)
            {
                for (int i = userData.RolesToRemove.Count - 1; i >= 0; i--)
                {
                    if (user.RoleIds.Contains(userData.RolesToRemove[i].Id))
                    {
                        userData.RolesToRemove.RemoveAt(i);
                    }
                }
                await user.RemoveRolesAsync(userData.RolesToRemove);
            }
            if (ReactSettings.LogUpdates)
                await _loggingService.LogAction($"{user.Username} Updated Roles.", false, true);

            _pendingUserUpdate.Remove(user);
        }

        /// <summary>
        /// If a user is reacting to messages, they're added to a pending list of updates. Any time they react within the timeframe, it resets, and updates what roles need to be set.
        /// </summary>
        private void UpdatePendingRoles(IGuildUser user, IRole role, bool state)
        {
            // We check if the user has pending updates, if they don't we add them
            if (!_pendingUserUpdate.ContainsKey(user))
            {
                _pendingUserUpdate.Add(user, new ReactRoleUserData(user));
                UpdateUserRoles(user);
            }
            var userData = _pendingUserUpdate[user];
            userData.LastChange = DateTime.Now;
            // Add our change, make sure it isn't in our RemoveList
            if (state == true) 
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

        private bool IsUserValid(IGuildUser user)
        {
            return !user.IsBot;
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
            //TODO Maybe add a warning? or check the new changes to make sure emotes still exist?
            _pendingUserUpdate.Clear();

            await StartService();
            return _isRunning;
        }
        #endregion
    }
}
