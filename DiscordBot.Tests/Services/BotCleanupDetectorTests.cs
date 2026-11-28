using DiscordBot.Services.Server;
using Entry = DiscordBot.Services.Server.BotCleanupDetector.Entry;

namespace DiscordBot.Tests.Services;

public class BotCleanupDetectorTests
{
    private const ulong Wick = 536991182035746816;
    private const ulong Author = 10;
    private const ulong Channel = 20;
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private static Entry WickEntry(ulong id = 1, int count = 1, TimeSpan? age = null, ulong actor = Wick, ulong target = Author, ulong channel = Channel) =>
        new(id, actor, target, channel, count, Now - (age ?? TimeSpan.FromSeconds(3)));

    [Fact]
    public void FreshEntry_Matches()
    {
        Assert.True(new BotCleanupDetector().TryMatch(Wick, Author, Channel, [WickEntry()], Now));
    }

    [Fact]
    public void SameEntry_MatchesOnlyAsOftenAsItsCount()
    {
        var detector = new BotCleanupDetector();
        Assert.True(detector.TryMatch(Wick, Author, Channel, [WickEntry()], Now));
        // A member deleting their own w! message afterwards: the entry's one deletion is already claimed.
        Assert.False(detector.TryMatch(Wick, Author, Channel, [WickEntry()], Now.AddSeconds(5)));
    }

    [Fact]
    public void CountIncrease_OnKnownEntry_Matches()
    {
        var detector = new BotCleanupDetector();
        Assert.True(detector.TryMatch(Wick, Author, Channel, [WickEntry()], Now));
        // Discord merged a second clean-up into the same entry two minutes later.
        Assert.True(detector.TryMatch(Wick, Author, Channel, [WickEntry(count: 2)], Now.AddMinutes(2)));
    }

    [Fact]
    public void TwoDeletesSeenAfterBoth_BothMatch()
    {
        var detector = new BotCleanupDetector();
        Assert.True(detector.TryMatch(Wick, Author, Channel, [WickEntry(count: 2)], Now));
        Assert.True(detector.TryMatch(Wick, Author, Channel, [WickEntry(count: 2)], Now));
        Assert.False(detector.TryMatch(Wick, Author, Channel, [WickEntry(count: 2)], Now));
    }

    [Fact]
    public void OldEntrySeenFirstTime_DoesNotMatch()
    {
        Assert.False(new BotCleanupDetector().TryMatch(Wick, Author, Channel, [WickEntry(age: TimeSpan.FromMinutes(3))], Now));
    }

    [Fact]
    public void OldEntry_MatchesAfterItsCountGoesUp()
    {
        var detector = new BotCleanupDetector();
        Assert.False(detector.TryMatch(Wick, Author, Channel, [WickEntry(count: 3, age: TimeSpan.FromMinutes(3))], Now));
        Assert.True(detector.TryMatch(Wick, Author, Channel, [WickEntry(count: 4, age: TimeSpan.FromMinutes(3))], Now.AddSeconds(4)));
    }

    [Theory]
    [InlineData(999ul, Author, Channel)]
    [InlineData(Wick, 999ul, Channel)]
    [InlineData(Wick, Author, 999ul)]
    public void OtherActorTargetOrChannel_DoesNotMatch(ulong actor, ulong target, ulong channel)
    {
        Assert.False(new BotCleanupDetector().TryMatch(Wick, Author, Channel, [WickEntry(actor: actor, target: target, channel: channel)], Now));
    }

    [Fact]
    public void NoEntries_DoesNotMatch()
    {
        Assert.False(new BotCleanupDetector().TryMatch(Wick, Author, Channel, [], Now));
    }
}
