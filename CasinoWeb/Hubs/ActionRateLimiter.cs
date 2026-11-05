using System.Collections.Concurrent;

namespace CasinoWeb.Hubs;

/// <summary>Lets a player start at most <see cref="MaxActions"/> game actions in any one second, across all their connections.</summary>
public sealed class ActionRateLimiter(TimeProvider clock)
{
    public const int MaxActions = 5;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    // One short queue per player who ever acted: bounded by the number of guild members.
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _recent = new();

    /// <summary>Counts the action and returns <c>true</c>, or returns <c>false</c> without counting it, so a refused burst recovers after a second.</summary>
    public bool TryAcquire(string userId)
    {
        var now = clock.GetUtcNow();
        var starts = _recent.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (starts)
        {
            while (starts.Count > 0 && now - starts.Peek() >= Window)
                starts.Dequeue();
            if (starts.Count >= MaxActions)
                return false;
            starts.Enqueue(now);
            return true;
        }
    }
}
