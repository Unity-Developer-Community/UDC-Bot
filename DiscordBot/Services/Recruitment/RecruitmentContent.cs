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
            if (Ambiguous().IsMatch(text)) return "ambiguous / discuss payment terms";
            return "no concrete rate or budget found";
        }
        catch (RegexMatchTimeoutException) { return "unknown"; }
    }

    [GeneratedRegex(@"(?:[$€£¥]\s*\d+(?:[.,]\d+)*(?:\s*[kK])?|\b\d+(?:[.,]\d+)*\s*(?:USD|AUD|CAD|EUR|GBP|JPY)\b|\b(?:USD|AUD|CAD|EUR|GBP|JPY)\s*\d+)", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Concrete();
    [GeneratedRegex(@"\b(?:rev(?:enue)?[ -]?share|royalt(?:y|ies)|negotiable|competitive|paid|budget|payment|rates?|profit[ -]?share)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Ambiguous();
}
