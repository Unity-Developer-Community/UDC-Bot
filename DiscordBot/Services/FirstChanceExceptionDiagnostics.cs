using System.Runtime.ExceptionServices;

namespace DiscordBot.Services.Logging;

/// <summary>Temporary exact-type tracing, including exceptions subsequently caught by libraries.</summary>
public sealed class FirstChanceExceptionDiagnostics : IDisposable
{
    [ThreadStatic] private static bool _reporting;
    private readonly string _exceptionType;
    private readonly Action<string> _write;
    private int _remaining = 20;

    public FirstChanceExceptionDiagnostics(string exceptionType, Action<string> write)
    {
        _exceptionType = exceptionType;
        _write = write;
        AppDomain.CurrentDomain.FirstChanceException += OnException;
    }

    public static FirstChanceExceptionDiagnostics? StartFromEnvironment()
    {
        var type = Environment.GetEnvironmentVariable("UDCBOT_TRACE_EXCEPTION_TYPE");
        if (string.IsNullOrWhiteSpace(type)) return null;
        LoggingService.LogToConsole($"First-chance tracing enabled for {type} (first 20 throws, console only).",
            ExtendedLogSeverity.Warning);
        return new FirstChanceExceptionDiagnostics(type.Trim(), message =>
            LoggingService.LogToConsole(message, ExtendedLogSeverity.Debug));
    }

    private void OnException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (_reporting || Volatile.Read(ref _remaining) <= 0 ||
            args.Exception.GetType().FullName != _exceptionType) return;
        var remaining = Interlocked.Decrement(ref _remaining);
        if (remaining < 0) return;
        _reporting = true;
        try
        {
            _write(ExceptionLogFormatter.Format(args.Exception, "First-chance (may be handled)", detailed: true));
            if (remaining == 0)
                _write("First-chance trace limit reached; restart to capture another 20 throws.");
        }
        catch
        {
            // A diagnostic callback must never interfere with the exception being observed.
        }
        finally { _reporting = false; }
    }

    public void Dispose() => AppDomain.CurrentDomain.FirstChanceException -= OnException;
}
