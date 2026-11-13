using Discord;
using DiscordBot.Domain.UnityNews;
using DiscordBot.Services.Code.Unity;
using DiscordBot.Services.Server;
using DiscordBot.Tests.Domain.UnityNews;

namespace DiscordBot.Tests.Services;

public class UnityNewsBlogCardsTests
{
    private static readonly DateTimeOffset Published = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly BlogPage FullPage = BlogPageParser.Parse(BlogFixtures.H2Page);

    private static MessageComponent Card(BlogPage page, string? role = null, string? description = "Klang Games profiles real player-built societies.") =>
        UnityNewsCards.BlogCard("How SEED renders a world", BlogFixtures.ArticleUrl, description, Published, page, role);

    private static IEnumerable<IMessageComponent> Flatten(IEnumerable<IMessageComponent> components) =>
        components.SelectMany(c => c switch
        {
            ContainerComponent container => new[] { c }.Concat(Flatten(container.Components)),
            ActionRowComponent row => new[] { c }.Concat(Flatten(row.Components)),
            _ => [c],
        });

    [Fact]
    public void BlogCard_ShowsAuthorDateReadTimeTopicsAndChapterLinks()
    {
        var text = AuditLogCardsTests.Text(Card(FullPage, "<@&123>"));

        Assert.StartsWith("<@&123>\n## [How SEED renders a world](https://unity.com/blog/how-seed-renders)", text);
        Assert.Contains("> Klang Games profiles real player-built societies.", text);
        Assert.Contains("**Adam Axler** · Senior Content Marketing Manager and 1 other", text);
        Assert.Contains($"<t:{new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()}:D> · 6 min read", text);
        Assert.Contains("**Topics:** Platforms and publishing · Programming", text);
        Assert.Contains("**In this post**", text);
        Assert.Contains("1. [A world built from tiny pieces](https://unity.com/blog/how-seed-renders#:~:text=A%20world%20built%20from%20tiny%20pieces)", text);
        Assert.Contains("2. [Why we're stuck, reloading - a & b](https://unity.com/blog/how-seed-renders#:~:text=Why%20we%27re%20stuck%2C%20reloading%20%2D%20a%20%26%20b)", text);
    }

    [Fact]
    public void BlogCard_HasTheGalleryOfFourImages_AndTheLinkButton_WithinLimits()
    {
        var card = Card(FullPage);
        var parts = Flatten(card.Components).ToList();

        var gallery = Assert.Single(parts.OfType<MediaGalleryComponent>());
        Assert.Equal(4, gallery.Items.Count);
        var button = Assert.Single(parts.OfType<ButtonComponent>());
        Assert.Equal("Read on unity.com", button.Label);
        Assert.Equal(BlogFixtures.ArticleUrl, button.Url);
        Assert.True(AuditLogCardsTests.Text(card).Length <= AuditCard.DiscordTextLimit);
        Assert.True(parts.Count <= 40);
    }

    [Fact]
    public void BlogCard_WithoutPage_IsTheFeedItemAlone()
    {
        var card = Card(BlogPage.Empty, description: new string('x', 1500));
        var text = AuditLogCardsTests.Text(card);
        var parts = Flatten(card.Components).ToList();

        Assert.Contains("## [How SEED renders a world]", text);
        Assert.DoesNotContain("In this post", text);
        Assert.DoesNotContain("min read", text);
        Assert.Empty(parts.OfType<MediaGalleryComponent>());
        Assert.Single(parts.OfType<ButtonComponent>());
        Assert.True(text.Length <= AuditCard.DiscordTextLimit);
        // The date still shows, from the feed.
        Assert.Contains(":D>", text);
    }

    [Fact]
    public void BlogCard_ChaptersAllNumbered_DropTheirOwnNumber_ButStillLinkToTheHeading()
    {
        var page = BlogPage.Empty with { Chapters = ["1. One", "2. Two"] };

        var text = AuditLogCardsTests.Text(Card(page));

        Assert.Contains("1. [One](https://unity.com/blog/how-seed-renders#:~:text=", text);
        Assert.Contains("2. [Two](https://unity.com/blog/how-seed-renders#:~:text=", text);
        Assert.DoesNotContain("[1. One]", text);
        // The link still finds the heading with its number.
        Assert.Contains("text=1.%20One", text);
    }

    [Fact]
    public void BlogCard_ChaptersNotAllNumbered_KeepTheirText()
    {
        var page = BlogPage.Empty with { Chapters = ["1. One", "Two"] };

        var text = AuditLogCardsTests.Text(Card(page));

        Assert.Contains("1. [1. One](", text);
        Assert.Contains("2. [Two](", text);
    }

    [Fact]
    public void BlogCard_NoRoleMeansNoMention()
    {
        Assert.DoesNotContain("<@&", AuditLogCardsTests.Text(Card(FullPage)));
    }

    [Fact]
    public void BlogCard_DefusesMentionsAndBracketsFromThePage()
    {
        var page = new BlogPage { Chapters = ["Hello @everyone [x]", "<@123> two"], Authors = [new BlogAuthor("@here", "<@&5>")] };

        var text = AuditLogCardsTests.Text(UnityNewsCards.BlogCard("T [1]", BlogFixtures.ArticleUrl, "ping @everyone", Published, page));

        // Ordinal: the culture-aware default of the assertions ignores the zero-width space the defusing adds.
        Assert.DoesNotContain("@everyone", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@here", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<@", text, StringComparison.Ordinal);
        Assert.Contains("(x)", text);
    }

    [Fact]
    public void BlogCard_ManyLongChapters_StayWithinTheTextLimit()
    {
        var page = new BlogPage
        {
            Chapters = Enumerable.Range(1, 8).Select(i => $"Chapter {i} " + new string('é', 150)).ToList(),
            Authors = [new BlogAuthor(new string('a', 500), new string('r', 500))],
            Topics = Enumerable.Range(1, 30).Select(i => "Topic " + i).ToList(),
        };

        var text = AuditLogCardsTests.Text(Card(page, "<@&123>", new string('d', 3000)));

        Assert.True(text.Length <= AuditCard.DiscordTextLimit);
    }

    private const string ForumPostUrl = "https://discord.com/channels/1/2";

    private static MessageComponent DiscussionCard(IReadOnlyList<string> images, string? description = "Klang Games profiles real player-built societies.") =>
        UnityNewsCards.BlogDiscussionCard("How SEED renders a world", BlogFixtures.ArticleUrl, description, images, ForumPostUrl);

    [Fact]
    public void BlogDiscussionCard_StartsWithTheTitleLink_AndHasTheDescription_NoRoleOrChapters()
    {
        var text = AuditLogCardsTests.Text(DiscussionCard(FullPage.Images));

        Assert.StartsWith("## [How SEED renders a world](https://unity.com/blog/how-seed-renders)", text);
        Assert.Contains("Klang Games profiles real player-built societies.", text);
        Assert.DoesNotContain("<@&", text);
        Assert.DoesNotContain("In this post", text);
        Assert.DoesNotContain("min read", text);
    }

    [Fact]
    public void BlogDiscussionCard_HasTwoLinkButtons()
    {
        var buttons = Flatten(DiscussionCard(FullPage.Images).Components).OfType<ButtonComponent>().ToList();

        Assert.Equal(["Read on unity.com", "Discuss in unity-news"], buttons.Select(b => b.Label));
        Assert.Equal([BlogFixtures.ArticleUrl, ForumPostUrl], buttons.Select(b => b.Url));
    }

    [Fact]
    public void BlogDiscussionCard_GalleryHoldsTheImages_AndIsLeftOutWithNone()
    {
        var withImages = Flatten(DiscussionCard(FullPage.Images).Components).OfType<MediaGalleryComponent>().ToList();
        var gallery = Assert.Single(withImages);
        Assert.Equal(4, gallery.Items.Count);

        var tooMany = Enumerable.Range(1, 6).Select(i => $"https://unity.com/{i}.png").ToList();
        Assert.Equal(4, Assert.Single(Flatten(DiscussionCard(tooMany).Components).OfType<MediaGalleryComponent>()).Items.Count);

        Assert.Empty(Flatten(DiscussionCard([]).Components).OfType<MediaGalleryComponent>());
    }

    [Fact]
    public void BlogDiscussionCard_LongDescription_StaysWithinTheLimit_AndDefusesMentions()
    {
        var longText = AuditLogCardsTests.Text(DiscussionCard(FullPage.Images, new string('d', 5000)));
        Assert.True(longText.Length <= AuditCard.DiscordTextLimit);

        var text = AuditLogCardsTests.Text(DiscussionCard([], "ping @everyone <@123>"));
        Assert.DoesNotContain("@everyone", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<@", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BlogTagNames_AreOnlyTheBlogTag()
    {
        Assert.Equal(["Unity Blog"], UnityNewsCards.BlogTagNames());
    }

    [Fact]
    public void BlogForumTitle_KeepsThePrefixAndTheLimit()
    {
        Assert.Equal("Blog - Hello", UnityNewsCards.BlogForumTitle(" Hello "));
        Assert.True(UnityNewsCards.BlogForumTitle(new string('t', 300)).Length <= UnityNewsCards.MaxForumTitleLength);
    }
}
