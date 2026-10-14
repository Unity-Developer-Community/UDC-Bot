using System.Text.RegularExpressions;

namespace DiscordBot.Services.Recruitment;

public static partial class RecruitmentContent
{
    // Bounded input and regex timeout keep member-provided content off the unbounded work path.
    public static string Payment(string? content)
    {
        if (content is null) return "unknown";
        var text = content[..Math.Min(content.Length, 8000)];
        try
        {
            if (Concrete().IsMatch(text)) return "concrete rate or budget stated";
            if (RevenueShare().IsMatch(text)) return "revenue-share stated";
            if (Unpaid().IsMatch(text)) return "unpaid collaboration stated";
            if (Ambiguous().IsMatch(text)) return "ambiguous / discuss payment terms";
            return "no concrete rate or budget found";
        }
        catch (RegexMatchTimeoutException) { return "unknown"; }
    }

    // A link is evidence of a reference to work, never evidence that the author made it. No URLs are fetched.
    public static string Portfolio(string? content)
    {
        if (content is null) return "unknown";
        var text = content[..Math.Min(content.Length, 8000)];
        var external = false;
        try
        {
            foreach (Match match in Links().Matches(text))
            {
                if (!Uri.TryCreate(match.Value.TrimEnd('.', ',', ';', '!', '?'), UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("https" or "http") || uri.HostNameType != UriHostNameType.Dns) continue;
                var host = uri.IdnHost;
                static bool Domain(string host, string domain) => host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
                if (Domain(host, "discord.com") || Domain(host, "discord.gg") || Domain(host, "discordapp.com")) continue;
                external = true;
                var workHost = new[] { "github.com", "gitlab.com", "artstation.com", "behance.net", "dribbble.com", "itch.io", "vimeo.com", "youtube.com", "youtu.be", "deviantart.com" }
                    .Any(domain => Domain(host, domain));
                var lineStart = text.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
                var lineEnd = text.IndexOf('\n', match.Index);
                if (lineEnd < 0) lineEnd = text.Length;
                if (workHost && uri.AbsolutePath.Length > 1 || WorkLabel().IsMatch(text[lineStart..lineEnd]))
                    return "work-sample link detected";
            }
            return external ? "external link found; purpose unclear" : "no work-sample link found";
        }
        catch (RegexMatchTimeoutException) { return "unknown"; }
    }

    [GeneratedRegex(@"(?:[$€£¥]\s*\d+(?:[.,]\d+)*(?:\s*[kK])?|\b\d+(?:[.,]\d+)*\s*(?:USD|AUD|CAD|EUR|GBP|JPY)\b|\b(?:USD|AUD|CAD|EUR|GBP|JPY)\s*\d+)", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Concrete();
    [GeneratedRegex(@"\b(?:rev(?:enue)?[ -]?share|royalt(?:y|ies)|negotiable|competitive|paid|budget|payment|rates?|profit[ -]?share)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Ambiguous();
    [GeneratedRegex(@"\b(?:rev(?:enue)?[ -]?share|royalt(?:y|ies)|profit[ -]?share)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex RevenueShare();
    [GeneratedRegex(@"\b(?:unpaid|volunteer|no (?:pay|payment|compensation))\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Unpaid();
    [GeneratedRegex(@"https?://[^\s<>\[\]()]+", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Links();
    [GeneratedRegex(@"\b(?:portfolio|showreel|demo|work[ -]?samples?|examples? of (?:my |our )?work|previous work|proof of work)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex WorkLabel();
}
