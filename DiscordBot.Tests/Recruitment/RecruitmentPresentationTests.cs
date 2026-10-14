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
    public async Task ForHireEmbedHighlightsMissingEvidenceWithoutImplyingEndorsement()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(forum: "12");
        post.Facts.AccountCreatedAt = post.CreatedAt.AddDays(-2); post.Facts.JoinedAt = post.CreatedAt;
        post.Facts.MemberRoleCount = 0; post.Facts.Payment = RecruitmentContent.Payment("DM for rates");
        var message = RecruitmentPresentation.Public(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now);
        var embed = Assert.IsType<Discord.Embed>(message.Embed);
        Assert.Equal(Discord.Color.Orange, embed.Color);
        Assert.Equal(5, embed.Fields.Count(f => f.Name.StartsWith("⚠")));
        Assert.Contains("2 days at posting", embed.Fields[0].Value);
        Assert.Contains("Clarify scope", embed.Fields.Last().Value);
        Assert.DoesNotContain("Initial snapshot", message.TextFallback);
        Assert.DoesNotContain("Community activity", message.TextFallback);
        Assert.DoesNotContain("Protected from", message.TextFallback);
        Assert.Contains("do not verify identity", embed.Footer!.Value.Text);
        Assert.True(message.TextFallback!.Length < 2000);
    }

    [Fact]
    public async Task HobbyRecruitingDoesNotDemandPaymentOrAForHirePortfolio()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(forum: "13");
        post.Facts.MemberRoleCount = 2; post.Facts.Payment = RecruitmentContent.Payment("Unpaid volunteer project");
        var embed = RecruitmentPresentation.Public(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now).Embed!;
        Assert.DoesNotContain(embed.Fields, f => f.Name.StartsWith("⚠") || f.Name.Contains("Portfolio"));
        Assert.Equal(Discord.Color.Blue, embed.Color);
        post.Facts.Payment = RecruitmentContent.Payment("Revenue share");
        Assert.Contains(RecruitmentPresentation.Public(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now).Embed!.Fields,
            f => f.Name == "⚠ Payment" && f.Value.Contains("no guaranteed payment"));
    }

    [Fact]
    public async Task StarterAndRoleCorrectionsRefreshEmbedFieldsWithoutScanningForumsOrDuplicatingControls()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(forum: "12"); await rig.Ack();
        var messageId = post.PublicMessageId; var inventories = rig.Discord.Inventories;
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { Starter = "Portfolio https://example.org/works. Rates USD 50/hour", MemberRoleCount = 3 };
        await rig.Engine.FactsChangedAsync(post.Id, default);
        var message = rig.Discord.Messages["100:recruitment:100:public"].Message;
        Assert.Contains(message.Embed!.Fields, f => f.Name == "Portfolio / work samples");
        Assert.Contains(message.Embed.Fields, f => f.Name == "Server roles" && f.Value == "3 assigned");
        Assert.Equal(messageId, post.PublicMessageId); Assert.Equal(inventories, rig.Discord.Inventories);
        var writes = rig.Discord.Upserts;
        await rig.Engine.FactsChangedAsync(post.Id, default);
        Assert.Equal(writes, rig.Discord.Upserts);
        rig.Discord.Threads[post.Id] = rig.Discord.Threads[post.Id] with { MemberRoleCount = 0 };
        await rig.Engine.FactsChangedAsync(post.Id, default);
        Assert.Contains(rig.Discord.Messages["100:recruitment:100:public"].Message.Embed!.Fields, f => f.Name == "⚠ Server roles");
    }
}
