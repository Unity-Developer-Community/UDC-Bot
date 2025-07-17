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
    
    // Track birthdays that have been announced today to avoid spam
    private readonly HashSet<string> _announcedToday = new();
    private DateTime _lastAnnouncementDate = DateTime.Today;
    
    // URLs for birthday data from the existing !bday command
    private const string NextBirthdayUrl = "https://docs.google.com/spreadsheets/d/10iGiKcrBl1fjoBNTzdtjEVYEgOfTveRXdI5cybRTnj4/gviz/tq?tqx=out:html&range=C15:C15";
    private const string BirthdayTableUrl = "https://docs.google.com/spreadsheets/d/10iGiKcrBl1fjoBNTzdtjEVYEgOfTveRXdI5cybRTnj4/gviz/tq?tqx=out:html&gid=318080247&range=B:D";
    
    public BirthdayAnnouncementService(DiscordSocketClient client, ILoggingService loggingService, BotSettings settings)
    {
        _client = client;
        _loggingService = loggingService;
        _settings = settings;
        
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
                var announcementKey = $"{birthday.Name}-{DateTime.Today:yyyy-MM-dd}";
                
                if (_announcedToday.Contains(announcementKey))
                {
                    continue; // Already announced this birthday today
                }
                
                var message = FormatBirthdayAnnouncement(birthday);
                await channel.SendMessageAsync(message);
                
                _announcedToday.Add(announcementKey);
                _loggingService.LogAction($"[{ServiceName}] Announced birthday for {birthday.Name}", ExtendedLogSeverity.Info);
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
            var relevantNodes = await WebUtil.GetHtmlNodes(BirthdayTableUrl, "/html/body/table/tr");
            if (relevantNodes == null)
            {
                return birthdays;
            }
            
            var today = DateTime.Today;
            
            foreach (var row in relevantNodes)
            {
                var nameNode = row.SelectSingleNode("td[2]");
                var dateNode = row.SelectSingleNode("td[1]");
                var yearNode = row.SelectSingleNode("td[3]");
                
                if (nameNode == null || dateNode == null) continue;
                
                var name = nameNode.InnerText?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                
                var dateString = dateNode.InnerText?.Trim();
                if (string.IsNullOrEmpty(dateString)) continue;
                
                // Try to parse the birthday date
                if (TryParseBirthdayDate(dateString, yearNode?.InnerText, out var birthDate))
                {
                    // Check if this birthday is today (ignoring year)
                    if (birthDate.Month == today.Month && birthDate.Day == today.Day)
                    {
                        var age = CalculateAge(birthDate, today);
                        birthdays.Add(new BirthdayInfo { Name = name, BirthDate = birthDate, Age = age });
                    }
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogAction($"[{ServiceName}] Error fetching birthday data: {e.Message}", ExtendedLogSeverity.LowWarning);
        }
        
        return birthdays;
    }
    
    private bool TryParseBirthdayDate(string dateString, string yearString, out DateTime birthDate)
    {
        birthDate = default;
        
        try
        {
            var provider = CultureInfo.InvariantCulture;
            
            // Add year if available and not empty
            if (!string.IsNullOrEmpty(yearString) && !yearString.Contains("&nbsp;"))
            {
                dateString = $"{dateString}/{yearString.Trim()}";
                birthDate = DateTime.ParseExact(dateString, "M/d/yyyy", provider);
            }
            else
            {
                // Parse without year, assume current year for calculation
                var tempDate = DateTime.ParseExact(dateString, "M/d", provider);
                birthDate = new DateTime(DateTime.Today.Year, tempDate.Month, tempDate.Day);
            }
            
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
    
    private int? CalculateAge(DateTime birthDate, DateTime today)
    {
        if (birthDate.Year == today.Year)
        {
            return null; // No year information available
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
        var message = $"🎉 **Happy Birthday {birthday.Name}!** 🎂";
        
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
        
        return age % 10 switch
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
}