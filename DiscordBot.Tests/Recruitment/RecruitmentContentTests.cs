using DiscordBot.Services.Recruitment;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Recruitment;

[TestClass]
public sealed class RecruitmentContentTests
{
    [TestMethod]
    [DataRow("Budget AUD 500")]
    [DataRow("My rate is 30/hr")]
    [DataRow("Pay: USD 30–50 per hour")]
    [DataRow("Salary: 80k GBP annually")]
    [DataRow("Fixed budget $2,500")]
    [DataRow("Budget is 2k-3k")]
    [DataRow("Day rate: 300 EUR")]
    [DataRow("Rate: $30.50/hr")]
    public void RecognizesConcreteRates(string content) =>
        Assert.AreEqual(RecruitmentPaymentSignal.Concrete,
            RecruitmentContentAnalyzer.Analyze(content, RecruitmentForumKind.PaidRecruiting));

    [TestMethod]
    [DataRow("DM for rates")]
    [DataRow("Competitive salary, negotiable")]
    [DataRow("Revenue share of $500 if we sell enough")]
    [DataRow("20% rev-share")]
    public void VagueAndContingentPay_RemainsAmbiguous(string content) =>
        Assert.AreEqual(RecruitmentPaymentSignal.Ambiguous,
            RecruitmentContentAnalyzer.Analyze(content, RecruitmentForumKind.PaidForHire));

    [TestMethod]
    [DataRow("Launching in 2026. Contact +61 123 456 789")]
    [DataRow("Portfolio: https://example.test/USD500")]
    [DataRow("We need 3 artists for 2 months")]
    public void UnrelatedNumbersAndUrls_AreNotRates(string content) =>
        Assert.AreEqual(RecruitmentPaymentSignal.Missing,
            RecruitmentContentAnalyzer.Analyze(content, RecruitmentForumKind.PaidRecruiting));

    [TestMethod]
    public void MissingOrOversizedInput_IsUnknown_AndHobbyNeedsNoPay()
    {
        Assert.AreEqual(RecruitmentPaymentSignal.Unknown,
            RecruitmentContentAnalyzer.Analyze(null, RecruitmentForumKind.PaidRecruiting));
        Assert.AreEqual(RecruitmentPaymentSignal.Unknown,
            RecruitmentContentAnalyzer.Analyze(new string('x', 8001), RecruitmentForumKind.PaidRecruiting));
        Assert.AreEqual(RecruitmentPaymentSignal.NotApplicable,
            RecruitmentContentAnalyzer.Analyze("No payment", RecruitmentForumKind.HobbyForHire));
    }
}
