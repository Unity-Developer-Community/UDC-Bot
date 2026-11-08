using Discord;
using DiscordBot.Services.Logging;

namespace CasinoWeb.Logging;

/// <summary>Sends the casino services' log lines to ASP.NET logging (console, collected by Loki). It never posts to the Discord log channel.</summary>
public sealed class WebLoggingService(ILogger<WebLoggingService> logger) : ILoggingService
{
    public void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp) { }

    public Task Log(LogBehaviour behaviour, string message, ExtendedLogSeverity severity = ExtendedLogSeverity.Info, Embed? embed = null)
    {
        logger.Log(ToLevel(severity), "{Message}", message);
        return Task.CompletedTask;
    }

    // Cards are for the Discord log channel, which the casino never posts to.
    public Task<IUserMessage?> LogComponentsToChannel(MessageComponent components, IReadOnlyCollection<FileAttachment>? files = null, LogTopic topic = LogTopic.General) => Task.FromResult<IUserMessage?>(null);

    private static LogLevel ToLevel(ExtendedLogSeverity severity) => severity switch
    {
        ExtendedLogSeverity.Critical => LogLevel.Critical,
        ExtendedLogSeverity.Error => LogLevel.Error,
        ExtendedLogSeverity.Warning or ExtendedLogSeverity.LowWarning => LogLevel.Warning,
        ExtendedLogSeverity.Verbose or ExtendedLogSeverity.Debug => LogLevel.Debug,
        _ => LogLevel.Information,
    };
}
