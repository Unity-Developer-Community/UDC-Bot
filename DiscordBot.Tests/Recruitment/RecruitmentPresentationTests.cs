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
        Assert.DoesNotContain("trusted", text); Assert.DoesNotContain("no red flags", text);
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
