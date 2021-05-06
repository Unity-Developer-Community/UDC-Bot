using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace DiscordBot.Services
{
    public class BotAnnouncementService
    {
        private readonly string _serviceLogName = "BotAnnouncements";
        
        private ISocketMessageChannel _announceChannel;
        private List<string> _announcements;
        
        public static string CommandList;

        private readonly Settings.Deserialized.Settings _settings;
        private readonly DiscordSocketClient _client;
        private readonly ILoggingService _loggingService;

        private string _announcementPath = @"Settings/Announcements.json";
        public BotAnnouncementService(DiscordSocketClient client, Settings.Deserialized.Settings settings, ILoggingService logging)
        {
            _settings = settings;
            _client = client;
            _announcements = SerializeUtil.DeserializeFile<List<string>>(_announcementPath);
            _loggingService = logging;
            
            _client.Ready += ClientIsReady;
        }

        private async Task ClientIsReady()
        {
            await _loggingService.LogAction($"{DateTime.Now.ToString()}: Server started.");
           
            if (_settings.UpdateNewsChannel != 0)
                _announceChannel = _client.GetChannel(_settings.UpdateNewsChannel) as ISocketMessageChannel;

            if (_announcements.Count != 0)
                await ProcessAnnouncements(clearAnnouncementsAfter: true);
            
            if (_announceChannel == null)
                ConsoleLogger.Log($"{_serviceLogName} UpdateNewsChannel not provided in settings.", Severity.Warning);
        }

        private async Task ProcessAnnouncements(bool clearAnnouncementsAfter = true, IMessageChannel ovverideChannel = null)
        {
            var usedChannel = ovverideChannel ?? _announceChannel;
            if (_announcements.Count == 0)
                return;
            if (usedChannel == null)
                return;
            
            for (int index = 0; index < _announcements.Count; index++)
            {
                await usedChannel.SendMessageAsync(_announcements[index]);
            }

            if (clearAnnouncementsAfter)
            {
                await _loggingService.LogAction($"{_serviceLogName} service posted {_announcements.Count} messages");
                _announcements.Clear();
                await SaveAnnouncements();
            }
        }

        private async Task SaveAnnouncements()
        {
            if (!SerializeUtil.SerializeFile(_announcementPath, _announcements))
            {
                await _loggingService.LogAction($"{_serviceLogName} announcement file failed to save. Contents posted below.\n");
                for (int i = 0; i < _announcements.Count; i++)
                {
                    await _loggingService.LogAction($"{_announcements[i]}");
                }
                ConsoleLogger.Log($"{_serviceLogName} failed to save the announcement file.", Severity.Error);
            }
        }

        public async Task AddAnnouncement(string contents, IUser user, bool sendNow = false)
        {
            if (!sendNow)
            {
                _announcements.Add(contents);
                await SaveAnnouncements();
                await _loggingService.LogAction($"{_serviceLogName} announcement added to service by {user}");
            }
            else
            {
                await _announceChannel.SendMessageAsync(contents);
                await _loggingService.LogAction($"{_serviceLogName} announcement sent by {user}");
            }
        }

        public async Task<bool> PreviewAnnouncements(IMessageChannel channel)
        {
            if (_announcements.Count == 0)
                return false;
            
            await ProcessAnnouncements(clearAnnouncementsAfter: false, ovverideChannel: channel);
            return true;
        }
        
        public async Task<int> ClearAnnouncements(IUser user)
        {
            int announcements = _announcements.Count;
            _announcements.Clear();
            await SaveAnnouncements();
            await _loggingService.LogAction($"{_serviceLogName} cleared by {user}");
            return announcements;
        }
    }
}