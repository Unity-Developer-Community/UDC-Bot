using DiscordBot.Utils;

namespace DiscordBot.Services.Recruitment.Domain.Analysis;

/// <summary>One bounded parse per observed starter. Display summaries retain their seed-host semantics.</summary>
public sealed record ListingAnalysis(PaymentObservation Payment, PortfolioEvidence Portfolio, string PortfolioSummary,
    string? Duration, string? DmRequest, int? NormalizedLength, List<ContentObservation> Observations,
    List<string> ContactLinks, string CompensationTerms)
{
    public static ListingAnalysis Read(ThreadSnapshot live, string purpose, IReadOnlySet<string>? supportedHosts = null)
    {
        var payment = RecruitmentContent.ReadPayment(live.Starter);
        var portfolio = RecruitmentContent.ReadPortfolio(live.Starter, live.Attachments, supportedHosts);
        // Current checks use the persisted registry. The historical summary has always used seed hosts.
        var legacy = portfolio with { Candidates = portfolio.Candidates.Select(c => c with
            { Supported = RecruitmentContent.SeedPortfolioHosts.Any(h => DomainName.Matches(c.Host, h)) }).ToArray() };
        return new(payment, portfolio, RecruitmentContent.PortfolioSummary(legacy),
            RecruitmentContent.Duration(live.Starter, purpose.EndsWith("for-hire", StringComparison.Ordinal)),
            RecruitmentContent.DmRequest(live.Starter), RecruitmentContent.NormalizedLength(live.Starter),
            RecruitmentContent.Observations(live.Starter, payment), RecruitmentContent.ContactLinks(portfolio),
            live.Starter is null ? "" : RecruitmentContent.CompensationTerms(live.Starter));
    }
}
