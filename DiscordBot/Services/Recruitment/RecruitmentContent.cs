using System.Text.RegularExpressions;

namespace DiscordBot.Services.Recruitment;

public sealed record ContentObservation(string Code, string Description, string Evidence);
public enum CompensationKind { Unavailable, Missing, MonetaryPromise, Rate, Mixed, Unpaid, RevenueShare }
public sealed record PaymentObservation(string Summary, string Evidence, CompensationKind Kind = CompensationKind.Missing);
public sealed record PortfolioCandidate(string Url, string Host, bool Specific, bool Supported, bool WorkContext, bool Negated, string? ReuseUrl = null);
public sealed record PortfolioEvidence(bool Available, bool MediaSample, IReadOnlyList<PortfolioCandidate> Candidates);
public sealed record StarterAttachment(string Id, string Name, string? ContentType);

public static partial class RecruitmentContent
{
    private static string Bounded(string text) => text[..Math.Min(text.Length, 8000)];
    private static string Excerpt(string text) => RecruitmentPresentation.Safe(text.Trim(), 180);
    public static string Payment(string? content) => ReadPayment(content).Summary;

    // An amount alone is not compensation. Labels and units provide context, never verification.
    public static PaymentObservation ReadPayment(string? content)
    {
        if (content is null) return new("unknown", "", CompensationKind.Unavailable);
        var text = Bounded(content);
        try
        {
            var sentences = Sentences().Split(text);
            var rate = sentences.FirstOrDefault(s => Concrete().IsMatch(s) &&
                (PaymentLabel().IsMatch(s) || RateUnit().IsMatch(s)) && !OtherPrice().IsMatch(s) && !ApplicantFee().IsMatch(s) && !NegatedMoney().IsMatch(s));
            var unpaid = sentences.FirstOrDefault(s => Unpaid().IsMatch(s) && !NegatedUnpaid().IsMatch(s) && !(ApplicantFee().IsMatch(s) && NoFee().IsMatch(s)));
            var share = sentences.FirstOrDefault(s => RevenueShare().IsMatch(s) && !NegatedShare().IsMatch(s));
            var promise = sentences.FirstOrDefault(s => MonetaryPromise().IsMatch(s) && !NegatedMoney().IsMatch(s) && !ApplicantFee().IsMatch(s) && (!Unpaid().IsMatch(s) || NegatedUnpaid().IsMatch(s)));
            if ((rate is not null || promise is not null) && (unpaid is not null || share is not null))
                return new("mixed compensation terms; clarify what is guaranteed", Excerpt(rate ?? promise!) + " / " + Excerpt(unpaid ?? share!), CompensationKind.Mixed);
            if (sentences.Any(s => NegatedMoney().IsMatch(s) && !(ApplicantFee().IsMatch(s) && NoFee().IsMatch(s)))) return new("no monetary payment stated", Excerpt(sentences.First(s => NegatedMoney().IsMatch(s) && !(ApplicantFee().IsMatch(s) && NoFee().IsMatch(s)))), CompensationKind.Unpaid);
            if (unpaid is not null) return new("unpaid collaboration stated", Excerpt(unpaid), CompensationKind.Unpaid);
            if (share is not null) return new("revenue-share stated", Excerpt(share), CompensationKind.RevenueShare);
            if (rate is not null) return new("concrete rate or budget stated", Excerpt(rate), CompensationKind.Rate);
            var amount = sentences.FirstOrDefault(s => Concrete().IsMatch(s));
            if (amount is not null) return new("amount mentioned; compensation unclear", Excerpt(amount));
            var vague = sentences.FirstOrDefault(s => PaymentLabel().IsMatch(s));
            return vague is not null ? new("ambiguous / discuss payment terms", Excerpt(vague), CompensationKind.MonetaryPromise) : new("no concrete rate or budget found", "");
        }
        catch (RegexMatchTimeoutException) { return new("unknown", "", CompensationKind.Unavailable); }
    }

    public static readonly IReadOnlySet<string> SeedPortfolioHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "github.com", "gitlab.com", "artstation.com", "behance.net", "dribbble.com", "itch.io", "vimeo.com", "youtube.com", "youtu.be", "deviantart.com" };

    public static PortfolioEvidence ReadPortfolio(string? content, IReadOnlyList<StarterAttachment>? attachments = null,
        IReadOnlySet<string>? supportedHosts = null)
    {
        var media = attachments?.Any(a => a.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ||
            a.ContentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true ||
            a.ContentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true) == true;
        var candidates = new List<PortfolioCandidate>();
        if (content is null) return new(false, media, candidates);
        var text = Bounded(content);
        try
        {
            foreach (Match match in Links().Matches(text))
            {
                if (!TryLink(match.Value, out var uri) || RecruitmentPortfolioUrls.Read(uri!) is not { } identity) continue;
                var start = text.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
                var end = text.IndexOf('\n', match.Index); if (end < 0) end = text.Length;
                var line = text[start..end];
                candidates.Add(new(identity.DisplayUrl, identity.Host, identity.Specific,
                    (supportedHosts ?? SeedPortfolioHosts).Any(d => Domain(identity.Host, d)), WorkLabel().IsMatch(line), NoWork().IsMatch(line), identity.ReuseUrl));
                if (candidates.Count == 8) break;
            }
            return new(true, media, candidates);
        }
        catch (RegexMatchTimeoutException) { return new(false, media, candidates); }
    }

    public static string Portfolio(string? content, IReadOnlyList<StarterAttachment>? attachments = null)
    {
        var facts = ReadPortfolio(content, attachments);
        if (facts.MediaSample) return "media sample attached";
        if (!facts.Available) return "unknown";
        if (facts.Candidates.Any(c => !c.Negated && c.Specific && (c.Supported || c.WorkContext))) return "work-sample link detected";
        return facts.Candidates.Count > 0 ? "external link found; purpose unclear" : "no work-sample link found";
    }

    public static string? DmRequest(string? content)
    {
        if (content is null) return null;
        try { return Sentences().Split(Bounded(content)).FirstOrDefault(s => DmFirst().IsMatch(s) && !NoPrivateTerms().IsMatch(s) && !PrivateOnly().IsMatch(s) && !SafetyQuote().IsMatch(s)) is { } s ? Excerpt(s) : ""; }
        catch (RegexMatchTimeoutException) { return null; }
    }

    public static string? Duration(string? content, bool forHire)
    {
        if (content is null) return null;
        try { return Sentences().Split(Bounded(content)).FirstOrDefault(s => (ProjectDuration().IsMatch(s) || forHire && Availability().IsMatch(s)) && !NoDuration().IsMatch(s)) is { } s ? Excerpt(s) : ""; }
        catch (RegexMatchTimeoutException) { return null; }
    }

    public static int? NormalizedLength(string? content)
    {
        if (content is null) return null;
        try
        {
            var text = Links().Replace(Bounded(content), " ");
            text = Regex.Replace(text, @"^[ \t+*=\-]{3,}$|^[ \t]*[-+][ \t]+", " ", RegexOptions.Multiline, TimeSpan.FromMilliseconds(100));
            text = Regex.Replace(text, @"<[^>]{0,200}>|[*_~`#>\[\]()|\\]", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Length;
        }
        catch (RegexMatchTimeoutException) { return null; }
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
            Add("private-terms", "Post directs readers to private messages for terms", s => PrivateTerms().IsMatch(s) && !NoPrivateTerms().IsMatch(s) && !SafetyQuote().IsMatch(s));
            Add("applicant-fee", "Post mentions an applicant payment or fee", s => ApplicantFee().IsMatch(s) && !NoFee().IsMatch(s) && !SafetyQuote().IsMatch(s));
            Add("payment-pressure", "Post combines payment with urgency", s => Urgency().IsMatch(s) && PaymentAction().IsMatch(s) && !NoPressure().IsMatch(s) && !SafetyQuote().IsMatch(s));
        }
        catch (RegexMatchTimeoutException) { /* Observations are optional, never enforcement evidence. */ }
        var payment = ReadPayment(content);
        if (payment.Kind == CompensationKind.Mixed) observations.Add(new("mixed-compensation", "Mixed compensation terms; clarify what is guaranteed", payment.Evidence[..Math.Min(payment.Evidence.Length, 180)]));
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

    public static List<string> ContactLinks(string? content) => ReadPortfolio(content).Candidates
        .Where(c => c.Specific && !c.Negated && c.ReuseUrl is not null).Select(c => c.ReuseUrl!)
        .Distinct(StringComparer.Ordinal).Take(8).ToList();
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
    [GeneratedRegex(@"\b(?:unpaid|volunteer|not paid|no (?:pay|payment|compensation)|(?:paid? (?:in|with) exposure|for exposure|exposure (?:only|instead of (?:pay|payment))|compensation.{0,15}exposure))\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Unpaid();
    [GeneratedRegex(@"https?://[^\s<>\[\]()]+", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Links();
    [GeneratedRegex(@"\b(?:portfolio|showreel|demo|work[ -]?samples?|examples? of (?:my |our )?work|previous work|proof of work)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex WorkLabel();
    [GeneratedRegex(@"\b(?:no|without|don't have|do not have|not (?:my|our|a))\b.{0,35}\b(?:portfolio|samples?|work|demo|showreel)\b|\b(?:portfolio|samples?|work|demo|showreel)\b.{0,25}\b(?:unavailable|not available|coming soon)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex NoWork();
    [GeneratedRegex(@"\b(?:dms?|pms?|private messages?)\b.{0,35}\b(?:rates?|budget|pay|payment|terms|details)\b|\b(?:rates?|budget|payment|terms|details)\b.{0,35}\b(?:in|via|by|only)\s+(?:dms?|pms?|private messages?)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex PrivateTerms();
    [GeneratedRegex(@"\b(?:no|never|don't|do not)\b.{0,20}\b(?:dms?|pms?|private messages?)\b", RegexOptions.IgnoreCase, 100)]
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
    [GeneratedRegex(@"\b(?:do not|don't|never|cannot|can't|won't|will not|no)\s+(?:offer\s+)?(?:pay|payment|budget|salary|compensation)\b|\bnot paid\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex NegatedMoney();
    [GeneratedRegex(@"\b(?:paid|pay|paying|payment|salary|wages|monetary compensation|guaranteed compensation)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex MonetaryPromise();
    [GeneratedRegex(@"\b(?:not (?:an? )?unpaid|not (?:an? )?volunteer|no unpaid|no volunteer|not (?:for |paid in )?exposure|no exposure|never unpaid)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex NegatedUnpaid();
    [GeneratedRegex(@"\b(?:no|not|without|never)\s+(?:a )?(?:rev(?:enue)?[ -]?share|royalt(?:y|ies)|profit[ -]?share)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex NegatedShare();
    [GeneratedRegex(@"\b(?:dms?|pms?|message me privately|private message)\b.{0,45}\b(?:apply|first|me|us|rates?|terms|details|interested|portfolio|contact)\b|\b(?:apply|contact|applications?)\b.{0,25}\b(?:dm|pm|dms|pms|private messages?)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex DmFirst();
    [GeneratedRegex(@"\b(?:only|solely)\b.{0,35}\b(?:private|personal|sensitive|confidential)\b|\b(?:dms?|pms?|private messages?)\b.{0,35}\b(?:private|personal|sensitive|confidential)\s+(?:information|details|data)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex PrivateOnly();
    [GeneratedRegex(@"\b(?:beware|red flag|example of (?:a scam|unsafe)|warning|safety advice|scammers? (?:say|ask|request)|avoid (?:posts|people|anyone|requests))\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex SafetyQuote();
    [GeneratedRegex(@"\b(?:duration|project length)\s*[:=]\s*(?:\d+|one|two|three|four|six|twelve)\s+(?:days?|weeks?|months?|years?)\b|\b(?:\d+|one|two|three|four|six|twelve)[ -](?:day|week|month|year)\s+(?:project|contract|engagement)\b|\b(?:project|contract|engagement|work|role|job)\s+(?:(?:will |is |expected to )?(?:last|run|take|for|length|duration)(?:s)?[: ]*|duration[: ]+)(?:about |approximately )?(?:\d+|one|two|three|four|six|twelve)\s*(?:days?|weeks?|months?|years?)\b|\b(?:deliver|complete|finish|completion)\b.{0,20}\b(?:in|within)\s+(?:\d+|one|two|three|four|six|twelve)\s+(?:days?|weeks?|months?)\b|\b(?:ongoing|permanent|long[ -]term)\s+(?:work|role|position|contract|project|engagement)\b|\b(?:work|role|position|contract|project)\s+is\s+(?:ongoing|permanent)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex ProjectDuration();
    [GeneratedRegex(@"\b(?:not|no|never|unavailable|not available)\b.{0,20}\b(?:ongoing|permanent|available|long[ -]term|short[ -]term)\b|\b(?:duration|project length)\s*[:=]\s*(?:unknown|unclear|not)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex NoDuration();
    [GeneratedRegex(@"\bavailable\s+(?:for\s+(?:ongoing|short[ -]term|long[ -]term|permanent)\s+(?:work|contracts?|projects?|engagements?)|(?:from|starting|until)\s+(?:\d{4}-\d{2}-\d{2}|(?:January|February|March|April|May|June|July|August|September|October|November|December)\b))", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Availability();
    [GeneratedRegex(@"(?<=[.!?])\s+|[\r\n;]+", RegexOptions.None, 100)]
    private static partial Regex Sentences();
}
