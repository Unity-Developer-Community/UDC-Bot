using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentContentTests
{
    [Theory]
    [InlineData("Unpaid volunteer project. The game will sell for $20. DM for details.", "unpaid collaboration stated")]
    [InlineData("The game sells for $20", "amount mentioned; compensation unclear")]
    [InlineData("Application fee $20 to join", "amount mentioned; compensation unclear")]
    [InlineData("Budget: AUD 500 for the project", "concrete rate or budget stated")]
    [InlineData("My rate is $40/hour", "concrete rate or budget stated")]
    [InlineData("Unpaid role. Budget $500 for some tasks", "mixed compensation terms; clarify what is guaranteed")]
    [InlineData("Revenue share, plus a rate of $30/hour", "mixed compensation terms; clarify what is guaranteed")]
    [InlineData("Revenue share only", "revenue-share stated")]
    public void PaymentNeedsCompensationContextAndPreservesConflicts(string content, string summary)
    {
        var observation = RecruitmentContent.ReadPayment(content);
        Assert.Equal(summary, observation.Summary); Assert.NotEmpty(observation.Evidence);
    }

    [Theory]
    [InlineData("No portfolio available; apply at https://example.org/contact", "external link found; purpose unclear")]
    [InlineData("Portfolio coming soon: https://example.org/contact", "external link found; purpose unclear")]
    [InlineData("Not my work: https://artstation.com/artist", "external link found; purpose unclear")]
    [InlineData("My portfolio: https://example.org/work", "work-sample link detected")]
    public void NegatedWorkClaimsDoNotClearSampleCheck(string content, string expected) => Assert.Equal(expected, RecruitmentContent.Portfolio(content));

    [Theory]
    [InlineData("image/png", "media sample attached")]
    [InlineData("video/mp4", "media sample attached")]
    [InlineData("audio/ogg", "media sample attached")]
    [InlineData("application/octet-stream", "no work-sample link found")]
    public void MediaAttachmentsCanSupplySamplesWithoutTreatingExecutablesAsProof(string type, string expected) =>
        Assert.Equal(expected, RecruitmentContent.Portfolio("Samples attached", new[] { new StarterAttachment("1", "sample", type) }));

    [Theory]
    [InlineData("DM for rates and payment terms", "private-terms")]
    [InlineData("Payment details only in DMs", "private-terms")]
    [InlineData("Pay an application fee of $10", "applicant-fee")]
    [InlineData("No refunds for the application fee", "applicant-fee")]
    [InlineData("Send money immediately to reserve the job", "payment-pressure")]
    public void BehavioralObservationsRetainAnExcerpt(string content, string code)
    {
        var observation = Assert.Single(RecruitmentContent.Observations(content), o => o.Code == code);
        Assert.Contains(content, observation.Evidence);
    }

    [Theory]

    [InlineData("No application fees or deposits required.")]
    [InlineData("Do not DM for payment details; terms must stay public.")]
    [InlineData("Never send money immediately to someone you do not know.")]
    public void OrdinaryContactAndExplicitSafetyAdviceAreNotPressure(string content) => Assert.Empty(RecruitmentContent.Observations(content));

    [Fact]
    public void DmToApplyIsPublicFirstClarificationWithoutInventingPaymentPressure()
    {
        const string text = "Budget USD 500. Deliver in two weeks. DM to apply with your portfolio.";
        Assert.Empty(RecruitmentContent.Observations(text));
        Assert.NotEmpty(RecruitmentContent.DmRequest(text)!);
    }

    [Fact]
    public void HostRootsAndOverlongContentDoNotCreateBroadContactMatches()
    {
        Assert.Empty(RecruitmentContent.ContactLinks("https://github.com https://example.org/"));
        Assert.Equal(new[] { "https://example.org/my-project" }, RecruitmentContent.ContactLinks("https://example.org/my-project?tracking=one https://example.org/my-project?tracking=two"));
        Assert.Empty(RecruitmentContent.Observations(new string('a', 8000) + " DM for rates"));
    }
}
