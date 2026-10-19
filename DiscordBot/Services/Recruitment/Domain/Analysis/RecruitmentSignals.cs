namespace DiscordBot.Services.Recruitment;

public enum ObservationSeverity { Grey, Green, Yellow, Red }
public enum ObservationApplicability { Available, Unavailable, NotApplicable }
public sealed record RecruitmentObservation(string Id, string Label, ObservationSeverity Severity,
    ObservationApplicability Applicability, string Value, string? Evidence = null);

/// <summary>Factual advisory signals only. No result is an eligibility or enforcement input.</summary>
public static class RecruitmentSignals
{
    public static List<RecruitmentObservation> Evaluate(ThreadSnapshot live, string purpose, IReadOnlySet<string>? supportedHosts = null)
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
            var high = origin.AddMonths(highMonths); var low = origin.AddMonths(lowMonths);
            Add(id, label, posting >= high ? ObservationSeverity.Green : posting >= low ? ObservationSeverity.Yellow : ObservationSeverity.Red,
                posting >= high ? $"At least {highMonths / 12} year{(highMonths == 12 ? "" : "s")}" : posting >= low ? $"At least {lowMonths} months" : $"Under {lowMonths} months");
        }
        Age("account-age", "Account Age", live.AccountCreatedAt, 24, 6);
        Age("join-date", "Join Date", live.JoinedAt, 12, 2, true);
        if (live.MemberRoleCount is not { } roles || roles < 0) Unknown("server-roles", "Server Roles");
        else Add("server-roles", "Server Roles", roles > 0 ? ObservationSeverity.Green : ObservationSeverity.Red, $"{roles} Role{(roles == 1 ? "" : "s")}");
        var payment = RecruitmentContent.ReadPayment(live.Starter);
        var compensationRed = payment.Kind is CompensationKind.Missing or CompensationKind.Unpaid or CompensationKind.RevenueShare;
        if (!paid) Na("compensation", "Compensation");
        else if (payment.Kind == CompensationKind.Unavailable) Unknown("compensation", "Compensation");
        else Add("compensation", "Compensation", payment.Kind == CompensationKind.Rate ? ObservationSeverity.Green : compensationRed ? ObservationSeverity.Red : ObservationSeverity.Yellow,
            payment.Kind switch { CompensationKind.Rate => "Rate Stated", CompensationKind.Mixed => "Mixed Terms", CompensationKind.MonetaryPromise => "Rate Unclear", CompensationKind.Unpaid => "Unpaid / Exposure", CompensationKind.RevenueShare => "Revenue Share Only", _ => "No Monetary Pay Found" }, payment.Evidence);
        var duration = RecruitmentContent.Duration(live.Starter, hire);
        if (duration is null) Unknown("duration", "Job Duration");
        else Add("duration", "Job Duration", duration.Length > 0 ? ObservationSeverity.Green : paid && compensationRed ? ObservationSeverity.Red : ObservationSeverity.Yellow,
            duration.Length > 0 ? "Window Stated" : "Timing Unclear", duration);
        var portfolio = RecruitmentContent.ReadPortfolio(live.Starter, live.Attachments, supportedHosts);
        if (!hire) Na("portfolio", "Portfolio");
        else if (!portfolio.Available) Unknown("portfolio", "Portfolio");
        else
        {
            var supported = portfolio.Candidates.FirstOrDefault(c => c.Specific && c.Supported && !c.Negated);
            var any = portfolio.Candidates.Any(c => !c.Negated && !IsContactOnly(c.Url));
            Add("portfolio", "Portfolio", supported is not null ? ObservationSeverity.Green : any || portfolio.MediaSample || !paid ? ObservationSeverity.Yellow : ObservationSeverity.Red,
                supported is not null ? "Platform Link" : portfolio.MediaSample ? "Samples Attached" : any ? "External Link" : "No Samples Found", supported?.Url);
        }
        var length = RecruitmentContent.NormalizedLength(live.Starter);
        var dm = RecruitmentContent.DmRequest(live.Starter);
        if (dm is null) Unknown("dm-requests", "DM Requests");
        else Add("dm-requests", "DM Requests", dm.Length == 0 ? ObservationSeverity.Green : length < 120 ? ObservationSeverity.Red : ObservationSeverity.Yellow,
            dm.Length == 0 ? "No DM-first Request" : "DM-first Request", dm);
        if (live.HasCustomAvatar is not { } avatar) Unknown("profile-image", "Profile Image");
        else Add("profile-image", "Profile Image", avatar ? ObservationSeverity.Green : ObservationSeverity.Yellow, avatar ? "Custom Avatar" : "Default Avatar");
        if (length is null) Unknown("post-detail", "Post Detail");
        else Add("post-detail", "Post Detail", length >= 300 ? ObservationSeverity.Green : length >= 120 ? ObservationSeverity.Yellow : ObservationSeverity.Red,
            $"{length} Characters", "Normalized body length is a heuristic, not proof of completeness or quality.");
        return result;
    }

    private static bool IsContactOnly(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        new[] { "/contact", "/login", "/signin", "/invite", "/join" }.Contains(uri.AbsolutePath.TrimEnd('/').ToLowerInvariant());

    public static string? SafeIdentity(string? value, int max = 100)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var safe = RecruitmentPresentation.Safe(value, max).Trim();
        return safe.Length == 0 ? null : safe;
    }
    public static string? SafeAvatarUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host is "cdn.discordapp.com" or "media.discordapp.net" && value.Length <= 500 ? value : null;
}
