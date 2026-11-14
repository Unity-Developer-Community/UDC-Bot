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
}
