using DiscordBot.Services.Recruitment.Domain.Analysis;
using DiscordBot.Utils;
namespace DiscordBot.Services.Recruitment;

public enum ObservationSeverity { Grey, Green, Yellow, Red }
public enum ObservationApplicability { Available, Unavailable, NotApplicable }
public sealed record RecruitmentObservation(string Id, string Label, ObservationSeverity Severity,
    ObservationApplicability Applicability, string Value, string? Evidence = null);

/// <summary>Factual advisory signals only. No result is an eligibility or enforcement input.</summary>
public static class RecruitmentSignals
{
    public static List<RecruitmentObservation> Evaluate(ThreadSnapshot live, string purpose, IReadOnlySet<string>? supportedHosts = null)
        => Evaluate(live, purpose, ListingAnalysis.Read(live, purpose, supportedHosts));

    public static List<RecruitmentObservation> Evaluate(ThreadSnapshot live, string purpose, ListingAnalysis analysis)
    {
        var paid = purpose.StartsWith("paid-", StringComparison.Ordinal);
        var hire = purpose.EndsWith("for-hire", StringComparison.Ordinal);
        var result = new List<RecruitmentObservation>();
        void Add(string id, string label, ObservationSeverity color, string value, string? evidence = null,
            ObservationApplicability applicability = ObservationApplicability.Available) => result.Add(new(id, label, color, applicability, value, evidence));
        void Unknown(string id, string label) => Add(id, label, ObservationSeverity.Grey, "Unavailable", applicability: ObservationApplicability.Unavailable);
        void Na(string id, string label) => Add(id, label, ObservationSeverity.Grey, "Not Applicable", applicability: ObservationApplicability.NotApplicable);
        void Age(string id, string label, DateTimeOffset? date, int highMonths, int lowMonths, bool join = false)
        {
            if (date is not { } start) { Unknown(id, label); return; }
            var posting = live.CreatedAt.UtcDateTime;
            var origin = start.UtcDateTime;
            if (origin > posting)
            {
                if (join) Add(id, label, ObservationSeverity.Grey, "Rejoined after posting", applicability: ObservationApplicability.Unavailable);
                else Unknown(id, label);
                return;
            }
            if (posting >= origin.AddMonths(highMonths))
                Add(id, label, ObservationSeverity.Green, $"At least {highMonths / 12} year{(highMonths == 12 ? "" : "s")}");
            else if (posting >= origin.AddMonths(lowMonths))
                Add(id, label, ObservationSeverity.Yellow, $"At least {lowMonths} months");
            else Add(id, label, ObservationSeverity.Red, $"Under {lowMonths} months");
        }
        Age("account-age", "Account Age", live.AccountCreatedAt, 24, 6);
        Age("join-date", "Join Date", live.JoinedAt, 12, 2, true);
        if (live.MemberRoleCount is not { } roles || roles < 0) Unknown("server-roles", "Server Roles");
        else Add("server-roles", "Server Roles", roles > 0 ? ObservationSeverity.Green : ObservationSeverity.Red, $"{roles} Role{(roles == 1 ? "" : "s")}");
        var payment = analysis.Payment;
        var compensationRed = payment.Kind is CompensationKind.Missing or CompensationKind.Unpaid or CompensationKind.RevenueShare;
        if (!paid) Na("compensation", "Compensation");
        else if (payment.Kind == CompensationKind.Unavailable) Unknown("compensation", "Compensation");
        else Add("compensation", "Compensation", CompensationSeverity(payment.Kind),
            payment.Kind switch { CompensationKind.Rate => "Rate Stated", CompensationKind.Mixed => "Mixed Terms", CompensationKind.MonetaryPromise => "Rate Unclear", CompensationKind.Unpaid => "Unpaid / Exposure", CompensationKind.RevenueShare => "Revenue Share Only", _ => "No Monetary Pay Found" }, payment.Evidence);
        var duration = analysis.Duration;
        if (duration is null) Unknown("duration", "Job Duration");
        else Add("duration", "Job Duration", DurationSeverity(duration, paid && compensationRed),
            duration.Length > 0 ? "Window Stated" : "Timing Unclear", duration);
        var portfolio = analysis.Portfolio;
        if (!hire) Na("portfolio", "Portfolio");
        else if (!portfolio.Available) Unknown("portfolio", "Portfolio");
        else
        {
            var supported = portfolio.Candidates.FirstOrDefault(c => c.Specific && c.Supported && !c.Negated);
            var any = portfolio.Candidates.Any(c => !c.Negated && !IsContactOnly(c.Url));
            Add("portfolio", "Portfolio", PortfolioSeverity(supported is not null, any || portfolio.MediaSample || !paid),
                PortfolioValue(supported is not null, portfolio.MediaSample, any), supported?.Url);
        }
        var length = analysis.NormalizedLength;
        var dm = analysis.DmRequest;
        if (dm is null) Unknown("dm-requests", "DM Requests");
        else Add("dm-requests", "DM Requests", DmSeverity(dm, length),
            dm.Length == 0 ? "No DM-first Request" : "DM-first Request", dm);
        if (live.HasCustomAvatar is not { } avatar) Unknown("profile-image", "Profile Image");
        else Add("profile-image", "Profile Image", avatar ? ObservationSeverity.Green : ObservationSeverity.Yellow, avatar ? "Custom Avatar" : "Default Avatar");
        if (length is null) Unknown("post-detail", "Post Detail");
        else Add("post-detail", "Post Detail", DetailSeverity(length.Value),
            $"{length} Characters", "Normalized body length is a heuristic, not proof of completeness or quality.");
        return result;
    }

    private static ObservationSeverity CompensationSeverity(CompensationKind kind) => kind switch
    {
        CompensationKind.Rate => ObservationSeverity.Green,
        CompensationKind.Missing or CompensationKind.Unpaid or CompensationKind.RevenueShare => ObservationSeverity.Red,
        _ => ObservationSeverity.Yellow
    };
    private static ObservationSeverity DurationSeverity(string duration, bool lacksPay)
    {
        if (duration.Length > 0) return ObservationSeverity.Green;
        return lacksPay ? ObservationSeverity.Red : ObservationSeverity.Yellow;
    }
    private static ObservationSeverity PortfolioSeverity(bool supported, bool hasPossibleSamples)
    {
        if (supported) return ObservationSeverity.Green;
        return hasPossibleSamples ? ObservationSeverity.Yellow : ObservationSeverity.Red;
    }
    private static string PortfolioValue(bool supported, bool media, bool link)
    {
        if (supported) return "Platform Link";
        if (media) return "Samples Attached";
        return link ? "External Link" : "No Samples Found";
    }
    private static ObservationSeverity DmSeverity(string request, int? length)
    {
        if (request.Length == 0) return ObservationSeverity.Green;
        return length < 120 ? ObservationSeverity.Red : ObservationSeverity.Yellow;
    }
    private static ObservationSeverity DetailSeverity(int length) => length switch
    {
        >= 300 => ObservationSeverity.Green, >= 120 => ObservationSeverity.Yellow, _ => ObservationSeverity.Red
    };

    private static bool IsContactOnly(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        new[] { "/contact", "/login", "/signin", "/invite", "/join" }.Contains(uri.AbsolutePath.TrimEnd('/').ToLowerInvariant());

    public static string? SafeIdentity(string? value, int max = 100)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var safe = DiscordDisplayText.Literal(value, max).Trim();
        return safe.Length == 0 ? null : safe;
    }
    public static string? SafeAvatarUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host is "cdn.discordapp.com" or "media.discordapp.net" && value.Length <= 500 ? value : null;
}
