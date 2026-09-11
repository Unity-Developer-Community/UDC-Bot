using Microsoft.Extensions.Logging;

namespace DiscordBot.Services.Logging;

/// <summary>Keep host and ILogger failures visible using the bot's exception format.</summary>
public sealed class BotConsoleLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new BotConsoleLogger(categoryName);
    public void Dispose() { }

    private sealed class BotConsoleLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var severity = logLevel switch
            {
                LogLevel.Critical => ExtendedLogSeverity.Critical,
                LogLevel.Error => ExtendedLogSeverity.Error,
                LogLevel.Warning => ExtendedLogSeverity.Warning,
                LogLevel.Information => ExtendedLogSeverity.Info,
                LogLevel.Debug => ExtendedLogSeverity.Debug,
                _ => ExtendedLogSeverity.Verbose
            };
            var message = $"{category} | {formatter(state, exception)}";
            if (exception is null) LoggingService.LogToConsole(message, severity);
            else LoggingService.LogExceptionToConsole(exception, message, severity);
        }
    }
}
