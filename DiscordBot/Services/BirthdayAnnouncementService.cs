using System.Globalization;
using Discord.WebSocket;
using DiscordBot.Components;
using DiscordBot.Settings.Options;
using DiscordBot.Utils;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

public class BirthdayAnnouncementService : IManagedBotService, IComponentHealthContributor
{
    private const string ServiceName = "BirthdayAnnouncementService";
    
    public string ComponentId => ComponentIds.BirthdayAnnouncements;
    public bool IsRunning => _loopTask is { IsCompleted: false };
    
    private readonly DiscordSocketClient _client;
    private readonly ILoggingService _loggingService;
    private readonly IBirthdaySource _birthdaySource;
    private readonly BirthdayOptions _options;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private CancellationTokenSource? _lifecycleCancellation;
    private Task? _loopTask;
    
    // Track birthdays that have been announced today to avoid spam
    private readonly HashSet<string> _announcedToday = new();
    private DateTime _lastAnnouncementDate = DateTime.Today;
    
    public BirthdayAnnouncementService(
        DiscordSocketClient client,
        ILoggingService loggingService,
        IBirthdaySource birthdaySource,
        IOptions<BirthdayOptions> options)
    {
        _client = client;
        _loggingService = loggingService;
        _birthdaySource = birthdaySource;
        _options = options.Value;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
                return;

            if (_options.AnnouncementChannelId == 0)
                throw new InvalidOperationException("BirthdayAnnouncements:AnnouncementChannelId is required.");
            if (_options.CheckIntervalMinutes <= 0)
                throw new InvalidOperationException("BirthdayAnnouncements:CheckIntervalMinutes must be greater than zero.");

            _lifecycleCancellation?.Dispose();
            _lifecycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loopTask = CheckBirthdaysLoop(_lifecycleCancellation.Token);
            await _loggingService.LogAction(
                $"[{ServiceName}] Started with {_options.CheckIntervalMinutes} minute intervals.",
                ExtendedLogSeverity.Info);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_loopTask is null)
                return;

            await _lifecycleCancellation!.CancelAsync();
            try
            {
                await _loopTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
            {
            }

            _loopTask = null;
            _lifecycleCancellation.Dispose();
            _lifecycleCancellation = null;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public Task<ComponentHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ComponentHealthSnapshot(
            IsRunning ? ComponentRuntimeState.Running : ComponentRuntimeState.Stopped,
            IsRunning ? "Birthday check loop is running." : "Birthday check loop is stopped.",
            DateTimeOffset.UtcNow));
    
    private async Task CheckBirthdaysLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Check if it's a new day and reset announced birthdays
                if (DateTime.Today > _lastAnnouncementDate)
                {
                    _announcedToday.Clear();
                    _lastAnnouncementDate = DateTime.Today;
                    _loggingService.LogAction($"[{ServiceName}] New day detected, reset announced birthdays list.", ExtendedLogSeverity.Info);
                }
                
                await CheckAndAnnounceBirthdays(cancellationToken);
                
                // Wait for the configured interval
                await Task.Delay(TimeSpan.FromMinutes(_options.CheckIntervalMinutes), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            await _loggingService.LogChannelAndFile($"[{ServiceName}] Birthday announcement service has crashed.\nException: {e.Message}", ExtendedLogSeverity.Warning);
            throw;
        }
    }
    
    private async Task CheckAndAnnounceBirthdays(CancellationToken cancellationToken)
    {
        try
        {
            var todaysBirthdays = await _birthdaySource.GetTodaysBirthdaysAsync(cancellationToken);
            
            if (todaysBirthdays.Count == 0)
            {
                return; // No birthdays today
            }
            
            var channel = _client.GetChannel(_options.AnnouncementChannelId) as SocketTextChannel;
            if (channel == null)
            {
                _loggingService.LogAction($"[{ServiceName}] Could not find birthday announcement channel with ID {_options.AnnouncementChannelId}", ExtendedLogSeverity.Warning);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _loggingService.LogAction($"[{ServiceName}] Error checking birthdays: {e.Message}", ExtendedLogSeverity.LowWarning);
        }
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
        
        var lastDigit = age % 10;
        return lastDigit switch
        {
            1 => $"{age}st",
            2 => $"{age}nd", 
            3 => $"{age}rd",
            _ => $"{age}th"
        };
    }
    
}

public class BirthdayInfo
{
    public string Name { get; set; }
    public DateTime BirthDate { get; set; }
    public int? Age { get; set; }
}

public interface IBirthdaySource
{
    Task<IReadOnlyList<BirthdayInfo>> GetTodaysBirthdaysAsync(CancellationToken cancellationToken);
}

public sealed class GoogleSheetsBirthdaySource : IBirthdaySource
{
    public async Task<IReadOnlyList<BirthdayInfo>> GetTodaysBirthdaysAsync(
        CancellationToken cancellationToken)
    {
        var birthdays = new List<BirthdayInfo>();
        var relevantNodes = await WebUtil.GetHtmlNodes(
            BirthdayTableUrl,
            "/html/body/table/tr",
            cancellationToken);
        if (relevantNodes is null)
            return birthdays;

        var today = DateTime.Today;
        foreach (var row in relevantNodes)
        {
            var name = row.SelectSingleNode("td[2]")?.InnerText?.Trim();
            var date = row.SelectSingleNode("td[1]")?.InnerText?.Trim();
            var year = row.SelectSingleNode("td[3]")?.InnerText;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(date) ||
                !TryParseBirthdayDate(date, year, today, out var birthDate) ||
                birthDate.Month != today.Month || birthDate.Day != today.Day)
            {
                continue;
            }

            birthdays.Add(new BirthdayInfo
            {
                Name = name,
                BirthDate = birthDate,
                Age = CalculateAge(birthDate, today)
            });
        }

        return birthdays;
    }

    private const string BirthdayTableUrl = "https://docs.google.com/spreadsheets/d/10iGiKcrBl1fjoBNTzdtjEVYEgOfTveRXdI5cybRTnj4/gviz/tq?tqx=out:html&gid=318080247&range=B:D";

    private static bool TryParseBirthdayDate(
        string date,
        string? year,
        DateTime today,
        out DateTime birthDate)
    {
        try
        {
            if (!string.IsNullOrEmpty(year) && !year.Contains("&nbsp;"))
            {
                birthDate = DateTime.ParseExact(
                    $"{date}/{year.Trim()}",
                    "M/d/yyyy",
                    CultureInfo.InvariantCulture);
            }
            else
            {
                var parsed = DateTime.ParseExact(date, "M/d", CultureInfo.InvariantCulture);
                birthDate = new DateTime(today.Year, parsed.Month, parsed.Day);
            }

            return true;
        }
        catch (FormatException)
        {
            birthDate = default;
            return false;
        }
    }

    private static int? CalculateAge(DateTime birthDate, DateTime today)
    {
        if (birthDate.Year == today.Year)
            return null;

        var age = today.Year - birthDate.Year;
        if (today.Month < birthDate.Month ||
            (today.Month == birthDate.Month && today.Day < birthDate.Day))
        {
            age--;
        }

        return age;
    }
}
