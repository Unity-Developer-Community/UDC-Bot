using DiscordBot.Domain.UnityNews;

namespace DiscordBot.Tests.Domain.UnityNews;

public class BlogDiscussionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CardDue_NoHumanMessage_IsDue()
    {
        Assert.True(BlogDiscussion.CardDue(Now, Now, null));
    }

    [Fact]
    public void CardDue_SomeoneTalkedRecently_Waits()
    {
        Assert.False(BlogDiscussion.CardDue(Now, Now.AddMinutes(-5), Now.AddMinutes(-9)));
    }

    [Fact]
    public void CardDue_QuietForTheIdleTime_IsDue()
    {
        Assert.True(BlogDiscussion.CardDue(Now, Now.AddMinutes(-5), Now - BlogDiscussion.IdleTime));
    }

    [Fact]
    public void CardDue_BusyButWaitedTooLong_IsDue()
    {
        Assert.True(BlogDiscussion.CardDue(Now, Now - BlogDiscussion.MaxWait, Now.AddMinutes(-1)));
        Assert.False(BlogDiscussion.CardDue(Now, Now - BlogDiscussion.MaxWait + TimeSpan.FromMinutes(1), Now.AddMinutes(-1)));
    }

    [Fact]
    public void RepostAllowed_NoCardYet_IsAllowed()
    {
        Assert.True(BlogDiscussion.RepostAllowed(Now, default, 0, [1, 2, 3]));
    }

    [Fact]
    public void RepostAllowed_WithinTheCooldown_IsRefused()
    {
        Assert.False(BlogDiscussion.RepostAllowed(Now, Now - BlogDiscussion.RepostCooldown + TimeSpan.FromMinutes(1), 42, []));
    }

    [Fact]
    public void RepostAllowed_LastCardStillAmongTheNewestMessages_IsRefused()
    {
        Assert.False(BlogDiscussion.RepostAllowed(Now, Now.AddHours(-5), 42, [50, 42, 30]));
    }

    [Fact]
    public void RepostAllowed_PastTheCooldownAndScrolledAway_IsAllowed()
    {
        Assert.True(BlogDiscussion.RepostAllowed(Now, Now - BlogDiscussion.RepostCooldown, 42, [50, 49, 48]));
    }

    private sealed record Card(string Name, DateTimeOffset QueuedAt);

    private static (IReadOnlyList<Card> Expired, Card? Next) Next(IEnumerable<Card> queue, DateTimeOffset lastCardAt, DateTimeOffset? lastHuman = null) =>
        BlogDiscussion.NextCard(Now, queue, c => c.QueuedAt, lastCardAt, lastHuman);

    [Fact]
    public void NextCard_EmptyQueue_Nothing()
    {
        var (expired, next) = Next([], default);
        Assert.Empty(expired);
        Assert.Null(next);
    }

    [Fact]
    public void NextCard_NoCardYet_QuietChannel_SendsTheOldest()
    {
        var (_, next) = Next([new Card("b", Now.AddMinutes(-5)), new Card("a", Now.AddMinutes(-30))], default);
        Assert.Equal("a", next?.Name);
    }

    [Fact]
    public void NextCard_WithinTheGap_Waits()
    {
        var (_, next) = Next([new Card("a", Now.AddHours(-1))], Now - BlogDiscussion.MinGap + TimeSpan.FromMinutes(1));
        Assert.Null(next);
    }

    [Fact]
    public void NextCard_GapPassed_QuietChannel_Sends()
    {
        var (_, next) = Next([new Card("a", Now.AddHours(-3))], Now - BlogDiscussion.MinGap);
        Assert.Equal("a", next?.Name);
    }

    [Fact]
    public void NextCard_GapPassed_BusyChannel_WaitsFromTheEndOfTheGap()
    {
        // Queued 6h ago, but the gap only ended 1h ago: the busy channel is waited for up to MaxWait from then.
        var queue = new[] { new Card("a", Now.AddHours(-6)) };
        Assert.Null(Next(queue, Now - BlogDiscussion.MinGap - TimeSpan.FromHours(1), Now.AddMinutes(-1)).Next);
        Assert.Equal("a", Next(queue, Now - BlogDiscussion.MinGap - BlogDiscussion.MaxWait, Now.AddMinutes(-1)).Next?.Name);
    }

    [Fact]
    public void NextCard_CardsADayOld_AreDropped_AndTheNextLiveOneConsidered()
    {
        var old = new Card("old", Now - BlogDiscussion.MaxCardAge);
        var live = new Card("live", Now.AddHours(-2));
        var (expired, next) = Next([old, live], default);
        Assert.Equal([old], expired);
        Assert.Equal("live", next?.Name);
    }

    [Fact]
    public void NextCard_ExpiredStillReportedWithinTheGap()
    {
        var old = new Card("old", Now.AddHours(-30));
        var (expired, next) = Next([old], Now.AddMinutes(-10));
        Assert.Equal([old], expired);
        Assert.Null(next);
    }

    [Fact]
    public void ShouldReplace_NoHumanAfterTheLastCard_Replaces()
    {
        Assert.True(BlogDiscussion.ShouldReplace(50, [(60, false), (50, false), (40, true)]));
    }

    [Fact]
    public void ShouldReplace_SomeoneWroteAfterIt_Keeps()
    {
        Assert.False(BlogDiscussion.ShouldReplace(50, [(60, true), (50, false)]));
    }

    [Fact]
    public void ShouldReplace_LastCardNotAmongTheNewest_Keeps()
    {
        Assert.False(BlogDiscussion.ShouldReplace(50, [(70, false), (60, false)]));
    }

    [Fact]
    public void ShouldReplace_NoCardYet_Nothing()
    {
        Assert.False(BlogDiscussion.ShouldReplace(0, [(70, false)]));
    }
}
