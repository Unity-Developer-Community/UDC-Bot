using DiscordBot.Services.Recruitment;
using DiscordBot.Services.Recruitment.Domain.Analysis;
using DiscordBot.Utils;

namespace DiscordBot.Tests.Recruitment;

public class ListingAnalysisTests
{
    [Theory]
    [InlineData("github.com", true)]
    [InlineData("USER.GitHub.Com", true)]
    [InlineData("fakegithub.com", false)]
    [InlineData("github.com.example.org", false)]
    public void DomainMatchingRequiresRootOrDotBoundary(string host, bool expected) =>
        Assert.Equal(expected, DomainName.Matches(host, "github.com"));

    [Fact]
    public async Task EmptyRegistryChangesChecksButPreservesSeedBasedSummaryAndEvidence()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        var post = await rig.Post();
        var live = rig.Discord.Threads[post.Id] with { Starter = "https://github.com/Example/CaseSensitive?utm_source=chat" };
        var analysis = ListingAnalysis.Read(live, "paid-for-hire", new HashSet<string>());
        Assert.False(Assert.Single(analysis.Portfolio.Candidates).Supported);
        Assert.Equal("work-sample link detected", analysis.PortfolioSummary);
        Assert.Equal("https://github.com/Example/CaseSensitive", Assert.Single(analysis.ContactLinks));
        Assert.Equal(RecruitmentContent.Observations(live.Starter), analysis.Observations);
    }

    [Fact]
    public void LiteralDisplayTruncatesBeforeReplacingControlAndFormattingCharacters()
    {
        Assert.Equal("A   ", DiscordDisplayText.Literal("A\n@*Z", 4));
        Assert.Equal("café 🦆", DiscordDisplayText.Literal("café 🦆"));
    }
}
