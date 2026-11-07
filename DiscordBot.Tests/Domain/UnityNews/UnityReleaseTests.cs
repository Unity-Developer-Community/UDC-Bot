using DiscordBot.Domain.UnityNews;
using Newtonsoft.Json;

namespace DiscordBot.Tests.Domain.UnityNews;

public class UnityReleaseTests
{
    [Theory]
    [InlineData("6000.3.28f1", "6000.3")]
    [InlineData("6000.7.0b8", "6000.7")]
    [InlineData("7000.0.0a16", "7000.0")]
    [InlineData("6000.3.27f2", "6000.3")]
    [InlineData("2022.3.62f1", "2022.3")]
    public void Line_IsMajorAndMinor(string version, string line)
    {
        Assert.Equal(line, UnityVersion.Line(version));
    }

    [Theory]
    [InlineData("6000.3.28f1", "Unity 6.3")]
    [InlineData("6000.0.85f2", "Unity 6.0")]
    [InlineData("6000.7.0b8", "Unity 6.7")]
    [InlineData("7000.0.0a16", "Unity 7.0")]
    [InlineData("2022.3.62f1", "Unity 2022.3")]
    public void DisplayLine_UsesMarketingName(string version, string display)
    {
        Assert.Equal(display, UnityVersion.DisplayLine(version));
    }

    [Theory]
    [InlineData("6000.7.0b8", "b8")]
    [InlineData("7000.0.0a16", "a16")]
    [InlineData("6000.3.28f1", "6000.3.28f1")]
    public void Short_DropsTheLineOnlyForPreReleases(string version, string expected)
    {
        Assert.Equal(expected, UnityVersion.Short(version));
    }

    [Fact]
    public void Release_TitleAndPageUrl_FollowTheStream()
    {
        var lts = UnityNewsFixtures.Release("6000.3.28f1", UnityStream.Lts);
        var beta = UnityNewsFixtures.Release("6000.7.0b8", UnityStream.Beta);
        var alpha = UnityNewsFixtures.Release("7000.0.0a16", UnityStream.Alpha);
        var supported = UnityNewsFixtures.Release("6000.6.8f1", UnityStream.Supported);

        Assert.Equal("LTS · 6000.3.28f1", lts.Title);
        Assert.Equal("Beta · 6000.7.0b8", beta.Title);
        Assert.Equal("Alpha · 7000.0.0a16", alpha.Title);
        Assert.Equal("Supported · 6000.6.8f1", supported.Title);
        Assert.Equal("https://unity.com/releases/editor/whats-new/6000.3.28f1", lts.PageUrl);
        Assert.Equal("https://unity.com/releases/editor/beta/6000.7.0b8", beta.PageUrl);
        Assert.Equal("https://unity.com/releases/editor/alpha/7000.0.0a16", alpha.PageUrl);
        Assert.Equal("https://unity.com/releases/editor/whats-new/6000.6.8f1", supported.PageUrl);
    }

    [Fact]
    public void ApiPage_Deserializes_AndSkipsUnknownStreams()
    {
        const string json = """
            {"offset":0,"limit":3,"total":69,"results":[
              {"version":"6000.6.8f1","releaseDate":"2026-11-03T16:15:27.205Z","releaseNotes":{"url":"https://storage.googleapis.com/notes/6000_6_8f1.md","type":"MD"},"stream":"SUPPORTED","recommended":true,"shortRevision":"237b6593792b"},
              {"version":"6000.7.0b8","releaseDate":"2026-11-05T10:01:00Z","releaseNotes":{"url":"https://storage.googleapis.com/notes/6000_7_0b8.md"},"stream":"BETA","recommended":false},
              {"version":"1.0.0","releaseDate":"2026-11-05T10:01:00Z","releaseNotes":null,"stream":"TECH","recommended":false},
              {"version":"6000.3.28f1","releaseDate":"2026-11-03T16:15:00Z","stream":"LTS"}
            ]}
            """;

        var releases = JsonConvert.DeserializeObject<UnityReleaseApiPage>(json)!.ToReleases();

        Assert.Equal(["6000.6.8f1", "6000.7.0b8", "6000.3.28f1"], releases.Select(r => r.Version));
        Assert.Equal(UnityStream.Supported, releases[0].Stream);
        Assert.True(releases[0].Recommended);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 16, 15, 27, 205, TimeSpan.Zero), releases[0].ReleaseDate);
        Assert.Equal("https://storage.googleapis.com/notes/6000_6_8f1.md", releases[0].NotesUrl);
        Assert.Equal(UnityStream.Beta, releases[1].Stream);
        Assert.Null(releases[2].NotesUrl);
    }
}
