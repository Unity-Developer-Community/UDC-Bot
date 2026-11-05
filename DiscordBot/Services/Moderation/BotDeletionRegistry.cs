using System.Collections.Concurrent;

namespace DiscordBot.Services.Moderation;

/// <summary>
/// Message IDs the bot is deleting itself, so the audit log doesn't post a card for them
/// (they are already reported elsewhere, or are the bot's own housekeeping). Entries expire after <see cref="Lifetime"/>.
/// </summary>
public sealed class BotDeletionRegistry
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _expires = new();

    public void Register(IEnumerable<ulong> messageIds, DateTimeOffset now)
    {
        foreach (var id in messageIds)
            _expires[id] = now + Lifetime;

        foreach (var (id, expires) in _expires)
        {
            if (now >= expires)
                _expires.TryRemove(id, out _);
        }
    }

    public bool Contains(ulong messageId, DateTimeOffset now) =>
        _expires.TryGetValue(messageId, out var expires) && now < expires;
}
