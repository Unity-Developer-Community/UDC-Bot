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

    [Fact]
    public async Task MaximumReaderNotesFitTextFallbackAndDoNotImplyClearance()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(forum: "12");
        post.Facts.Payment = "mixed compensation terms; clarify what is guaranteed";
        post.Facts.PaymentEvidence = new string('x', 363);
        post.Facts.Observations = new List<ContentObservation> {
            new("private-terms", "Post directs readers to private messages for terms", new string('x', 180)),
            new("applicant-fee", "Post mentions an applicant payment or fee", new string('x', 180)),
            new("payment-pressure", "Post combines payment with urgency", new string('x', 180)) };
        post.Hold = "Recovery needed";
        var message = RecruitmentPresentation.Public(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now);
        Assert.InRange(message.TextFallback!.Length, 1, 2000);
        var color = message.Embed!.Color;
        post.Facts.Observations.Clear(); post.Facts.Payment = "concrete rate or budget stated"; post.Facts.Portfolio = "work-sample link detected";
        var quiet = RecruitmentPresentation.Public(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now);
        Assert.Equal(color, quiet.Embed!.Color);
        Assert.DoesNotContain("trusted", quiet.TextFallback); Assert.DoesNotContain("no red flags", quiet.TextFallback);
    }

    [Fact]
    public async Task ForHireEmbedHighlightsMissingEvidenceWithoutImplyingEndorsement()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(forum: "12");
        post.Facts.AccountCreatedAt = post.CreatedAt.AddDays(-2); post.Facts.JoinedAt = post.CreatedAt;
        post.Facts.MemberRoleCount = 0; post.Facts.Payment = RecruitmentContent.Payment("DM for rates");
        var message = RecruitmentPresentation.Public(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now);
        var embed = Assert.IsType<Discord.Embed>(message.Embed);
        Assert.Equal(new Discord.Color(0x747F8D), embed.Color);
        Assert.Contains("Account Age", embed.Description);
        Assert.Contains("Clarify scope", embed.Description);
        Assert.Contains("No Samples Found", embed.Description);
        Assert.Contains("Server Roles", embed.Description);
        Assert.True(message.AcknowledgementRequired);
        Assert.DoesNotContain("Initial snapshot", message.TextFallback);
        Assert.DoesNotContain("Community activity", message.TextFallback);
        Assert.DoesNotContain("Protected from", message.TextFallback);
        Assert.Contains("identity and work are unverified", embed.Footer!.Value.Text);
        Assert.True(message.TextFallback!.Length < 2000);
    }

    [Fact]
    public async Task HobbyRecruitingDoesNotDemandPaymentOrAForHirePortfolio()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(forum: "13");
        post.Facts.MemberRoleCount = 2; post.Facts.Payment = RecruitmentContent.Payment("Unpaid volunteer project");
        var embed = RecruitmentPresentation.Public(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now).Embed!;
        Assert.DoesNotContain(embed.Fields, f => f.Name.StartsWith("⚠") || f.Name.Contains("Portfolio"));
        Assert.Equal(new Discord.Color(0x747F8D), embed.Color);
        post.Facts.Payment = RecruitmentContent.Payment("Revenue share");
        Assert.Contains("Not Applicable", RecruitmentPresentation.Public(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now).Embed!.Description);
    }

    [Fact]
    public async Task StarterAndRoleCorrectionsRefreshEmbedFieldsWithoutScanningForumsOrDuplicatingControls()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(forum: "12"); await rig.Ack();
        var messageId = post.PublicMessageId; var inventories = rig.Discord.Inventories;
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { Starter = "Portfolio https://example.org/works. Rates USD 50/hour", MemberRoleCount = 3 };
        await rig.Engine.FactsChangedAsync(post.Id, default);
        var message = rig.Discord.Messages["100:recruitment:100:public"].Message;
        Assert.DoesNotContain("No portfolio", message.Embed!.Description);
        Assert.False(message.AcknowledgementRequired); Assert.True(message.ReaderControls);
        Assert.Contains("3 Roles", RecruitmentPresentation.Details(post));
        Assert.Equal(messageId, post.PublicMessageId); Assert.Equal(inventories, rig.Discord.Inventories);
        var writes = rig.Discord.Upserts;
        await rig.Engine.FactsChangedAsync(post.Id, default);
        Assert.Equal(writes, rig.Discord.Upserts);
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { MemberRoleCount = 0 };
        await rig.Engine.FactsChangedAsync(post.Id, default);
        Assert.Contains("0 Roles", RecruitmentPresentation.Details(post));
        Assert.Contains("0 Roles", rig.Discord.Messages["100:recruitment:100:public"].Message.Embed!.Description);
    }
}
