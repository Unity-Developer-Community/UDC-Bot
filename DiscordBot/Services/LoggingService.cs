using System.Diagnostics;
using System.IO;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Logging;

#region Extended Log Severity

// We use DNets built in severity levels, but we add a few more for internal logging.
public enum ExtendedLogSeverity
{
    Critical = LogSeverity.Critical,
    Error = LogSeverity.Error,
    Warning = LogSeverity.Warning,
    Info = LogSeverity.Info,
    Verbose = LogSeverity.Verbose,
    Debug = LogSeverity.Debug,
    // Extended levels
    Positive = 10, // Positive(info) is green in the console
    LowWarning = 11, // LowWarning(warning) is yellow in the console
}

public static class ExtendedLogSeverityExtensions
{
    public static LogSeverity ToLogSeverity(this ExtendedLogSeverity severity)
    {
        return severity switch
        {
            ExtendedLogSeverity.Positive => LogSeverity.Info,
            ExtendedLogSeverity.LowWarning => LogSeverity.Warning,
            _ => (LogSeverity)severity
        };
    }
    
    public static ExtendedLogSeverity ToExtended(this LogSeverity severity)
    {
        return (ExtendedLogSeverity)severity;
    }
    
}

#endregion // Extended Log Severity

public class LoggingService : ILoggingService
{
    private readonly DiscordSocketClient _client;

    private readonly BotSettings _settings;

    public LoggingService(DiscordSocketClient client, BotSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task LogAction(string action, bool logToFile = true, bool logToChannel = true, Embed embed = null)
    {
        if (logToChannel)
        {
            var channel = _client.GetChannel(_settings.BotAnnouncementChannel.Id) as ISocketMessageChannel;
            await channel.SendMessageAsync(action, false, embed);
        }

        if (logToFile)
            await File.AppendAllTextAsync(_settings.ServerRootPath + @"/log.txt",
                $"[{ConsistentDateTimeFormat()}] {action} {Environment.NewLine}");
    }

    public void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp)
    {
        File.AppendAllText(_settings.ServerRootPath + @"/logXP.txt",
            $"[{ConsistentDateTimeFormat()}] - {user} gained {totalXp}xp (base: {baseXp}, bonus : {bonusXp}, reduce : {xpReduce}) in channel {channel} {Environment.NewLine}");
    }

    // Returns DateTime.Now in format: d/M/yy HH:mm:ss
    public static string ConsistentDateTimeFormat()
    {
        return DateTime.Now.ToString("d/M/yy HH:mm:ss");
    }

    // Logs DiscordNet specific messages, this shouldn't be used for normal logging
    public static Task DiscordNetLogger(LogMessage message)
    {
        LoggingService.LogToConsole($"{message.Source} | {message.Message}", message.Severity.ToExtended());
        return Task.CompletedTask;
    }
    #region Console Messages
    // Logs message to console without changing the colour
    public static void LogConsole(string message) {
        Console.WriteLine($"[{ConsistentDateTimeFormat()}] {message}");
    }

    public static void LogToConsole(string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info) 
    {
        ConsoleColor restoreColour = Console.ForegroundColor;
        SetConsoleColour(severity);

        Console.WriteLine($"[{ConsistentDateTimeFormat()}] {message} [{severity}]");

        Console.ForegroundColor = restoreColour;
    }
    public static void LogToConsole(string message, LogSeverity severity) => LogToConsole(message, severity.ToExtended());
    
    public static void LogServiceDisabled(string service, string varName)
    {
        LogToConsole($"Service \"{service}\" is Disabled, {varName} is false in settings.json", ExtendedLogSeverity.Warning);
    }
    
    public static void LogServiceEnabled(string service)
    {
        LogToConsole($"Service \"{service}\" is Enabled", ExtendedLogSeverity.Info);
    }

    /// <summary>
    /// Same behaviour as LogToConsole, however this method is not included in the release build.
    /// Good if you need more verbose but obvious logging, but don't want it included in release.
    /// </summary>
    [Conditional("DEBUG")]
    public static void DebugLog(string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info)
    {
        LogToConsole(message, severity);
    }
    [Conditional("DEBUG")]
    public static void DebugLog(string message, LogSeverity severity) => DebugLog(message, severity.ToExtended());

    private static void SetConsoleColour(ExtendedLogSeverity severity)
    {
        switch (severity)
        {
            case ExtendedLogSeverity.Critical:
            case ExtendedLogSeverity.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                break;
            case ExtendedLogSeverity.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                break;
            case ExtendedLogSeverity.Info:
                Console.ForegroundColor = ConsoleColor.White;
                break;
            case ExtendedLogSeverity.Positive:
                Console.ForegroundColor = ConsoleColor.Green;
                break;
            case ExtendedLogSeverity.LowWarning:
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                break;
            // case ExtendedLogSeverity.Verbose:
            // case ExtendedLogSeverity.Debug:
            default:
                Console.ForegroundColor = ConsoleColor.DarkGray;
                break;
        }
    }
    #endregion
} 

public interface ILoggingService
{
    Task LogAction(string action, bool logToFile = true, bool logToChannel = true, Embed embed = null);
    void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp);
}