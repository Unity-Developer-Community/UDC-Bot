using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace DiscordBot.Services
{
    public class RaidProtectionService
    {
        public bool IsLockDownEnabled { get; private set; } = false;

        private readonly DiscordSocketClient _client;
        private readonly ILoggingService _loggingService;
        // Settings
        private readonly Settings.Deserialized.Settings _settings;   
        private readonly Settings.Deserialized.RaidProtection _raidSettings;

        private string _overridenKickMessage = string.Empty;
        private DateTime _overridenEndTime = DateTime.Now.AddSeconds(-30);

        private DateTime _lastJoinDate = DateTime.Now;
        private DateTime _raidStartTime;
        private int _usersInRaidCount = 0;
        private List<SocketGuildUser> _usersInRaid = new List<SocketGuildUser>();
        
        public RaidProtectionService(DiscordSocketClient client, ILoggingService logging, Settings.Deserialized.RaidProtection raidSettings, Settings.Deserialized.Settings settings)
        {
            _client = client;
            _settings = settings;
            _raidSettings = raidSettings;
            _loggingService = logging;
            
            // Event Subscriptions
            _client.UserJoined += UserJoined;
        }

        private async Task UserJoined(SocketGuildUser user)
        {
            // ulong general = _settings.GeneralChannel.Id;
            SocketTextChannel socketTextChannel = null; // _client.GetChannel(general) as SocketTextChannel;
            //TODO The above could allow us to delete the welcome message
            
            // If we're in manual override mode
            if (DateTime.Now < _overridenEndTime && IsLockDownEnabled)
            {
                await ProcessKick(user);
                return;
            }
            // Otherwise check if lastJoinDate is longer than the shutoff period
            else if ((DateTime.Now - _lastJoinDate).TotalSeconds > _raidSettings.MaxJoinSeconds)
            {
                await DisableLockdown();
                return;
            }
            await ProcessKick(user);
        }

        private async Task ProcessKick(SocketGuildUser user)
        {
            // Add the current user to usersInRaid, increase _usersInRaid by 1 and update lastJoinDate to currentTime.
            _usersInRaidCount++;
            _usersInRaid.Add(user);
            _lastJoinDate = DateTime.Now;
            // Check the if the number of users inside usersInRaid is bigger than Y [joinMaxNewUsers]
            //      ==> If True, kick all users inside usersInRaid and remove them from the list as they are kicked.
            if (_usersInRaid.Count > _raidSettings.MaxNewUsers || IsLockDownEnabled)
            {
                if (IsLockDownEnabled == false)
                {
                    _raidStartTime = DateTime.Now;
                }
                // Since we need to reach a number before we start kicking, our first kick contains a group, afterwards we just kick them as they join to reduce odds of messaging users.
                IsLockDownEnabled = true;
                // spin up a new task so the GateWay event for this can finish and we don't get GateWay limited
                await Task.Run(async () => await CrudeRaidKicker(new List<SocketGuildUser>(_usersInRaid)));

                _usersInRaid.Clear();
            }
        }
        
        private async Task CrudeRaidKicker(List<SocketGuildUser> raiders)
        {
            for (int i = 0; i <= raiders.Count - 1; i++)
            {
                var raider = raiders[i];
                try
                {
                    // This can fail, so we have to catch that.
                    // We check if we have a custom message, otherwise give it the normal one.
                    if (_overridenKickMessage != string.Empty)
                    {
                        await raider.SendMessageAsync(_overridenKickMessage);
                    }
                    else
                    {
                        await raider.SendMessageAsync(_raidSettings.KickMessage);
                    }
                }
                catch (Exception)
                {
                    // await _loggingService.LogAction($"{raidProtectLine} Failed to notify user of kick {raider.user.Mention}", false);
                }
                if (_overridenKickMessage != string.Empty)
                    await raider.KickAsync(_overridenKickMessage);
                else
                    await raider.KickAsync(_raidSettings.KickMessage);
                
                await _loggingService.LogAction($"{_raidSettings.RaidProtectionIdentifier} {raider.Mention} has been kicked due to lockdown.");
            }
            raiders.Clear();
        }

        public async Task DisableLockdown()
        {
            if (IsLockDownEnabled == true && _usersInRaidCount > 0) {
                await _loggingService.LogAction(
                    $"{_raidSettings.RaidProtectionIdentifier} {_usersInRaidCount} users were kicked over {(DateTime.Now - _raidStartTime).Seconds} seconds before resuming regular operations.");
            }
            _usersInRaidCount = 0;
            IsLockDownEnabled = false;
            _usersInRaid.Clear();
            _overridenKickMessage = string.Empty;
            _overridenEndTime = DateTime.Now.AddSeconds(-10);
        }

        public void EnableLockdown(int duration, string kickMessage)
        {
            if (duration > _raidSettings.MaxManualLockDownDuration)
                duration = _raidSettings.MaxManualLockDownDuration;

            if (kickMessage != string.Empty)
                _overridenKickMessage = kickMessage;

            IsLockDownEnabled = true;
            _raidStartTime = DateTime.Now;
            _overridenEndTime = DateTime.Now.AddSeconds(duration);
        }
    }
}