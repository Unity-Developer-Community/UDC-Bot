using System.IO;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Logging;

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
            File.AppendAllText(_settings.ServerRootPath + @"/log.txt",
                $"[{ConsistantDateTimeFormat()}] {action} {Environment.NewLine}");
    }

    public void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp)
    {
        File.AppendAllText(_settings.ServerRootPath + @"/logXP.txt",
            $"[{ConsistantDateTimeFormat()}] - {user} gained {totalXp}xp (base: {baseXp}, bonus : {bonusXp}, reduce : {xpReduce}) in channel {channel} {Environment.NewLine}");
    }

    // Returns DateTime.Now in format: d/M/yy HH:mm:ss
    public static string ConsistantDateTimeFormat()
    {
        return DateTime.Now.ToString("d/M/yy HH:mm:ss");
    }

    // Logs DiscordNet specific messages, this shouldn't be used for normal logging
    public static Task DiscordNetLogger(LogMessage message)
    {
        LoggingService.LogToConsole($"{message.Source} | {message.Message}", message.Severity);
        return Task.CompletedTask;
    }
    #region Console Messages
    // Logs message to console without changing the colour
    public static void LogConsole(string message) {
        Console.WriteLine($"[{ConsistantDateTimeFormat()}] {message}");
    }
    public static void LogToConsole(string message, LogSeverity severity = LogSeverity.Info) 
    {
        ConsoleColor restoreColour = Console.ForegroundColor;
        SetConsoleColour(severity);

        Console.WriteLine($"[{ConsistantDateTimeFormat()}] {message} [{severity}]");

        Console.ForegroundColor = restoreColour;
    }
    private static void SetConsoleColour(LogSeverity severity)
    {
        switch (severity)
        {
            case LogSeverity.Critical:
            case LogSeverity.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                break;
            case LogSeverity.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                break;
            case LogSeverity.Info:
                Console.ForegroundColor = ConsoleColor.White;
                break;
            case LogSeverity.Verbose:
            case LogSeverity.Debug:
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