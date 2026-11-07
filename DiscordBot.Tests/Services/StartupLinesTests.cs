using DiscordBot.Services;
using DiscordBot.Services.Code.Tips;
using DiscordBot.Services.Profiles;

namespace DiscordBot.Tests.Services;

/// <summary>Lines services post into the startup report.</summary>
public class StartupLinesTests
{
    private const long Mb = 1024 * 1024;

    [Fact]
    public void TipDirectorySummary_Empty_ShowsZero()
    {
        Assert.Equal("0 tip files, 0 MB of 1024 MB.", TipService.DirectorySummary(0, 0, 1024 * Mb));
    }

    [Fact]
    public void TipDirectorySummary_WithinLimit()
    {
        Assert.Equal("1 tip file, 3.5 MB of 1024 MB.", TipService.DirectorySummary(1, (long)(3.5 * Mb), 1024 * Mb));
    }

    [Fact]
    public void TipDirectorySummary_OverLimit_Warns()
    {
        Assert.Equal("12 tip files, 11 MB, over the 10 MB limit: no new tips can be added this session.",
            TipService.DirectorySummary(12, 11 * Mb, 10 * Mb));
    }

    [Fact]
    public void DatabaseConnectedSummary_LeavesOutCreatedTables()
    {
        Assert.Equal("Connected: 5 users, 1 badge, 3 casino users.", DatabaseService.ConnectedSummary(5, 1, 3));
        Assert.Equal("Connected: 0 users, 2 casino users.", DatabaseService.ConnectedSummary(0, null, 2));
        Assert.Equal("Connected.", DatabaseService.ConnectedSummary(null, null, null));
    }

    [Fact]
    public void KarmaLastResetsLine_NeverReset()
    {
        var weekly = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
        var line = KarmaResetService.LastResetsLine(weekly, DateTime.MinValue, DateTime.MinValue);
        Assert.Equal($"Last resets: weekly <t:{new DateTimeOffset(weekly).ToUnixTimeSeconds()}:R>, monthly never, yearly never.", line);
    }
}
