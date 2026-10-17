using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentSignalTests
{
    private static ThreadSnapshot Snapshot(string? text = "Budget $500. Project lasts 2 weeks.") => new("100", "12", "200", "Role",
        DateTimeOffset.Parse("2026-02-28T12:00:00Z"), false, false, false, false, Array.Empty<string>(), text,
        DateTimeOffset.Parse("2024-02-29T12:00:00Z"), DateTimeOffset.Parse("2025-02-28T12:00:00Z"),
        MemberRoleCount: 1, HasCustomAvatar: true);
    private static RecruitmentObservation Check(ThreadSnapshot live, string id, string purpose = "paid-for-hire") =>
        RecruitmentSignals.Evaluate(live, purpose).Single(c => c.Id == id);

    [Fact]
    public void StableNineChecksUseUtcCalendarAnniversariesIncludingLeapDates()
    {
        var live = Snapshot(); var checks = RecruitmentSignals.Evaluate(live, "paid-for-hire");
        Assert.Equal(9, checks.Count); Assert.Equal(9, checks.Select(c => c.Id).Distinct().Count());
        Assert.Equal(ObservationSeverity.Green, Check(live, "account-age").Severity);
        Assert.Equal(ObservationSeverity.Green, Check(live, "join-date").Severity);
        Assert.Equal(ObservationSeverity.Yellow, Check(live with { CreatedAt = live.CreatedAt.AddDays(-1) }, "account-age").Severity);
        Assert.Equal(ObservationSeverity.Yellow, Check(live with { CreatedAt = live.CreatedAt.AddDays(-1) }, "join-date").Severity);
        Assert.Equal(ObservationSeverity.Yellow, Check(live with { AccountCreatedAt = live.CreatedAt.AddMonths(-6) }, "account-age").Severity);
        Assert.Equal(ObservationSeverity.Red, Check(live with { AccountCreatedAt = live.CreatedAt.AddMonths(-6).AddTicks(1) }, "account-age").Severity);
        Assert.Equal(ObservationSeverity.Yellow, Check(live with { JoinedAt = live.CreatedAt.AddMonths(-2) }, "join-date").Severity);
        Assert.Equal(ObservationSeverity.Red, Check(live with { JoinedAt = live.CreatedAt.AddMonths(-2).AddTicks(1) }, "join-date").Severity);
        Assert.Equal(ObservationSeverity.Yellow, Check(live with { CreatedAt = live.CreatedAt.AddTicks(-1) }, "account-age").Severity);
        Assert.Contains("Rejoined", Check(live with { JoinedAt = live.CreatedAt.AddHours(1) }, "join-date").Value);
        Assert.Equal(ObservationApplicability.Unavailable, Check(live with { AccountCreatedAt = live.CreatedAt.AddHours(1) }, "account-age").Applicability);
        Assert.Contains("Rejoined", Check(live with { JoinedAt = live.CreatedAt.AddDays(1) }, "join-date").Value);
        Assert.Equal(ObservationApplicability.Unavailable, Check(live with { AccountCreatedAt = live.CreatedAt.AddDays(1) }, "account-age").Applicability);
    }

    [Theory]
    [InlineData(null, ObservationApplicability.Unavailable, ObservationSeverity.Grey)]
    [InlineData("", ObservationApplicability.Available, ObservationSeverity.Red)]
    [InlineData("Budget $500", ObservationApplicability.Available, ObservationSeverity.Green)]
    [InlineData("Payment will be discussed", ObservationApplicability.Available, ObservationSeverity.Yellow)]
    [InlineData("Paid in exposure", ObservationApplicability.Available, ObservationSeverity.Red)]
    [InlineData("Budget $500. Never pay an application fee", ObservationApplicability.Available, ObservationSeverity.Green)]
    [InlineData("Exposure only", ObservationApplicability.Available, ObservationSeverity.Red)]
    [InlineData("Revenue share only", ObservationApplicability.Available, ObservationSeverity.Red)]
    [InlineData("Budget $500 plus revenue share", ObservationApplicability.Available, ObservationSeverity.Yellow)]
    [InlineData("We do not pay", ObservationApplicability.Available, ObservationSeverity.Red)]
    [InlineData("We do not pay $50/hour", ObservationApplicability.Available, ObservationSeverity.Red)]
    [InlineData("Not an unpaid role. Budget $500", ObservationApplicability.Available, ObservationSeverity.Green)]
    [InlineData("Exposure to Unity required. Budget $500", ObservationApplicability.Available, ObservationSeverity.Green)]
    [InlineData("Paid work plus revenue share", ObservationApplicability.Available, ObservationSeverity.Yellow)]
    [InlineData("Application fee $500", ObservationApplicability.Available, ObservationSeverity.Red)]
    [InlineData("Our game sells for $500", ObservationApplicability.Available, ObservationSeverity.Red)]
    public void CompensationIsContextual(string? text, ObservationApplicability applicability, ObservationSeverity severity)
    { var c = Check(Snapshot(text), "compensation"); Assert.Equal(applicability, c.Applicability); Assert.Equal(severity, c.Severity); }

    [Theory]
    [InlineData("Project lasts 2 weeks", true)]
    [InlineData("Duration: 2 weeks", true)]
    [InlineData("3-month project", true)]
    [InlineData("Deliver in two weeks", true)]
    [InlineData("Ongoing work", true)]
    [InlineData("Available for long-term contracts", true)]
    [InlineData("Available from 2026-10-20", true)]
    [InlineData("I am 2 years old on this server", false)]
    [InlineData("Rate $50 per month", false)]
    [InlineData("We launch soon", false)]
    [InlineData("No ongoing work", false)]
    [InlineData("Not available for long-term contracts", false)]
    [InlineData("My birthday is 2026-10-20", false)]
    public void DurationNeedsEngagementContext(string text, bool found) =>
        Assert.Equal(found, Check(Snapshot(text), "duration").Severity == ObservationSeverity.Green);

    [Theory]
    [InlineData("DM to apply", true)]
    [InlineData("DM me", true)]
    [InlineData("Not a scam: DM to apply", true)]
    [InlineData("Contact via DMs", true)]
    [InlineData("Do not DM me; apply here", false)]
    [InlineData("Never DM for payment terms", false)]
    [InlineData("Do not apply via DMs", false)]
    [InlineData("DM only for private information", false)]
    [InlineData("Warning: scammers say 'DM to apply'", false)]
    public void DmFirstKeepsNegationAndPrivateInformationExceptions(string text, bool adverse) =>
        Assert.Equal(adverse ? ObservationSeverity.Red : ObservationSeverity.Green, Check(Snapshot(text), "dm-requests").Severity);

    [Theory]
    [InlineData(119, ObservationSeverity.Red)]
    [InlineData(120, ObservationSeverity.Yellow)]
    [InlineData(299, ObservationSeverity.Yellow)]
    [InlineData(300, ObservationSeverity.Green)]
    public void NormalizedBodyThresholds(int count, ObservationSeverity severity) => Assert.Equal(severity, Check(Snapshot(new string('a', count)), "post-detail").Severity);

    [Fact]
    public void DecorationAndUrlsCannotManufactureDetail()
    {
        Assert.Equal(3, RecruitmentContent.NormalizedLength("**a**    b https://example.org/" + new string('x', 900)));
        Assert.Equal(0, RecruitmentContent.NormalizedLength("https://example.org/" + new string('x', 2000)));
        Assert.Equal(0, RecruitmentContent.NormalizedLength(new string('-', 400)));
        Assert.Equal(0, RecruitmentContent.NormalizedLength(string.Concat(Enumerable.Repeat("- - - \n", 100))));
        Assert.Equal(ObservationSeverity.Yellow, Check(Snapshot("DM to apply " + new string('x', 300)), "dm-requests").Severity);
    }

    [Theory]
    [InlineData("paid-recruiting", ObservationApplicability.NotApplicable, ObservationApplicability.Available)]
    [InlineData("paid-for-hire", ObservationApplicability.Available, ObservationApplicability.Available)]
    [InlineData("hobby-recruiting", ObservationApplicability.NotApplicable, ObservationApplicability.NotApplicable)]
    [InlineData("hobby-for-hire", ObservationApplicability.Available, ObservationApplicability.NotApplicable)]
    public void ForumApplicability(string purpose, ObservationApplicability portfolio, ObservationApplicability compensation)
    { Assert.Equal(portfolio, Check(Snapshot(), "portfolio", purpose).Applicability); Assert.Equal(compensation, Check(Snapshot(), "compensation", purpose).Applicability); }

    [Fact]
    public void PortfolioUsesInjectedHostsAndDistinguishesAttachedFromSupportedSamples()
    {
        var live = Snapshot("Portfolio https://artist.example/works");
        Assert.Equal(ObservationSeverity.Yellow, Check(live, "portfolio").Severity);
        Assert.Equal(ObservationSeverity.Green, RecruitmentSignals.Evaluate(live, "paid-for-hire", new HashSet<string> { "artist.example" }).Single(c => c.Id == "portfolio").Severity);
        Assert.Equal(ObservationSeverity.Yellow, Check(Snapshot("") with { Attachments = new[] { new StarterAttachment("1", "demo", "image/png") } }, "portfolio").Severity);
        Assert.Equal(ObservationSeverity.Red, Check(Snapshot("Not my work https://artstation.com/artist"), "portfolio").Severity);
        Assert.Equal(ObservationSeverity.Yellow, Check(Snapshot("https://artist.example"), "portfolio").Severity);
        Assert.Equal(ObservationSeverity.Red, Check(Snapshot("https://artist.example/contact"), "portfolio").Severity);
        Assert.Equal(ObservationSeverity.Red, Check(Snapshot("https://discord.gg/team"), "portfolio").Severity);
        Assert.Equal(ObservationSeverity.Yellow, Check(Snapshot(""), "portfolio", "hobby-for-hire").Severity);
    }

    [Fact]
    public void UnknownMemberIsDistinctFromNoRolesOrDefaultAvatar()
    {
        Assert.Equal(ObservationSeverity.Red, Check(Snapshot() with { MemberRoleCount = 0 }, "server-roles").Severity);
        Assert.Equal(ObservationSeverity.Grey, Check(Snapshot() with { MemberRoleCount = null }, "server-roles").Severity);
        Assert.Equal(ObservationSeverity.Yellow, Check(Snapshot() with { HasCustomAvatar = false }, "profile-image").Severity);
        Assert.Equal(ObservationSeverity.Grey, Check(Snapshot() with { HasCustomAvatar = null }, "profile-image").Severity);
    }
}
