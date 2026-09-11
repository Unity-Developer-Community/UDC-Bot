using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace DiscordBot.Services.Logging;

/// <summary>Bounded exception summaries without machine-specific source paths.</summary>
public static class ExceptionLogFormatter
{
    public static bool DevelopmentDetails =>
#if DEBUG
        true;
#else
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
#endif

    public static string Format(Exception exception, string? context = null, bool? detailed = null,
        string? callerFile = null, int callerLine = 0, string? callerMember = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var development = detailed ?? DevelopmentDetails;
        var pending = new Queue<Exception>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var parts = new List<string>();
        pending.Enqueue(exception);
        while (pending.Count > 0 && parts.Count < 8)
        {
            var current = pending.Dequeue();
            if (!seen.Add(current)) continue;
            var frames = new StackTrace(current, true).GetFrames() ?? [];
            var applicationFrames = frames.Where(frame =>
                frame.GetMethod()?.DeclaringType?.Assembly == typeof(LoggingService).Assembly).ToArray();
            var selected = (applicationFrames.Length > 0 ? applicationFrames : frames)
                .Take(development ? 3 : 1).Select(FormatFrame).ToArray();
            var location = selected.Length > 0
                ? (development ? "\n  at " : " @ ") + string.Join("\n  at ", selected)
                : !string.IsNullOrEmpty(callerFile)
                    ? $" @ {Path.GetFileName(callerFile)}:{callerLine} ({callerMember}; logged here)"
                    : "";
            parts.Add($"{current.GetType().Name}: {OneLine(current.Message, 500)}{location}");
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.Take(8)) pending.Enqueue(inner);
            }
            else if (current.InnerException is { } inner) pending.Enqueue(inner);
        }
        var prefix = string.IsNullOrWhiteSpace(context) ? "" : OneLine(context, 300) + " | ";
        var result = prefix + string.Join(development ? "\nCaused by: " : " -> ", parts);
        if (pending.Count > 0) result += " [additional exceptions omitted]";
        return result.Length <= 1800 ? result : result[..1768] + " [truncated]";
    }

    private static string FormatFrame(StackFrame frame)
    {
        var method = frame.GetMethod();
        var type = method?.DeclaringType;
        var name = $"{type?.Name}.{method?.Name}";
        if (method?.Name == "MoveNext" && type?.DeclaringType is { } owner)
            name = $"{owner.Name}.{type.Name.Split('>')[0].TrimStart('<')}";
        var file = frame.GetFileName();
        return string.IsNullOrEmpty(file) || frame.GetFileLineNumber() == 0
            ? name : $"{Path.GetFileName(file)}:{frame.GetFileLineNumber()} ({name})";
    }

    private static string OneLine(string text, int limit)
    {
        var value = text.Replace('\r', ' ').Replace('\n', ' ');
        return value.Length <= limit ? value : value[..limit] + "…";
    }
}

public static class ExceptionLoggingExtensions
{
    /// <summary>Log the exception object so its origin and inner exceptions are retained.</summary>
    public static async Task LogException(this ILoggingService logging, Exception exception, string context,
        LogBehaviour behaviour = LogBehaviour.Console | LogBehaviour.File,
        ExtendedLogSeverity severity = ExtendedLogSeverity.Error,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "")
    {
        var local = behaviour & ~LogBehaviour.Channel;
        if (local != LogBehaviour.None)
            await logging.Log(local, ExceptionLogFormatter.Format(exception, context,
                callerFile: callerFile, callerLine: callerLine, callerMember: callerMember), severity);
        if (behaviour.HasFlag(LogBehaviour.Channel))
            await logging.Log(LogBehaviour.Channel, ExceptionLogFormatter.Format(exception, context, detailed: false,
                callerFile: callerFile, callerLine: callerLine, callerMember: callerMember), severity);
    }
}
