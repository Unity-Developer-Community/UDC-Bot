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
}
