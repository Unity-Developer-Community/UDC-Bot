using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentPresentationTests
{
    [Theory]
    [InlineData("DM me https://discord.gg/work", "no work-sample link found")]
    [InlineData("https://github.com/person/project", "work-sample link detected")]
    [InlineData("Portfolio: https://personal.example/my-work", "work-sample link detected")]
    [InlineData("https://artstation.com/artist", "work-sample link detected")]
    [InlineData("https://github.com", "external link found; purpose unclear")]
    [InlineData("https://github.com.example/artist", "external link found; purpose unclear")]
    [InlineData("Apply here: https://example.org/jobs", "external link found; purpose unclear")]
    [InlineData(null, "unknown")]
    public void WorkSampleDetectionDistinguishesInvitesAndUnlabelledLinks(string? content, string expected) =>
        Assert.Equal(expected, RecruitmentContent.Portfolio(content));

    private static string Text(RecruitmentMessage message) => Newtonsoft.Json.JsonConvert.SerializeObject(message.FallbackComponents);

    [Fact]
    public async Task PublicFallbackKeepsWarningsInstructionsAndAllObservations()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(forum: "12");
        post.Facts.Observations = new List<ContentObservation> { new("applicant-fee", "Applicant fee", "fee requested") };
        post.Hold = "Recovery needed";
        var message = RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now);
        var text = Text(message);
        Assert.Null(message.Embed); Assert.Empty(message.Content);
        Assert.Contains("Applicant fee", text); Assert.Contains("fee requested", text);
        Assert.Contains("Acknowledge", text); Assert.Contains("processing is paused", text);
        Assert.All(post.Facts.Checks, check => Assert.Contains(check.Label, text));
        Assert.Contains("**Job Duration:**", text);
        Assert.DoesNotContain("(Green)", text); Assert.DoesNotContain("(Yellow)", text);
        Assert.DoesNotContain("trusted", text); Assert.DoesNotContain("no red flags", text);
    }

    [Fact]
    public async Task LegendUsesCompactStatusIconsAndBoldLabelsWhilePreservingEvidenceAndUnknowns()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        post.Facts.Checks = new()
        {
            new("account-age", "Account Age", ObservationSeverity.Green, ObservationApplicability.Available, "At least 2 years"),
            new("duration", "Job Duration", ObservationSeverity.Yellow, ObservationApplicability.Available, "Timing Unclear"),
            new("compensation", "Compensation", ObservationSeverity.Red, ObservationApplicability.Available, "No Monetary Pay Found", "Unpaid position"),
            new("portfolio", "Portfolio", ObservationSeverity.Green, ObservationApplicability.NotApplicable, "Not Applicable"),
            new("join-date", "Join Date", ObservationSeverity.Red, ObservationApplicability.Unavailable, "Rejoined after posting"),
            new("post-detail", "Post Detail", ObservationSeverity.Yellow, ObservationApplicability.Available, "159 Characters",
                "Normalized body length is a heuristic, not proof of completeness or quality.")
        };
        post.Facts.Observations = new() { new("fee", "Applicant fee", "pay **$50** <@123>") };
        var text = RecruitmentPresentation.Details(post);
        Assert.Contains("🟢 **Account Age:** At least 2 years", text);
        Assert.Contains("🔶 **Job Duration:** Timing Unclear", text);
        Assert.Contains("🟥 **Compensation:** No Monetary Pay Found — Unpaid position", text);
        Assert.Contains("⚪ **Portfolio:** Not Applicable", text);
        Assert.Contains("⚪ **Join Date:** Rejoined after posting", text);
        Assert.Contains("**Applicant fee:**", text); Assert.Contains("$50", text); Assert.DoesNotContain("<@123>", text);
        Assert.DoesNotContain("Available /", text); Assert.DoesNotContain("NotApplicable /", text);
        Assert.DoesNotContain("Server Roles excludes everyone", text);
        Assert.DoesNotContain("Normalized body length is a heuristic", text);
        Assert.Contains("length does not prove quality", text); Assert.Contains("privately", text);
        Assert.InRange(text.Length, 1, 1400);
        Assert.Contains("Job Duration: Timing Unclear (Amber)", RecruitmentBanner.Alt(post.Facts.Checks));
    }

    [Fact]
    public async Task HobbyRecruitingKeepsNotApplicableFacts()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(forum: "13");
        var message = RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now);
        Assert.Contains("Not Applicable", Text(message));
    }

    [Fact]
    public async Task StarterAndRoleCorrectionsRefreshEmbedFieldsWithoutScanningForumsOrDuplicatingControls()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(forum: "12"); await rig.Ack();
        var messageId = post.PublicMessageId; var inventories = rig.Discord.Inventories;
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { Starter = "Portfolio https://example.org/works. Rates USD 50/hour", MemberRoleCount = 3 };
        await rig.Engine.FactsChangedAsync(post.Id, default);
        var message = rig.Discord.Messages["100:recruitment:100:public"].Message;
        Assert.DoesNotContain("No portfolio", Text(message));
        Assert.False(message.AcknowledgementRequired); Assert.True(message.ReaderControls);
        Assert.Contains("3 Roles", RecruitmentPresentation.Details(post));
        Assert.Equal(messageId, post.PublicMessageId); Assert.Equal(inventories, rig.Discord.Inventories);
        var writes = rig.Discord.Upserts;
        await rig.Engine.FactsChangedAsync(post.Id, default);
        Assert.Equal(writes, rig.Discord.Upserts);
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { MemberRoleCount = 0 };
        await rig.Engine.FactsChangedAsync(post.Id, default);
        Assert.Contains("0 Roles", RecruitmentPresentation.Details(post));
        Assert.Contains("0 Roles", Text(rig.Discord.Messages["100:recruitment:100:public"].Message));
    }
}
