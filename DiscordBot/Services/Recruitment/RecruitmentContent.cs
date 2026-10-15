using System.Text.RegularExpressions;

namespace DiscordBot.Services.Recruitment;

public sealed record ContentObservation(string Code, string Description, string Evidence);
public sealed record PaymentObservation(string Summary, string Evidence);
public sealed record StarterAttachment(string Id, string Name, string? ContentType);

public static partial class RecruitmentContent
{
    private static string Bounded(string text) => text[..Math.Min(text.Length, 8000)];
    private static string Excerpt(string text) => RecruitmentPresentation.Safe(text.Trim(), 180);
    public static string Payment(string? content) => ReadPayment(content).Summary;

    // An amount alone is not compensation. Labels and units provide context, never verification.
    public static PaymentObservation ReadPayment(string? content)
    {
        if (content is null) return new("unknown", "");
        var text = Bounded(content);
        try
        {
            var sentences = Sentences().Split(text);
            var rate = sentences.FirstOrDefault(s => Concrete().IsMatch(s) &&
                (PaymentLabel().IsMatch(s) || RateUnit().IsMatch(s)) && !OtherPrice().IsMatch(s) && !ApplicantFee().IsMatch(s));
            var unpaid = sentences.FirstOrDefault(s => Unpaid().IsMatch(s) && !(ApplicantFee().IsMatch(s) && NoFee().IsMatch(s)));
            var share = sentences.FirstOrDefault(s => RevenueShare().IsMatch(s));
            if (rate is not null && (unpaid is not null || share is not null))
                return new("mixed compensation terms; clarify what is guaranteed", Excerpt(rate) + " / " + Excerpt(unpaid ?? share!));
            if (unpaid is not null) return new("unpaid collaboration stated", Excerpt(unpaid));
            if (share is not null) return new("revenue-share stated", Excerpt(share));
            if (rate is not null) return new("concrete rate or budget stated", Excerpt(rate));
            var amount = sentences.FirstOrDefault(s => Concrete().IsMatch(s));
            if (amount is not null) return new("amount mentioned; compensation unclear", Excerpt(amount));
            var vague = sentences.FirstOrDefault(s => PaymentLabel().IsMatch(s));
            return vague is not null ? new("ambiguous / discuss payment terms", Excerpt(vague)) : new("no concrete rate or budget found", "");
        }
        catch (RegexMatchTimeoutException) { return new("unknown", ""); }
    }

    // A labelled reference or media attachment is a sample supplied, not proof of authorship.
    public static string Portfolio(string? content, IReadOnlyList<StarterAttachment>? attachments = null)
    {
        if (attachments?.Any(a => a.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ||
            a.ContentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true ||
            a.ContentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true) == true)
            return "media sample attached";
        if (content is null) return "unknown";
        var text = Bounded(content);
        var external = false;
        try
        {
            foreach (Match match in Links().Matches(text))
            {
                if (!TryLink(match.Value, out var uri) || Domain(uri!.IdnHost, "discord.com") || Domain(uri.IdnHost, "discord.gg") || Domain(uri.IdnHost, "discordapp.com")) continue;
                external = true;
                var workHost = new[] { "github.com", "gitlab.com", "artstation.com", "behance.net", "dribbble.com", "itch.io", "vimeo.com", "youtube.com", "youtu.be", "deviantart.com" }
                    .Any(domain => Domain(uri.IdnHost, domain));
                var lineStart = text.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
                var lineEnd = text.IndexOf('\n', match.Index);
                if (lineEnd < 0) lineEnd = text.Length;
                var line = text[lineStart..lineEnd];
                if (!NoWork().IsMatch(line) && (workHost && uri.AbsolutePath.Length > 1 || WorkLabel().IsMatch(line)))
                    return "work-sample link detected";
            }
            return external ? "external link found; purpose unclear" : "no work-sample link found";
        }
        catch (RegexMatchTimeoutException) { return "unknown"; }
    }

    public static List<ContentObservation> Observations(string? content)
    {
        var observations = new List<ContentObservation>();
        if (content is null) return observations;
        try
        {
            var sentences = Sentences().Split(Bounded(content));
            void Add(string code, string description, Func<string, bool> match)
            {
                var evidence = sentences.FirstOrDefault(match);
                if (evidence is not null) observations.Add(new(code, description, Excerpt(evidence)));
            }
            Add("private-terms", "Post directs readers to private messages for terms", s => PrivateTerms().IsMatch(s) && !NoPrivateTerms().IsMatch(s));
            Add("applicant-fee", "Post mentions an applicant payment or fee", s => ApplicantFee().IsMatch(s) && !NoFee().IsMatch(s));
            Add("payment-pressure", "Post combines payment with urgency", s => Urgency().IsMatch(s) && PaymentAction().IsMatch(s) && !NoPressure().IsMatch(s));
        }
        catch (RegexMatchTimeoutException) { /* Observations are optional, never enforcement evidence. */ }
        return observations;
    }

    public static string CompensationTerms(string content)
    {
        try
        {
            var terms = string.Join("\n", Sentences().Split(Bounded(content)).Where(s =>
                PaymentLabel().IsMatch(s) || Unpaid().IsMatch(s) || RevenueShare().IsMatch(s) || RateUnit().IsMatch(s)));
            return string.Join(' ', terms.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        }
        catch (RegexMatchTimeoutException) { return ""; }
    }

    public static List<string> ContactLinks(string? content)
    {
        if (content is null) return new();
        try
        {
            return Links().Matches(Bounded(content)).Select(m => TryLink(m.Value, out var uri) ? uri : null)
                .Where(u => u is not null && u.AbsolutePath.Length > 1)
                .Select(u => u!.GetLeftPart(UriPartial.Path).TrimEnd('/'))
                .Where(u => u.Length <= 300).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        }
        catch (RegexMatchTimeoutException) { return new(); }
    }
    private static bool TryLink(string value, out Uri? uri) => Uri.TryCreate(value.TrimEnd('.', ',', ';', '!', '?'), UriKind.Absolute, out uri) &&
        uri.Scheme is "https" or "http" && uri.HostNameType == UriHostNameType.Dns;
    private static bool Domain(string host, string domain) => host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?:[$€£¥]\s*\d+(?:[.,]\d+)*(?:\s*[kK])?|\b\d+(?:[.,]\d+)*\s*(?:USD|AUD|CAD|EUR|GBP|JPY)\b|\b(?:USD|AUD|CAD|EUR|GBP|JPY)\s*\d+)", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Concrete();
    [GeneratedRegex(@"\b(?:pay(?:ing|ment)?|paid|budget|compensation|salary|rates?|wages?|offer(?:ing)?)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex PaymentLabel();
    [GeneratedRegex(@"(?:/|\bper\s+)(?:hr|hour|day|week|month|asset|model|sprite|project)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex RateUnit();
    [GeneratedRegex(@"\b(?:sells? for|retail|game costs?|software costs?|licen[cs]e costs?|purchase price)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex OtherPrice();
    [GeneratedRegex(@"\b(?:rev(?:enue)?[ -]?share|royalt(?:y|ies)|profit[ -]?share)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex RevenueShare();
    [GeneratedRegex(@"\b(?:unpaid|volunteer|not paid|no (?:pay|payment|compensation))\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Unpaid();
    [GeneratedRegex(@"https?://[^\s<>\[\]()]+", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Links();
    [GeneratedRegex(@"\b(?:portfolio|showreel|demo|work[ -]?samples?|examples? of (?:my |our )?work|previous work|proof of work)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex WorkLabel();
    [GeneratedRegex(@"\b(?:no|without|don't have|do not have|not (?:my|our|a))\b.{0,35}\b(?:portfolio|samples?|work|demo|showreel)\b|\b(?:portfolio|samples?|work|demo|showreel)\b.{0,25}\b(?:unavailable|not available|coming soon)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex NoWork();
    [GeneratedRegex(@"\b(?:dm|pm|private messages?)\b.{0,35}\b(?:rates?|budget|pay|payment|terms|details)\b|\b(?:rates?|budget|payment|terms|details)\b.{0,35}\b(?:in|via|by|only)\s+(?:dms?|pms?|private messages?)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex PrivateTerms();
    [GeneratedRegex(@"\b(?:no|never|don't|do not)\b.{0,20}\b(?:dm|pm|private messages?)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex NoPrivateTerms();
    [GeneratedRegex(@"\b(?:application|applicant|registration|onboarding|training|verification)\s+(?:fees?|deposit|payment)\b|\bpay\b.{0,30}\b(?:to apply|to (?:start|join)|before (?:starting|joining))\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex ApplicantFee();
    [GeneratedRegex(@"\b(?:no|without)\s+(?:(?:application|applicant|registration|onboarding|training|verification)\s+)?(?:fees?|payments?|deposits?)\b|\b(?:never|do not|don't)\s+pay\b|\b(?:fees?|payment|deposit)\b.{0,20}\b(?:not required|not charged)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex NoFee();
    [GeneratedRegex(@"\b(?:urgently|immediately|right now|today only|within \d+ (?:minutes?|hours?)|act fast)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Urgency();
    [GeneratedRegex(@"\b(?:pay|send money|transfer|deposit|crypto|payment)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex PaymentAction();
    [GeneratedRegex(@"\b(?:never|don't|do not|avoid)\b.{0,40}\b(?:pay|send|transfer|deposit)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex NoPressure();
    [GeneratedRegex(@"(?<=[.!?])\s+|[\r\n;]+", RegexOptions.None, 100)]
    private static partial Regex Sentences();
}
