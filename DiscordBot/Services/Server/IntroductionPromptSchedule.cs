namespace DiscordBot.Services.Server;

/// <summary>
/// Trailing debounce for the Introductions prompt. Each <see cref="Touch"/> pushes the due time
/// back to now + <see cref="Delay"/>. Thread-safe; all times are UTC.
/// </summary>
public sealed class IntroductionPromptSchedule
{
    private readonly object _lock = new();
    private DateTime? _dueUtc;

    public TimeSpan Delay { get; }

    public IntroductionPromptSchedule(int delayMinutes)
    {
        Delay = TimeSpan.FromMinutes(Math.Max(1, delayMinutes));
    }

    public DateTime? DueUtc
    {
        get { lock (_lock) return _dueUtc; }
    }

    public void Touch(DateTime nowUtc)
    {
        lock (_lock) _dueUtc = nowUtc + Delay;
    }

    public bool IsDue(DateTime nowUtc)
    {
        lock (_lock) return _dueUtc is { } due && nowUtc >= due;
    }

    /// <summary>
    /// If the schedule is due, clears it and returns true. Checking and clearing in one step means
    /// a <see cref="Touch"/> that lands while a prompt is being posted is not lost.
    /// </summary>
    public bool TryTakeDue(DateTime nowUtc)
    {
        lock (_lock)
        {
            if (_dueUtc is not { } due || nowUtc < due) return false;
            _dueUtc = null;
            return true;
        }
    }

    /// <summary>
    /// Schedules a retry after a failed post. Does nothing if a <see cref="Touch"/> already set a
    /// due time, so a newer message keeps its own debounce.
    /// </summary>
    public void Retry(DateTime retryAtUtc)
    {
        lock (_lock) _dueUtc ??= retryAtUtc;
    }

    public void Clear()
    {
        lock (_lock) _dueUtc = null;
    }
}
