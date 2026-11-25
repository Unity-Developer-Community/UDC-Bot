using DiscordBot.Domain.UnityNews;

namespace DiscordBot.Tests.Domain.UnityNews;

public class BlogDiscussionFilterTests
{
    private static string? Excluded(string title, string[]? topics = null, string[]? tags = null) =>
        BlogDiscussionFilter.ExcludedBy(title, topics ?? [], tags ?? [],
            BlogDiscussionFilter.DefaultBlockedTopics, BlogDiscussionFilter.DefaultBlockedTitleWords);

    // Titles, topics and tags of real posts from unity.com/blog, November 2026.
    [Theory]
    [InlineData("Is your data telling you the whole story?", new[] { "Monetization", "User acquisition" }, new string[0])]
    [InlineData("Real-time 3D with Unity Industry: What to vibe code with Unity CLI and when to rely on the foundational infrastructure", new[] { "CLI" }, new string[0])]
    [InlineData("Access Asset Store and AI Generators using the Unity CLI", new[] { "Asset Store", "CLI" }, new string[0])]
    [InlineData("The Unity CLI 1.0 is here!", new[] { "AI" }, new[] { "AI features" })]
    [InlineData("Meeting you where you work: Unity Plugin for Grok Build", new[] { "AI" }, new[] { "AI features" })]
    // No topics on the page: the title has to catch these.
    [InlineData("The inflection point: why mobile gaming is becoming essential to the digital advertising mix", new string[0], new string[0])]
    [InlineData("How AI Is Actually Reshaping Mobile Gaming Advertising", new string[0], new string[0])]
    [InlineData("Rethinking the funnel: where mobile gaming really fits in the marketing mix", new string[0], new string[0])]
    [InlineData("From attention to action: why the state of play matters for marketers", new string[0], new string[0])]
    [InlineData("10 tips for organizing your first video game project with AI", new[] { "Game design" }, new string[0])]
    public void UninterestingPosts_AreExcluded(string title, string[] topics, string[] tags)
    {
        Assert.NotNull(Excluded(title, topics, tags));
    }

    [Theory]
    [InlineData("How to playtest your first video game", new[] { "Game design" }, new string[0])]
    [InlineData("Tearing through reality: The tech behind Hyper Visor on XREAL AURA", new[] { "Platforms and publishing" }, new[] { "Target platforms" })]
    [InlineData("How bosses and enemies decide what to do in video games", new[] { "Game design" }, new[] { "Programming" })]
    [InlineData("Skipping domain reload: faster iteration using the MethodReload feature", new string[0], new string[0])]
    [InlineData("Echo Weaver: Building a time-loop metroidbrainia in Unity 6", new string[0], new string[0])]
    [InlineData("How Ares Interactive runs live ops without waiting on client updates", new[] { "Platforms and publishing" }, new[] { "Target platforms" })]
    public void InterestingPosts_GetACard(string title, string[] topics, string[] tags)
    {
        Assert.Null(Excluded(title, topics, tags));
    }

    [Theory]
    [InlineData("Downloads and loads of uploads")] // "ads" inside words
    [InlineData("What she said about Unity 6")] // "ai" inside a word
    public void BlockedWords_MatchWholeWordsOnly(string title)
    {
        Assert.Null(Excluded(title));
    }

    [Fact]
    public void Reason_NamesTheRule()
    {
        Assert.Equal("topic or tag \"Monetization\"", Excluded("Data", ["Monetization"]));
        Assert.NotNull(Excluded("Data", ["monetization"]));
        Assert.Equal("\"advertising\" in the title", Excluded("The digital advertising mix"));
    }

    [Fact]
    public void CustomRules_ReplaceTheDefaults()
    {
        Assert.Null(BlogDiscussionFilter.ExcludedBy("The Unity CLI 1.0 is here!", ["AI"], [], [], []));
        Assert.NotNull(BlogDiscussionFilter.ExcludedBy("Unity Industry summit", [], [], [], ["Unity Industry"]));
    }
}
