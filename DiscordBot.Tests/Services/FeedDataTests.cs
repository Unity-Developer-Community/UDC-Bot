using DiscordBot.Services.Code.Unity;
using DiscordBot.Domain.UnityNews;
using DiscordBot.Services;
using Newtonsoft.Json;

namespace DiscordBot.Tests.Services;

public class FeedDataTests
{
    [Fact]
    public void OldFormatFile_StillLoads()
    {
        const string json = """
            {"LastUnityReleaseCheck":"2026-11-07T10:00:00+10:00","LastUnityBlogCheck":"2026-11-07T10:05:00+10:00","PostedIds":["a","b"]}
            """;

        var data = JsonConvert.DeserializeObject<FeedData>(json)!;

        Assert.Equal(["a", "b"], data.PostedIds);
        Assert.Empty(data.ReleaseStreams);
        Assert.Equal(DateTimeOffset.MinValue, data.LastUnityReleaseCheckUtc);
        // An old file has never seeded the blog feed, so the first check records the feed instead of posting it.
        Assert.False(data.BlogSeeded);
    }

    [Fact]
    public void BlogSeeded_RoundTrips()
    {
        var loaded = JsonConvert.DeserializeObject<FeedData>(JsonConvert.SerializeObject(new FeedData { BlogSeeded = true }))!;

        Assert.True(loaded.BlogSeeded);
    }

    [Fact]
    public void ReleaseState_RoundTrips()
    {
        var data = new FeedData();
        var state = new ReleaseStreamState { Seeded = true, SeededThrough = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero) };
        state.Lines["6000.3"] = new ReleaseLineState
        {
            LastVersion = "6000.3.28f1",
            LastReleaseDate = state.SeededThrough,
            LastPostUtc = new DateTimeOffset(2026, 11, 5, 12, 5, 0, TimeSpan.Zero),
        };
        data.ReleaseStreams["Lts"] = state;

        var loaded = JsonConvert.DeserializeObject<FeedData>(JsonConvert.SerializeObject(data))!;

        var line = loaded.ReleaseStreams["Lts"].Lines["6000.3"];
        Assert.True(loaded.ReleaseStreams["Lts"].Seeded);
        Assert.Equal("6000.3.28f1", line.LastVersion);
        Assert.Equal(state.SeededThrough, line.LastReleaseDate);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 12, 5, 0, TimeSpan.Zero), line.LastPostUtc);
    }

    [Fact]
    public void BlogCardState_RoundTrips()
    {
        var card = new BlogCardData
        {
            Id = "id-1", Title = "A post", Url = "https://unity.com/blog/a", Description = "About it", Images = ["https://img/1"],
            ForumPostUrl = "https://discord.com/channels/1/2", PostedUtc = new DateTimeOffset(2026, 11, 14, 1, 0, 0, TimeSpan.Zero),
        };
        var data = new FeedData { LatestBlog = card, LastDiscussionCardMessageId = 1570702838030671953, LastDiscussionCardUtc = card.PostedUtc };
        data.PendingDiscussionCards.Add(card);

        var loaded = JsonConvert.DeserializeObject<FeedData>(JsonConvert.SerializeObject(data))!;

        Assert.Equal("A post", loaded.LatestBlog!.Title);
        Assert.Equal(["https://img/1"], loaded.LatestBlog.Images);
        Assert.Equal(card.PostedUtc, loaded.LatestBlog.PostedUtc);
        Assert.Equal("https://discord.com/channels/1/2", Assert.Single(loaded.PendingDiscussionCards).ForumPostUrl);
        Assert.Equal(1570702838030671953UL, loaded.LastDiscussionCardMessageId);
    }

    [Fact]
    public void OldFormatFile_HasNoBlogCardState()
    {
        var data = JsonConvert.DeserializeObject<FeedData>("""{"PostedIds":["a"]}""")!;

        Assert.Null(data.LatestBlog);
        Assert.Empty(data.PendingDiscussionCards);
        Assert.Equal(0UL, data.LastDiscussionCardMessageId);
    }

    [Fact]
    public void AddPostedId_KeepsOnlyTheNewest()
    {
        var data = new FeedData();
        for (var i = 0; i < FeedData.MaxPostedIds + 25; i++)
            data.AddPostedId($"id{i}");

        Assert.Equal(FeedData.MaxPostedIds, data.PostedIds.Count);
        Assert.Equal("id25", data.PostedIds[0]);
        Assert.Equal($"id{FeedData.MaxPostedIds + 24}", data.PostedIds[^1]);
    }

    [Fact]
    public void StartupLine_ListsStreamsChannelsAndUnseeded()
    {
        var data = new FeedData { BlogSeeded = true };
        data.ReleaseStreams["Lts"] = new ReleaseStreamState { Seeded = true };
        data.ReleaseStreams["Supported"] = new ReleaseStreamState { Seeded = true };
        data.ReleaseStreams["Alpha"] = new ReleaseStreamState { Seeded = true };
        var firstCheck = new DateTimeOffset(2026, 11, 7, 12, 0, 30, TimeSpan.Zero);

        var line = FeedService.StartupLine(data, 10, 0, 12, firstCheck);

        Assert.StartsWith("Releases (LTS, Supported, Beta, Alpha) → <#10>, blog → no channel set, discussion cards → <#12>; checks start <t:", line);
        Assert.EndsWith("Not seeded yet, so their first check posts nothing: Beta.", line);
    }

    [Fact]
    public void StartupLine_AllSeeded_NoSeedNote()
    {
        var data = new FeedData { BlogSeeded = true };
        foreach (var stream in new[] { "Lts", "Supported", "Beta", "Alpha" })
            data.ReleaseStreams[stream] = new ReleaseStreamState { Seeded = true };

        Assert.DoesNotContain("Not seeded", FeedService.StartupLine(data, 10, 11, 12, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void StartupLine_NewState_EverythingUnseeded()
    {
        Assert.EndsWith(": LTS, Supported, Beta, Alpha, blog.", FeedService.StartupLine(new FeedData(), 10, 11, 12, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void StartupLine_DiscussionCardsOffOrWithoutChannel()
    {
        var data = new FeedData { BlogSeeded = true };

        Assert.Contains("discussion cards → off;", FeedService.StartupLine(data, 10, 11, null, DateTimeOffset.UtcNow));
        Assert.Contains("discussion cards → no channel set;", FeedService.StartupLine(data, 10, 11, 0, DateTimeOffset.UtcNow));
    }
}
