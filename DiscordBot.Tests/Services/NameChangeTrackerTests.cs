using DiscordBot.Services.Server;
using static DiscordBot.Services.Server.MemberLogCards;

namespace DiscordBot.Tests.Services;

public class NameChangeTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Record_FirstChange_OpensEntryWithBeforeAndAfter()
    {
        var tracker = new NameChangeTracker();
        var snapshot = tracker.Record(1, NameKind.Username, "a", "b", Now);

        Assert.True(snapshot.IsNew);
        Assert.Null(snapshot.MessageId);
        var field = Assert.Single(snapshot.Fields);
        Assert.Equal(NameKind.Username, field.Kind);
        Assert.Equal(["a", "b"], field.Values);
        Assert.Equal(Now, snapshot.FirstAt);
        Assert.Equal(Now, snapshot.LastAt);
    }

    [Fact]
    public void Record_WithinWindow_AppendsAndKeepsLinkedMessage()
    {
        var tracker = new NameChangeTracker();
        tracker.Record(1, NameKind.Username, "a", "b", Now);
        tracker.SetMessage(1, 42);

        var snapshot = tracker.Record(1, NameKind.Username, "b", "c", Now + NameChangeTracker.Window);

        Assert.False(snapshot.IsNew);
        Assert.Equal(42ul, snapshot.MessageId);
        Assert.Equal(["a", "b", "c"], Assert.Single(snapshot.Fields).Values);
        Assert.Equal(Now, snapshot.FirstAt);
        Assert.Equal(Now + NameChangeTracker.Window, snapshot.LastAt);
    }

    [Fact]
    public void Record_AfterWindow_OpensNewEntry()
    {
        var tracker = new NameChangeTracker();
        tracker.Record(1, NameKind.Username, "a", "b", Now);
        tracker.SetMessage(1, 42);

        var snapshot = tracker.Record(1, NameKind.Username, "b", "c", Now + NameChangeTracker.Window + TimeSpan.FromSeconds(1));

        Assert.True(snapshot.IsNew);
        Assert.Null(snapshot.MessageId);
        Assert.Equal(["b", "c"], Assert.Single(snapshot.Fields).Values);
    }

    [Fact]
    public void Record_WindowIsAnchoredAtFirstChange()
    {
        var tracker = new NameChangeTracker();
        tracker.Record(1, NameKind.Username, "a", "b", Now);
        tracker.Record(1, NameKind.Username, "b", "c", Now + TimeSpan.FromMinutes(9));

        Assert.True(tracker.Record(1, NameKind.Username, "c", "d", Now + TimeSpan.FromMinutes(11)).IsNew);
    }

    [Fact]
    public void Record_PrunesExpiredUsers()
    {
        var tracker = new NameChangeTracker();
        tracker.Record(1, NameKind.Username, "a", "b", Now);
        tracker.Record(2, NameKind.Username, "a", "b", Now + TimeSpan.FromMinutes(5));

        tracker.Record(3, NameKind.Username, "a", "b", Now + TimeSpan.FromMinutes(11));

        Assert.Equal(2, tracker.Count);
        Assert.False(tracker.Record(2, NameKind.Username, "b", "c", Now + TimeSpan.FromMinutes(12)).IsNew);
    }

    [Fact]
    public void Record_WhenFull_EvictsUserWithOldestChange()
    {
        var tracker = new NameChangeTracker();
        for (var i = 0; i < NameChangeTracker.Capacity; i++)
            tracker.Record((ulong)i, NameKind.Username, "a", "b", Now + TimeSpan.FromSeconds(i));
        // User 0 changed again, so user 1 now has the oldest change.
        tracker.Record(0, NameKind.Username, "b", "c", Now + TimeSpan.FromSeconds(40));

        tracker.Record(999, NameKind.Username, "a", "b", Now + TimeSpan.FromSeconds(41));

        Assert.Equal(NameChangeTracker.Capacity, tracker.Count);
        Assert.False(tracker.Record(0, NameKind.Username, "c", "d", Now + TimeSpan.FromSeconds(42)).IsNew);
        Assert.True(tracker.Record(1, NameKind.Username, "b", "c", Now + TimeSpan.FromSeconds(43)).IsNew);
    }

    [Fact]
    public void Record_ConsecutiveDuplicatesCollapse_RevertIsKept()
    {
        var tracker = new NameChangeTracker();
        tracker.Record(1, NameKind.DisplayName, "Name", "name", Now);
        var snapshot = tracker.Record(1, NameKind.DisplayName, "name", "Name", Now.AddSeconds(30));

        Assert.Equal(["Name", "name", "Name"], Assert.Single(snapshot.Fields).Values);
    }

    [Fact]
    public void Record_SeveralKinds_KeepsFieldsInOrderSeen()
    {
        var tracker = new NameChangeTracker();
        tracker.Record(1, NameKind.DisplayName, "D1", "D2", Now);
        var snapshot = tracker.Record(1,
            [new(NameKind.Username, "u1", "u2"), new(NameKind.DisplayName, "D2", "D3")],
            Now.AddMinutes(2));

        Assert.Collection(snapshot.Fields,
            f => { Assert.Equal(NameKind.DisplayName, f.Kind); Assert.Equal(["D1", "D2", "D3"], f.Values); },
            f => { Assert.Equal(NameKind.Username, f.Kind); Assert.Equal(["u1", "u2"], f.Values); });
    }

    [Fact]
    public void Record_PastValueCap_KeepsFirstAndLatest()
    {
        var tracker = new NameChangeTracker();
        NameChangeTracker.Snapshot snapshot = null!;
        for (var i = 0; i < 9; i++)
            snapshot = tracker.Record(1, NameKind.Username, $"n{i}", $"n{i + 1}", Now.AddSeconds(i));

        var field = Assert.Single(snapshot.Fields);
        Assert.Equal(NameChangeTracker.MaxValuesPerField, field.Values.Count);
        Assert.Equal(["n0", "n5", "n6", "n7", "n8", "n9"], field.Values);
        Assert.Equal(4, field.Omitted);
        Assert.Equal(9, field.Changes);
    }
}
