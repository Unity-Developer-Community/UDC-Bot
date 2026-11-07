namespace DiscordBot.Services.Server;

/// <summary>
/// Remembers each user's recent name changes so changes within <see cref="Window"/> of the first
/// one share a single member-log card. Holds at most <see cref="Capacity"/> users and drops expired
/// users whenever it is used. In memory only, and not thread-safe: the caller locks.
/// </summary>
public sealed class NameChangeTracker
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    public const int Capacity = 30;
    /// <summary>Values kept per field: the first one and the latest ones, with the gap counted in <see cref="MemberLogCards.NameChange.Omitted"/>.</summary>
    public const int MaxValuesPerField = 6;

    public sealed record Change(MemberLogCards.NameKind Kind, string? Before, string? After);

    /// <summary>What the card shows. <see cref="MessageId"/> is the card to edit, or null when one must be posted.</summary>
    public sealed record Snapshot(
        IReadOnlyList<MemberLogCards.NameChange> Fields,
        DateTimeOffset FirstAt,
        DateTimeOffset LastAt,
        bool IsNew,
        ulong? MessageId);

    private sealed class Field(MemberLogCards.NameKind kind)
    {
        public MemberLogCards.NameKind Kind { get; } = kind;
        public List<string?> Values { get; } = [];
        public int Omitted { get; set; }

        public void Add(string? value)
        {
            if (Values.Count > 0 && Values[^1] == value)
                return;
            Values.Add(value);
            if (Values.Count > MaxValuesPerField)
            {
                // Keep the original name and the latest ones.
                Values.RemoveAt(1);
                Omitted++;
            }
        }
    }

    private sealed class Entry(DateTimeOffset firstAt)
    {
        public DateTimeOffset FirstAt { get; } = firstAt;
        public DateTimeOffset LastAt { get; set; } = firstAt;
        public List<Field> Fields { get; } = [];
        public ulong? MessageId { get; set; }
    }

    private readonly Dictionary<ulong, Entry> _entries = [];

    public int Count => _entries.Count;

    public Snapshot Record(ulong userId, MemberLogCards.NameKind kind, string? before, string? after, DateTimeOffset now) =>
        Record(userId, [new Change(kind, before, after)], now);

    /// <summary>Adds the changes from one event to the user's entry, opening a new entry when there is none within the window.</summary>
    public Snapshot Record(ulong userId, IReadOnlyCollection<Change> changes, DateTimeOffset now)
    {
        Prune(now);

        var isNew = !_entries.TryGetValue(userId, out var entry);
        if (entry == null)
        {
            if (_entries.Count >= Capacity)
                _entries.Remove(_entries.MinBy(e => e.Value.LastAt).Key);
            entry = new Entry(now);
            _entries[userId] = entry;
        }

        foreach (var change in changes)
        {
            var field = entry.Fields.Find(f => f.Kind == change.Kind);
            if (field == null)
            {
                field = new Field(change.Kind);
                entry.Fields.Add(field);
            }
            field.Add(change.Before);
            field.Add(change.After);
        }
        entry.LastAt = now;

        return new Snapshot(
            entry.Fields.Select(f => new MemberLogCards.NameChange(f.Kind, f.Values.ToList(), f.Omitted)).ToList(),
            entry.FirstAt, entry.LastAt, isNew, entry.MessageId);
    }

    /// <summary>Links the posted card to the user's entry, so later changes edit it.</summary>
    public void SetMessage(ulong userId, ulong messageId)
    {
        if (_entries.TryGetValue(userId, out var entry))
            entry.MessageId = messageId;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (userId, entry) in _entries)
            if (now - entry.FirstAt > Window)
                _entries.Remove(userId);
    }
}
