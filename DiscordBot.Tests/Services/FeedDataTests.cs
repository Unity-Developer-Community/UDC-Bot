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
    public void AddPostedId_KeepsOnlyTheNewest()
    {
        var data = new FeedData();
        for (var i = 0; i < FeedData.MaxPostedIds + 25; i++)
            data.AddPostedId($"id{i}");

        Assert.Equal(FeedData.MaxPostedIds, data.PostedIds.Count);
        Assert.Equal("id25", data.PostedIds[0]);
        Assert.Equal($"id{FeedData.MaxPostedIds + 24}", data.PostedIds[^1]);
    }
}
