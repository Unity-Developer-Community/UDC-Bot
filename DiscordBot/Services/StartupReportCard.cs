using DiscordBot.Services.Server;

namespace DiscordBot.Services.Logging;

/// <summary>
/// The startup report card: one Components V2 message listing the channel lines logged while the
/// bot started. Inputs are plain values so the layout can be tested.
/// </summary>
public static class StartupReportCard
{
    public static readonly Color StartingColor = new(150, 150, 160);
    public static readonly Color CleanColor = new(120, 200, 120);
    public static readonly Color WarningColor = new(240, 200, 80);
    public static readonly Color ErrorColor = new(220, 90, 90);

    /// <summary>Longest entry shown. The full text is in the log file.</summary>
    public const int MaxLineLength = 200;
    // Room kept for the "N more lines" note in the footer.
    private const int OmittedNoteReserve = 60;

    public sealed record Entry(TimeSpan Offset, ExtendedLogSeverity Severity, string Text);

    /// <summary>Process memory: the working set (what the OS counts, as in a container's usage) and the managed heap.</summary>
    public sealed record MemorySample(long WorkingSet, long GcHeap)
    {
        public static MemorySample Current() => new(Environment.WorkingSet, GC.GetTotalMemory(false));
    }

    /// <param name="FinishedAt">Null while starting.</param>
    /// <param name="Pending">Steps not finished yet: shown while starting, or as a warning when <paramref name="TimedOut"/>.</param>
    /// <param name="Memory">Taken when startup finished.</param>
    /// <param name="MemoryLater">Taken at <paramref name="MemoryLaterAt"/>; null until then.</param>
    public sealed record State(
        DateTimeOffset StartedAt,
        DateTimeOffset ReadyAt,
        DateTimeOffset? FinishedAt,
        IReadOnlyList<Entry> Entries,
        IReadOnlyList<string> Pending,
        bool TimedOut,
        MemorySample? Memory = null,
        MemorySample? MemoryLater = null,
        DateTimeOffset? MemoryLaterAt = null);

    public static MessageComponent Build(State state, int budget = AuditCard.DefaultBudget)
    {
        var errors = state.Entries.Count(e => IsError(e.Severity));
        var warnings = state.Entries.Count(e => IsWarning(e.Severity));
        var warned = warnings > 0 || state.TimedOut;

        var (title, color) = state.FinishedAt == null ? ("⏳ Starting…", StartingColor)
            : errors > 0 ? ("❌ Bot started with errors", ErrorColor)
            : warned ? ("⚠️ Bot started with warnings", WarningColor)
            : ("✅ Bot started", CleanColor);

        var times = $"Started {AuditLogCards.Timestamp(state.StartedAt, 'R')} ({AuditLogCards.Timestamp(state.StartedAt, 'T')})" +
                    $" · Gateway ready `{Offset(state.ReadyAt - state.StartedAt)}`";
        if (state.FinishedAt is { } finishedAt)
            times += $" · Finished {AuditLogCards.Timestamp(finishedAt, 'T')} · took {(finishedAt - state.StartedAt).TotalSeconds:0.0} s";
        var timedOut = state.TimedOut
            ? $"⚠️ Gave up waiting for: {string.Join(", ", state.Pending)}"
            : "";
        var memory = MemoryLine(state);

        var counts = new List<string> { $"{state.Entries.Count} line{(state.Entries.Count == 1 ? "" : "s")}" };
        if (warnings > 0) counts.Add($"{warnings} warning{(warnings == 1 ? "" : "s")}");
        if (errors > 0) counts.Add($"{errors} error{(errors == 1 ? "" : "s")}");
        if (state.FinishedAt == null && state.Pending.Count > 0)
            counts.Add($"waiting for: {string.Join(", ", state.Pending)}");
        var footer = string.Join(" · ", counts);

        var header = string.Join('\n', new[] { $"### {title}", times, memory, timedOut }.Where(l => l.Length > 0));
        var room = budget - header.Length - footer.Length - OmittedNoteReserve;
        var (lines, omitted) = FitEntries(state.Entries, room);
        if (omitted > 0)
            footer += $" · … {omitted} more line{(omitted == 1 ? "" : "s")} in the log file";

        var card = new AuditCard(color).WithHeader(title, null, null, times, memory, timedOut);
        if (lines.Count > 0)
            card.AddSeparator().AddLines(lines);
        return card.WithFooter(footer).Build(budget);
    }

    /// <summary><c>Memory: 182 MB (GC heap 64 MB) · 1m later: 190 MB (+8 MB) · GC heap 70 MB (+6 MB)</c>. Empty before startup finishes.</summary>
    public static string MemoryLine(State state)
    {
        if (state.Memory is not { } first || state.FinishedAt is not { } finishedAt)
            return "";
        var line = $"Memory: {Megabytes(first.WorkingSet)} (GC heap {Megabytes(first.GcHeap)})";
        if (state.MemoryLaterAt is not { } laterAt)
            return line;
        if (state.MemoryLater is not { } later)
            return line + $" · measuring again {AuditLogCards.Timestamp(laterAt, 'R')}";
        return line + $" · {MemberLogCards.StayDuration(laterAt - finishedAt)} later: {Megabytes(later.WorkingSet)} ({Change(later.WorkingSet - first.WorkingSet)})" +
               $" · GC heap {Megabytes(later.GcHeap)} ({Change(later.GcHeap - first.GcHeap)})";
    }

    public static string Megabytes(long bytes) => $"{bytes / (1024.0 * 1024.0):0} MB";

    private static string Change(long bytes)
    {
        var mb = Math.Round(bytes / (1024.0 * 1024.0));
        return mb == 0 ? "±0 MB" : $"{mb:+0;-0} MB";
    }

    /// <summary><c>+ss.fff</c>, or <c>+m:ss.fff</c> from a minute on.</summary>
    public static string Offset(TimeSpan offset)
    {
        if (offset < TimeSpan.Zero)
            offset = TimeSpan.Zero;
        return offset.TotalMinutes >= 1
            ? $"+{(int)offset.TotalMinutes}:{offset.Seconds:00}.{offset.Milliseconds:000}"
            : $"+{offset.Seconds:00}.{offset.Milliseconds:000}";
    }

    public static string Marker(ExtendedLogSeverity severity) =>
        IsError(severity) ? "❌" : IsWarning(severity) ? "⚠️" : severity == ExtendedLogSeverity.Positive ? "✅" : "•";

    /// <summary>The entry's first line, cut to <see cref="MaxLineLength"/>.</summary>
    public static string LineText(string text)
    {
        var trimmed = text.Trim();
        var newline = trimmed.IndexOf('\n');
        var first = newline < 0 ? trimmed : trimmed[..newline].TrimEnd() + " …";
        return AuditCard.Fit(first, MaxLineLength, "…");
    }

    public static string Line(Entry entry) => $"`{Offset(entry.Offset)}` {Marker(entry.Severity)} {LineText(entry.Text)}";

    /// <summary>Entry lines in order within <paramref name="room"/> chars, dropping the least severe (latest first) when they don't fit.</summary>
    private static (List<string> Lines, int Omitted) FitEntries(IReadOnlyList<Entry> entries, int room)
    {
        var lines = entries.Select(Line).ToList();
        var kept = Enumerable.Repeat(true, lines.Count).ToArray();
        var length = lines.Sum(l => l.Length) + Math.Max(0, lines.Count - 1);

        // Least important first: Info, then Positive, then warnings, then errors; latest first within each.
        var dropOrder = Enumerable.Range(0, lines.Count)
            .OrderBy(i => Priority(entries[i].Severity))
            .ThenByDescending(i => i);
        var omitted = 0;
        foreach (var i in dropOrder)
        {
            if (length <= room)
                break;
            kept[i] = false;
            length -= lines[i].Length + 1;
            omitted++;
        }
        return (lines.Where((_, i) => kept[i]).ToList(), omitted);
    }

    private static int Priority(ExtendedLogSeverity severity) =>
        IsError(severity) ? 3 : IsWarning(severity) ? 2 : severity == ExtendedLogSeverity.Positive ? 1 : 0;

    private static bool IsError(ExtendedLogSeverity severity) =>
        severity is ExtendedLogSeverity.Error or ExtendedLogSeverity.Critical;

    private static bool IsWarning(ExtendedLogSeverity severity) =>
        severity is ExtendedLogSeverity.Warning or ExtendedLogSeverity.LowWarning;
}
