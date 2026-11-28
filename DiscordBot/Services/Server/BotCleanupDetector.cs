namespace DiscordBot.Services.Server;

/// <summary>
/// Decides from audit-log entries whether another bot (such as Wick) deleted a member's message, so its command
/// clean-up gets no "Message deleted" card. Thread-safe.
/// </summary>
/// <remarks>
/// Discord merges consecutive deletions by the same moderator of the same author's messages in the same channel into
/// one entry and raises its count, so an entry can't simply be matched by age. Each entry carries credits: the
/// deletions it records that no deleted message has been matched to yet. An entry first seen within
/// <see cref="FreshEntry"/> of its creation brings all its deletions as credits; an older one only those counted after
/// it was first seen, so a member deleting their own message never matches an old entry. A message a member deleted
/// themselves creates no entry at all. A credit left by a deletion that no cleanup card claimed (a message that was
/// uncached or has no prefix) can absorb the member's own delete of a prefixed message in the same channel within
/// <see cref="FreshEntry"/>, and that delete then gets no card. This is accepted as rare: another bot must act on that
/// member in that channel first.
/// </remarks>
public sealed class BotCleanupDetector
{
    public static readonly TimeSpan FreshEntry = TimeSpan.FromSeconds(30);
    /// <summary>How long an entry's count is remembered after it was last seen.</summary>
    public static readonly TimeSpan SeenLifetime = TimeSpan.FromHours(1);

    /// <summary>One <c>MessageDeleted</c> audit-log entry: who deleted (actor), whose messages (target), where, and how many.</summary>
    public sealed record Entry(ulong Id, ulong ActorId, ulong TargetId, ulong ChannelId, int Count, DateTimeOffset CreatedAt);

    private sealed class Seen
    {
        public int Consumed;
        public DateTimeOffset LastSeen;
    }

    private readonly object _lock = new();
    private readonly Dictionary<ulong, Seen> _seen = new();

    /// <summary>
    /// Records <paramref name="entries"/> and returns true when one of them accounts for a deletion by
    /// <paramref name="actorId"/> of <paramref name="targetId"/>'s message in <paramref name="channelId"/> that no
    /// earlier call has claimed.
    /// </summary>
    public bool TryMatch(ulong actorId, ulong targetId, ulong channelId, IEnumerable<Entry> entries, DateTimeOffset now)
    {
        lock (_lock)
        {
            foreach (var (id, seen) in _seen.Where(s => now - s.Value.LastSeen > SeenLifetime).ToList())
                _seen.Remove(id);

            var matched = false;
            foreach (var entry in entries)
            {
                if (!_seen.TryGetValue(entry.Id, out var seen))
                {
                    seen = new Seen { Consumed = now - entry.CreatedAt <= FreshEntry ? 0 : entry.Count };
                    _seen[entry.Id] = seen;
                }
                seen.LastSeen = now;

                if (!matched && entry.ActorId == actorId && entry.TargetId == targetId && entry.ChannelId == channelId
                    && entry.Count > seen.Consumed)
                {
                    seen.Consumed++;
                    matched = true;
                }
            }
            return matched;
        }
    }
}
