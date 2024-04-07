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

/// <summary>
/// An enum for specifying which logging behaviour to use. Can be combined with bitwise OR.
/// </summary>
/// <remarks>
/// When adding new behaviours, ensure that the value is a power of 2 (1, 2, 4, 8, 16, etc).
/// Do not add a "ALL" value, as this could be dangerous for future additions depending on how it's used.
/// If adding behaviours, maybe rename `LogAction` to avoid confusion unless there is no chance of ambiguity or conflict.
/// </remarks>
[Flags]
public enum LogBehaviour
{
    None = 0,
    Console = 1,
    Channel = 2,
    File = 4,
    CommandFile = 8,
    // Common combinations
    ChannelAndFile = Channel | File,
    ConsoleChannelAndFile = Console | Channel | File,
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
    private const string ServiceName = "LoggingService";
    
    private readonly ISocketMessageChannel _logChannel;
    
    // Configuration
    private const long MaxLogSize = 1024 * 1024 * 2; // 2MB
    private const long FileCheckInterval = 1000 * 60 * 60 * 1; // 1 Hour
    private readonly bool _logCommandExecutions;
    
    // Where backup files go
    private readonly string _backupLogFilePath;
    
    private readonly string _logFilePath; // Normal Logs
    private readonly string _logXpFilePath; // XP Logs

    private DateTime _lastFileCheck;

    public LoggingService(DiscordSocketClient client, BotSettings settings)
    {
        _logCommandExecutions = settings.LogCommandExecutions;
        
        // Paths
        _backupLogFilePath = settings.ServerRootPath + @"/log_backups/";
        _logFilePath = settings.ServerRootPath + @"/log.txt";
        _logXpFilePath = settings.ServerRootPath + @"/logXP.txt";

        if (!Directory.Exists(_backupLogFilePath))
        {
            Directory.CreateDirectory(_backupLogFilePath);
            LogToConsole($"[{ServiceName}] Created backup log directory", ExtendedLogSeverity.Info);
        }

        // INIT
        if (settings.BotAnnouncementChannel == null)
        {
            LogToConsole($"[{ServiceName}] Error: Logging Channel not set in settings.json", LogSeverity.Error);
            return;
        }
        _logChannel = client.GetChannel(settings.BotAnnouncementChannel.Id) as ISocketMessageChannel;
        if (_logChannel == null)
        {
            LogToConsole($"[{ServiceName}] Error: Logging Channel {settings.BotAnnouncementChannel.Id} not found", LogSeverity.Error);
        }
    }
    
    public async Task Log(LogBehaviour behaviour, string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info, Embed embed = null)
    {
        if (behaviour.HasFlag(LogBehaviour.Console))
            LogToConsole(message, severity);
        if (behaviour.HasFlag(LogBehaviour.Channel))
            await LogToChannel(message, severity, embed);
        if (behaviour.HasFlag(LogBehaviour.File))
            await LogToFile(message, severity);
        if (_logCommandExecutions && behaviour.HasFlag(LogBehaviour.CommandFile))
            await LogToFile(message, severity);
    }
    
    public async Task LogToChannel(string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info, Embed embed = null)
    {
        if (_logChannel == null)
            return;
        await _logChannel.SendMessageAsync(message, false, embed);
    }
    
    public async Task LogToFile(string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info)
    { 
        PrepareLogFile(_logFilePath);
        await File.AppendAllTextAsync(_logFilePath,
            $"[{ConsistentDateTimeFormat()}] - [{severity}] - {message} {Environment.NewLine}");
    }
    
    public void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp)
    {
        PrepareLogFile(_logXpFilePath);
        File.AppendAllText(_logXpFilePath,
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
        LogToConsole($"{message.Source} | {message.Message}", message.Severity.ToExtended());
        return Task.CompletedTask;
    }

    private void PrepareLogFile(string path)
    {
        if (DateTime.Now - _lastFileCheck < TimeSpan.FromMilliseconds(FileCheckInterval))
            return;
        
        _lastFileCheck = DateTime.Now;
        if (new FileInfo(path).Length > MaxLogSize)
        {
            // Rename the file, add the year, month and day it was created, and the year month and day it was backed up (SHORT year
            var backupPath = $"{_backupLogFilePath}log_F{File.GetCreationTime(path):yyMMdd}_T{DateTime.Now:yyMMdd}.txt";
            File.Move(path, backupPath);
            LogToConsole($"[{ServiceName}] Log file was backed up to {backupPath}", ExtendedLogSeverity.Info);
        }
        
        if (!File.Exists(path))
        {
            File.Create(path).Dispose();
            File.AppendAllText(path, $"[{ConsistentDateTimeFormat()}] - Log file was started. {Environment.NewLine}");
            LogToConsole($"[{ServiceName}] Log file was started", ExtendedLogSeverity.Info);
        }
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
        LogToConsole($"Service \"{service}\" is Disabled, {varName} is false in settings.json", ExtendedLogSeverity.LowWarning);
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

/// <summary>
/// Interface for the LoggingService, this is only really required if you want to use DI.
/// Logging to console and file is still available without this through the static methods.
/// </summary>
/// <remarks>
/// There is also DebugLog (LoggingService), which is only included in debug builds which is useful for more verbose logging during development.
/// </remarks>
public interface ILoggingService
{
    void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp);
    
    /// <summary>
    /// Standard logging, this will log to console, channel and file depending on the behaviour.
    /// </summary>
    /// <param name="behaviour">Where logs go, Console, Channel, File (Or some combination)</param>
    /// <param name="message">Message</param>
    /// <param name="severity">Info, Error, Warn, etc (Included in File and Console logging)</param>
    /// <param name="embed">Embed, only used by Channel Logging</param>
    Task Log(LogBehaviour behaviour, string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info, Embed embed = null);

    /// <summary>
    /// 'Short hand' for logging to all CURRENT supported behaviours, console, channel and file.
    /// Same as calling `Log(LogBehaviour.ConsoleChannelAndFile, message, severity, embed);`
    /// </summary>
    Task LogAction(string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info, Embed embed = null) => 
        Log(LogBehaviour.ConsoleChannelAndFile, message, severity, embed);
    
    /// <summary>
    /// 'Short hand' for logging to channel and file.
    /// Same as calling `Log(LogBehaviour.ChannelAndFile, message, severity, embed);`
    /// </summary>
    Task LogChannelAndFile(string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info, Embed embed = null) => 
        Log(LogBehaviour.ChannelAndFile, message, severity, embed);
}