using System.Globalization;
using Discord.WebSocket;
using DiscordBot.Settings;
using DiscordBot.Utils;
using HtmlAgilityPack;

namespace DiscordBot.Services;

public class BirthdayAnnouncementService
{
    private const string ServiceName = "BirthdayAnnouncementService";
    
    public bool IsRunning { get; private set; }
    
    private readonly DiscordSocketClient _client;
    private readonly ILoggingService _loggingService;
    private readonly BotSettings _settings;
    private readonly DatabaseService _databaseService;
    
    // Track birthdays that have been announced today to avoid spam
    private readonly HashSet<string> _announcedToday = new();
    private DateTime _lastAnnouncementDate = DateTime.Today;
    
    public BirthdayAnnouncementService(DiscordSocketClient client, ILoggingService loggingService, BotSettings settings, DatabaseService databaseService)
    {
        _client = client;
        _loggingService = loggingService;
        _settings = settings;
        _databaseService = databaseService;
        
        Initialize();
    }
    
    private void Initialize()
    {
        if (IsRunning) return;
        
        if (!_settings.BirthdayAnnouncementEnabled)
        {
            _loggingService.LogAction($"[{ServiceName}] Birthday announcement service is disabled in settings.", ExtendedLogSeverity.Info);
            return;
        }
        
        if (_settings.BirthdayAnnouncementChannel?.Id == 0)
        {
            _loggingService.LogAction($"[{ServiceName}] Birthday announcement channel not configured.", ExtendedLogSeverity.Warning);
            return;
        }
        
        IsRunning = true;
        _loggingService.LogAction($"[{ServiceName}] Starting birthday announcement service with {_settings.BirthdayCheckIntervalMinutes} minute intervals.", ExtendedLogSeverity.Info);
        Task.Run(CheckBirthdaysLoop);
    }
    
    private async Task CheckBirthdaysLoop()
    {
        try
        {
            while (IsRunning)
            {
                // Check if it's a new day and reset announced birthdays
                if (DateTime.Today > _lastAnnouncementDate)
                {
                    _announcedToday.Clear();
                    _lastAnnouncementDate = DateTime.Today;
                    _loggingService.LogAction($"[{ServiceName}] New day detected, reset announced birthdays list.", ExtendedLogSeverity.Info);
                }
                
                await CheckAndAnnounceBirthdays();
                
                // Wait for the configured interval
                var intervalMs = _settings.BirthdayCheckIntervalMinutes * 60 * 1000;
                await Task.Delay(intervalMs);
            }
        }
        catch (Exception e)
        {
            await _loggingService.LogChannelAndFile($"[{ServiceName}] Birthday announcement service has crashed.\nException: {e.Message}", ExtendedLogSeverity.Warning);
            IsRunning = false;
        }
    }
    
    private async Task CheckAndAnnounceBirthdays()
    {
        try
        {
            var todaysBirthdays = await GetTodaysBirthdays();
            
            if (todaysBirthdays.Count == 0)
            {
                return; // No birthdays today
            }
            
            var channel = _client.GetChannel(_settings.BirthdayAnnouncementChannel.Id) as SocketTextChannel;
            if (channel == null)
            {
                _loggingService.LogAction($"[{ServiceName}] Could not find birthday announcement channel with ID {_settings.BirthdayAnnouncementChannel.Id}", ExtendedLogSeverity.Warning);
                return;
            }
            
            foreach (var birthday in todaysBirthdays)
            {
                var announcementKey = $"{birthday.UserId}-{DateTime.Today:yyyy-MM-dd}";
                
                if (_announcedToday.Contains(announcementKey))
                {
                    continue; // Already announced this birthday today
                }
                
                var message = FormatBirthdayAnnouncement(birthday);
                await channel.SendMessageAsync(message);
                
                _announcedToday.Add(announcementKey);
                _loggingService.LogAction($"[{ServiceName}] Announced birthday for {birthday.Name} (ID: {birthday.UserId})", ExtendedLogSeverity.Info);
            }
        }
        catch (Exception e)
        {
            _loggingService.LogAction($"[{ServiceName}] Error checking birthdays: {e.Message}", ExtendedLogSeverity.LowWarning);
        }
    }
    
    private async Task<List<BirthdayInfo>> GetTodaysBirthdays()
    {
        var birthdays = new List<BirthdayInfo>();
        
        try
        {
            var todaysBirthdayUsers = await _databaseService.Query.GetTodaysBirthdays();
            
            if (todaysBirthdayUsers == null || todaysBirthdayUsers.Count == 0)
            {
                return birthdays;
            }
            
            var guild = _client.GetGuild(_settings.GuildId);
            if (guild == null)
            {
                _loggingService.LogAction($"[{ServiceName}] Could not find guild with ID {_settings.GuildId}", ExtendedLogSeverity.Warning);
                return birthdays;
            }
            
            foreach (var userRecord in todaysBirthdayUsers)
            {
                if (userRecord.Birthday == null) continue;
                
                var userId = ulong.Parse(userRecord.UserID);
                var user = guild.GetUser(userId);
                
                if (user == null)
                {
                    // User may have left the guild, log but continue
                    _loggingService.LogAction($"[{ServiceName}] User {userId} not found in guild", ExtendedLogSeverity.LowWarning);
                    continue;
                }
                
                var age = CalculateAge(userRecord.Birthday.Value, DateTime.Today);
                birthdays.Add(new BirthdayInfo 
                { 
                    Name = user.DisplayName ?? user.Username, 
                    BirthDate = userRecord.Birthday.Value, 
                    Age = age,
                    UserId = userId,
                    UserMention = user.Mention
                });
            }
        }
        catch (Exception e)
        {
            _loggingService.LogAction($"[{ServiceName}] Error fetching birthday data: {e.Message}", ExtendedLogSeverity.LowWarning);
        }
        
        return birthdays;
    }
    
    private int? CalculateAge(DateTime birthDate, DateTime today)
    {
        if (birthDate.Year == 1900 || birthDate.Year == today.Year)
        {
            return null; // No year information available or invalid year
        }
        
        var age = today.Year - birthDate.Year;
        if (today.Month < birthDate.Month || (today.Month == birthDate.Month && today.Day < birthDate.Day))
        {
            age--;
        }
        
        return age;
    }
    
    private string FormatBirthdayAnnouncement(BirthdayInfo birthday)
    {
        var message = $"🎉 **Happy Birthday {birthday.UserMention}!** 🎂";
        
        if (birthday.Age.HasValue)
        {
            message += $" Hope you have a wonderful {GetAgeOrdinal(birthday.Age.Value)} birthday!";
        }
        else
        {
            message += " Hope you have a wonderful day!";
        }
        
        return message;
    }
    
    private string GetAgeOrdinal(int age)
    {
        // Handle special cases for 11th, 12th, 13th regardless of tens digit
        var lastTwoDigits = age % 100;
        if (lastTwoDigits >= 11 && lastTwoDigits <= 13)
        {
            return $"{age}th";
        }
        
        var lastDigit = age % 10;
        return lastDigit switch
        {
            1 => $"{age}st",
            2 => $"{age}nd", 
            3 => $"{age}rd",
            _ => $"{age}th"
        };
    }
    
    public async Task<bool> RestartService()
    {
        IsRunning = false;
        await Task.Delay(1000); // Give some time for the loop to exit
        Initialize();
        return IsRunning;
    }
}

public class BirthdayInfo
{
    public string Name { get; set; }
    public DateTime BirthDate { get; set; }
    public int? Age { get; set; }
    public ulong UserId { get; set; }
    public string UserMention { get; set; }
}