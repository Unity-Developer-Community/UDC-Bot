using DiscordBot.Domain.UnityNews;

namespace DiscordBot.Tests.Domain.UnityNews;

public class BlogPageParserTests
{
    [Fact]
    public void Parse_ReadsHeaderAndLdFields()
    {
        var page = BlogPageParser.Parse(BlogFixtures.H2Page);

        Assert.Equal("How SEED renders a world", page.Headline);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero), page.Published);
        Assert.Equal(6, page.DeclaredReadMinutes);
        Assert.Equal(6, page.ReadMinutes);
        Assert.Equal(["Platforms and publishing", "Programming"], page.Topics);
        Assert.Empty(page.Tags);
        // The role is trimmed; the author list comes from the header, which has roles.
        Assert.Equal([new BlogAuthor("Adam Axler", "Senior Content Marketing Manager"), new BlogAuthor("Mike Geig", "Principal Advocate")], page.Authors);
    }

    [Fact]
    public void Parse_H2Page_UsesTheH2HeadingsAsChapters()
    {
        var page = BlogPageParser.Parse(BlogFixtures.H2Page);

        // The h3 headings, the duplicate and the empty heading are left out.
        Assert.Equal(["A world built from tiny pieces", "Why we're stuck, reloading - a & b"], page.Chapters);
    }

    [Fact]
    public void Parse_H3OnlyPage_UsesTheHighestLevelPresent()
    {
        var page = BlogPageParser.Parse(BlogFixtures.H3OnlyPage);

        Assert.Equal(["First", "Second"], page.Chapters);
    }

    [Fact]
    public void Parse_ChaptersAreCapped()
    {
        var headings = string.Concat(Enumerable.Range(1, 12).Select(i => $"<h2>Chapter {i}</h2>"));

        var page = BlogPageParser.Parse(BlogFixtures.Page(headings));

        Assert.Equal(BlogPageParser.MaxChapters, page.Chapters.Count);
        Assert.Equal("Chapter 8", page.Chapters[^1]);
    }

    [Fact]
    public void Parse_Images_HeroFirstThenBodyImages_NoAvatarsOrIconsOrDuplicates_MaxFour()
    {
        var page = BlogPageParser.Parse(BlogFixtures.H2Page);

        Assert.Equal(
            new[] { BlogFixtures.Hero, BlogFixtures.Body1, BlogFixtures.Body2, BlogFixtures.Body3 }.Select(u => u + "?w=1280&auto=format"),
            page.Images);
    }

    [Fact]
    public void Parse_PageWithoutFlightData_KeepsLdAndBodyDetails_AndEstimatesTheReadTime()
    {
        var page = BlogPageParser.Parse(BlogFixtures.NoFlightPage);

        Assert.Equal([new BlogAuthor("Adam Axler", null)], page.Authors);
        Assert.Empty(page.Topics);
        Assert.Null(page.DeclaredReadMinutes);
        Assert.Equal("One", Assert.Single(page.Chapters));
        Assert.InRange(page.WordCount, 460, 462);
        // 461 words at 230 a minute.
        Assert.Equal(2, page.ReadMinutes);
        Assert.Equal(BlogFixtures.Hero + "?w=1280&auto=format", Assert.Single(page.Images));
    }

    [Fact]
    public void Parse_StatedReadTimeIsMissing_EstimatesFromTheWords()
    {
        var page = BlogPageParser.Parse(BlogFixtures.NoMinutesPage);

        Assert.Null(page.DeclaredReadMinutes);
        Assert.Equal(4, page.ReadMinutes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not html at all")]
    [InlineData("<html><script type=\"application/ld+json\">{broken</script><script>self.__next_f.push([1,\"\\\"readDuration\\\" {\\\"backButton\\\"\"])</script></html>")]
    [InlineData("<script>self.__next_f.push(</script><script>self.__next_f.push([1,5])</script>")]
    public void Parse_UnusablePage_IsEmptyAndNeverThrows(string? html)
    {
        var page = BlogPageParser.Parse(html);

        Assert.True(page.IsEmpty);
        Assert.Null(page.ReadMinutes);
    }
}
