using System.Text.RegularExpressions;

namespace DiscordBot.Services.Recruitment;

public static class RecruitmentContentAnalyzer
{
    public const int MaximumInputLength = 8000;
    private const string Amount = @"\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?";
    private const string Currency = @"(?:USD|AUD|CAD|NZD|EUR|GBP|JPY|INR|dollars?|euros?|pounds?)";
    private const string Unit = @"(?:/\s*(?:hr|hour|day|week|month|year)|per\s+(?:hour|day|week|month|year)|hourly|daily|annually)\b";
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex Url = Create(@"https?://\S+");
    private static readonly Regex Concrete = Create(
        @"(?:[$£€¥]\s*(?:" + Amount + @")\s*k?\b|\b" + Currency + @"\s*(?:" + Amount + @")\s*k?\b|" +
        @"(?<![\w.])(?:" + Amount + @")\s*k?(?:\s*[-–]\s*(?:" + Amount + @")\s*k?)?\s*(?:" + Currency + @"\b|" + Unit + @"))");
    private static readonly Regex Payment = Create(@"\b(?:pay|paid|payment|budget|rate|rates|salary|compensation|competitive|negotiable|rev[ -]?share|revenue[ -]?share)\b");
    private static readonly Regex BudgetRange = Create(@"\bbudget\b.{0,20}\b\d+(?:\.\d+)?k\s*[-–]\s*\d+(?:\.\d+)?k\b");
    private static readonly Regex RevenueShare = Create(@"\b(?:rev|revenue)[ -]?share\b");

    public static RecruitmentPaymentSignal Analyze(string? content, RecruitmentForumKind forum)
    {
        if (!RecruitmentForumClassifier.IsPaid(forum)) return RecruitmentPaymentSignal.NotApplicable;
        if (content is null) return RecruitmentPaymentSignal.Unknown;
        if (content.Length > MaximumInputLength) return RecruitmentPaymentSignal.Unknown;
        try
        {
            var text = Url.Replace(content, " ");
            // A number alone is never evidence of pay. Revenue-share prose remains
            // ambiguous unless a separate guaranteed payment statement is detected.
            var statements = Regex.Split(text, @"[\r\n;.!](?!\d)", RegexOptions.None, Timeout);
            if (statements.Any(s => !RevenueShare.IsMatch(s) && (Concrete.IsMatch(s) || BudgetRange.IsMatch(s))))
                return RecruitmentPaymentSignal.Concrete;
            return Payment.IsMatch(text) ?
                RecruitmentPaymentSignal.Ambiguous : RecruitmentPaymentSignal.Missing;
        }
        catch (RegexMatchTimeoutException)
        {
            return RecruitmentPaymentSignal.Unknown;
        }
    }

    private static Regex Create(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
}
