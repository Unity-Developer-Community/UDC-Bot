using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Discord.WebSocket;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

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

    private readonly DiscordSocketClient _client;
    private readonly ulong _logChannelId;

    // Configuration
    private const long MaxLogSize = 1024 * 1024 * 2; // 2MB
    private readonly bool _logCommandExecutions;

    // Where backup files go
    private readonly string _backupLogFilePath;

    private readonly string _logFilePath; // Normal Logs
    private readonly string _logXpFilePath; // XP Logs

    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private static readonly object ConsoleLock = new();

    public LoggingService(
        DiscordSocketClient client,
        IOptions<LoggingOptions> loggingOptions,
        IOptions<StorageOptions> storageOptions)
    {
        _client = client;
        var logging = loggingOptions.Value;
        var storage = storageOptions.Value;
        _logCommandExecutions = logging.LogCommandExecutions;

        // Paths
        _backupLogFilePath = storage.ServerRootPath + @"/log_backups/";
        _logFilePath = storage.ServerRootPath + @"/log.txt";
        _logXpFilePath = storage.ServerRootPath + @"/logXP.txt";

        // INIT
        _logChannelId = logging.AnnouncementChannelId;
        if (_logChannelId == 0)
        {
            LogToConsole($"[{ServiceName}] Error: Logging Channel not set in settings.json", LogSeverity.Error);
            return;
        }
    }

    public async Task Log(LogBehaviour behaviour, string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info, Embed embed = null)
    {
        if (behaviour.HasFlag(LogBehaviour.Console))
            LogToConsole(message, severity);
        if (behaviour.HasFlag(LogBehaviour.File) ||
            (_logCommandExecutions && behaviour.HasFlag(LogBehaviour.CommandFile)))
            await LogToFile(message, severity);
        if (behaviour.HasFlag(LogBehaviour.Channel))
        {
            try { await LogToChannel(message, severity, embed); }
            catch (Exception exception) { LogExceptionToConsole(exception, "Failed to send log to Discord"); }
        }
    }

    public async Task LogToChannel(string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info, Embed embed = null)
    {
        var logChannel = _client.GetChannel(_logChannelId) as ISocketMessageChannel;
        if (logChannel == null)
            return;
        await logChannel.SendMessageAsync(message, false, embed);
    }

    public async Task LogToFile(string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info)
    {
        await _fileLock.WaitAsync();
        try
        {
            PrepareLogFile(_logFilePath);
            await File.AppendAllTextAsync(_logFilePath,
                $"[{ConsistentDateTimeFormat()}] - [{severity}] - {message} {Environment.NewLine}");
        }
        catch (Exception exception)
        {
            LogToConsole(message, severity);
            LogExceptionToConsole(exception, "Failed to write log file");
        }
        finally { _fileLock.Release(); }
    }

    public void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp)
    {
        _fileLock.Wait();
        try
        {
            PrepareLogFile(_logXpFilePath);
            File.AppendAllText(_logXpFilePath,
                $"[{ConsistentDateTimeFormat()}] - {user} gained {totalXp}xp (base: {baseXp}, bonus : {bonusXp}, reduce : {xpReduce}) in channel {channel} {Environment.NewLine}");
        }
        catch (Exception exception) { LogExceptionToConsole(exception, "Failed to write XP log"); }
        finally { _fileLock.Release(); }
    }

    // Returns DateTime.Now in format: d/M/yy HH:mm:ss
    public static string ConsistentDateTimeFormat()
    {
        return DateTime.Now.ToString("d/M/yy HH:mm:ss");
    }

    // Logs DiscordNet specific messages, this shouldn't be used for normal logging
    public static Task DiscordNetLogger(LogMessage message)
    {
        if (message.Exception is { } exception)
            LogExceptionToConsole(exception, $"{message.Source} | {message.Message}", message.Severity.ToExtended());
        else
            LogToConsole($"{message.Source} | {message.Message}", message.Severity.ToExtended());
        return Task.CompletedTask;
    }

    private void PrepareLogFile(string path)
    {
        Directory.CreateDirectory(_backupLogFilePath);
        if (File.Exists(path) && new FileInfo(path).Length > MaxLogSize)
        {
            var backupPath = Path.Combine(_backupLogFilePath,
                $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}.txt");
            File.Move(path, backupPath);
        }
        // Append creates a missing file; preparation and append share the same lock.
    }

    #region Console Messages
    // Logs message to console without changing the colour
    public static void LogConsole(string message)
    {
        Console.WriteLine($"[{ConsistentDateTimeFormat()}] {message}");
    }

    public static void LogToConsole(string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info)
    {
        lock (ConsoleLock)
        {
            ConsoleColor restoreColour = Console.ForegroundColor;
            try
            {
                if (!Console.IsOutputRedirected) SetConsoleColour(severity);
                Console.WriteLine($"[{ConsistentDateTimeFormat()}] {message} [{severity}]");
            }
            finally
            {
                if (!Console.IsOutputRedirected) Console.ForegroundColor = restoreColour;
            }
        }
    }

    public static void LogExceptionToConsole(Exception exception, string context,
        ExtendedLogSeverity severity = ExtendedLogSeverity.Error,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "") =>
        LogToConsole(ExceptionLogFormatter.Format(exception, context,
            callerFile: callerFile, callerLine: callerLine, callerMember: callerMember), severity);

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
