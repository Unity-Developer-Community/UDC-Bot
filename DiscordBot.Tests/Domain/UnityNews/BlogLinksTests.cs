using DiscordBot.Domain.UnityNews;

namespace DiscordBot.Tests.Domain.UnityNews;

public class BlogLinksTests
{
    [Fact]
    public void EncodeTextFragment_EncodesTheCharactersTheSyntaxUses()
    {
        var encoded = BlogLinks.EncodeTextFragment("Why we're stuck, reloading - A & B (v2)");

        Assert.Equal("Why%20we%27re%20stuck%2C%20reloading%20%2D%20A%20%26%20B%20%28v2%29", encoded);
    }

    [Fact]
    public void EncodeTextFragment_EncodesNonAsciiAsUtf8()
    {
        Assert.Equal("Th%C3%A9o", BlogLinks.EncodeTextFragment("Théo"));
    }

    [Fact]
    public void ChapterUrl_AppendsTheFragment_ReplacingAnExistingOne()
    {
        Assert.Equal("https://unity.com/blog/x#:~:text=Try%20it%20out", BlogLinks.ChapterUrl("https://unity.com/blog/x#old", "Try it out"));
    }

    [Fact]
    public void ChapterUrl_LongHeadingIsCutAtAWordBoundary()
    {
        var url = BlogLinks.ChapterUrl("https://unity.com/blog/x", "Rendering a mature society within mobile memory budgets");

        Assert.Equal("https://unity.com/blog/x#:~:text=Rendering%20a%20mature%20society%20within%20mobile", url);
    }

    private sealed record Item(string Id, int Day);

    [Fact]
    public void PendingPosts_UnpostedOnlyOldestFirst_AtMostMax()
    {
        // Feed order is newest first, the 3rd and 4th share a day.
        var feed = new[] { new Item("e", 5), new Item("d", 4), new Item("c", 4), new Item("b", 2), new Item("a", 1) };

        var pending = BlogLinks.PendingPosts(feed, i => i.Id, i => new DateTimeOffset(2026, 11, i.Day, 0, 0, 0, TimeSpan.Zero), new List<string> { "b" }, 3);

        // The older feed entry of the shared day goes first; "e" waits for the next check.
        Assert.Equal(["a", "c", "d"], pending.Select(i => i.Id));
    }

    [Fact]
    public void PendingPosts_NothingNew_IsEmpty()
    {
        var pending = BlogLinks.PendingPosts([new Item("a", 1)], i => i.Id, _ => DateTimeOffset.UnixEpoch, new List<string> { "a" }, 5);

        Assert.Empty(pending);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    public void DeferUnreadablePage_DefersBelowTheLimit_ThenPosts(int failedAttempts, bool defer)
    {
        Assert.Equal(defer, BlogLinks.DeferUnreadablePage(failedAttempts, 6));
    }
}
