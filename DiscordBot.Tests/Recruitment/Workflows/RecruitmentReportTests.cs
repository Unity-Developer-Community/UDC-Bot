using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentReportTests
{
    [Fact]
    public async Task ReportsRemainPrivateAndDeduplicateAcrossRestarts()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Ack();
        var before = rig.Discord.Messages["100:recruitment:100:public"].Message.RenderKey;
        const string concern = "They asked for an application deposit in DMs.";
        Assert.Contains("saved for moderators", await rig.Engine.ReportAsync("1", "100", "201", concern, default));
        Assert.Equal(before, rig.Discord.Messages["100:recruitment:100:public"].Message.RenderKey);
        Assert.DoesNotContain(concern, await rig.Engine.DetailsAsync("1", "100", "200", default));
        Assert.Contains(concern, await rig.Staff("lookup"));
        Assert.Contains("private reports", rig.Engine.Health().Text());
        await rig.Restart();
        Assert.Contains("already reported", await rig.Engine.ReportAsync("1", "100", "201", concern, default));
        Assert.Single(rig.State.Listings[post.Id].Reports);
        Assert.Single(rig.Discord.Messages.Values, m => m.Message.Marker.Contains(":attention:"));
    }

    [Fact]
    public async Task ReporterMustBeCurrentMemberOfCorrectGuildAndCannotReportOwnListing()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post();
        Assert.Contains("own listing", await rig.Engine.ReportAsync("1", "100", "200", "Concern details", default));
        Assert.Contains("unavailable", await rig.Engine.ReportAsync("2", "100", "201", "Concern details", default));
        rig.Discord.MissingMembers.Add("201");
        Assert.Contains("not a current", await rig.Engine.ReportAsync("1", "100", "201", "Concern details", default));
        Assert.Contains("unavailable", await rig.Engine.DetailsAsync("1", "100", "201", default));
        Assert.Empty(rig.State.Listings["100"].Reports);
        var controls = await rig.Engine.OwnerDetailsAsync("1", "100", "201", default);
        Assert.False(controls.Open); Assert.False(controls.Removable);
    }

    [Fact]
    public async Task PerReporterLimitSurvivesRestartAndExpiresAfterOneDay()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        for (var i = 0; i < 4; i++) await rig.Post((100 + i).ToString(), (200 + i).ToString());
        for (var i = 0; i < 3; i++) Assert.Contains("saved", await rig.Engine.ReportAsync("1", (100 + i).ToString(), "300", "Concern details", default));
        await rig.Restart();
        Assert.Contains("three listing reports", await rig.Engine.ReportAsync("1", "103", "300", "Concern details", default));
        rig.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Contains("saved", await rig.Engine.ReportAsync("1", "103", "300", "Concern details", default));
    }

    [Fact]
    public async Task UnsafeFeedRejectsReportWithoutRetainingIt()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post();
        rig.Discord.FailFeed = true; rig.Engine.InvalidateAccess(); await rig.Sweep();
        Assert.Contains("temporarily unavailable", await rig.Engine.ReportAsync("1", "100", "201", "Concern details", default));
        Assert.Empty(rig.State.Listings["100"].Reports);
    }

    [Fact]
    public async Task LostNoticeResponseIsRecoveredOnceAndReportReviewIsAudited()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack();
        rig.Discord.LoseNextSendResponse = true;
        Assert.Contains("saved", await rig.Engine.ReportAsync("1", "100", "201", "Concern details", default));
        Assert.True(Assert.Single(rig.State.Listings["100"].Attention).SendPending);
        await rig.Restart(); rig.Clock.Advance(TimeSpan.FromMinutes(2)); await rig.Engine.RunDueAsync(default);
        var notice = Assert.Single(rig.State.Listings["100"].Attention);
        Assert.False(notice.SendPending); Assert.NotNull(notice.MessageId);
        Assert.Single(rig.Discord.Messages.Values, m => m.Message.Marker.Contains(":attention:"));
        Assert.Contains("Moderator access", await rig.Staff("resolve-reports", actor: "201"));
        Assert.Null(rig.State.Listings["100"].Reports[0].ResolvedAt);
        await rig.Staff("resolve-reports");
        Assert.NotNull(rig.State.Listings["100"].Reports[0].ResolvedAt);
        Assert.DoesNotContain("private reports", rig.Engine.Health().Text());
        Assert.Contains(rig.State.Listings["100"].Audit, a => a.Reason.Contains("resolve-reports"));
    }
}
