using System.Globalization;

namespace DiscordBot.Services.Recruitment.Domain.Analysis;

/// <summary>DNS/IDN and public-suffix decisions over supplied rules; no resource or filesystem access.</summary>
internal static class PortfolioHostnamePolicy
{
    private static readonly IdnMapping Idn = new();
    public static bool TryNormalize(string? input, IReadOnlySet<string> rules, IReadOnlySet<string> tlds, out string host)
    {
        host = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        var value = input.Trim();
        // Only hostnames, never an authority, URL, wildcard, port or trailing root dot.
        if (value.IndexOfAny(['/', '\\', ':', '@', '*', '?', '#', '%']) >= 0 || value.EndsWith('.')) return false;
        try { value = Idn.GetAscii(value).ToLowerInvariant(); }
        catch (ArgumentException) { return false; }
        if (value.Length > 253 || Uri.CheckHostName(value) != UriHostNameType.Dns || !value.Contains('.')) return false;
        var labels = value.Split('.');
        if (labels.Any(l => l.Length is < 1 or > 63 || l[0] == '-' || l[^1] == '-' || l.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))) return false;
        // An unlisted TLD is not an Internet registry boundary (e.g. a local search suffix).
        if (!tlds.Contains(labels[^1])) return false;
        var suffixLabels = 1;
        for (var i = 0; i < labels.Length; i++)
        {
            var suffix = string.Join('.', labels[i..]);
            if (rules.Contains("!" + suffix)) { suffixLabels = labels.Length - i - 1; break; }
            if (rules.Contains(suffix)) suffixLabels = Math.Max(suffixLabels, labels.Length - i);
            if (i + 1 < labels.Length && rules.Contains("*." + string.Join('.', labels[(i + 1)..])))
                suffixLabels = Math.Max(suffixLabels, labels.Length - i);
        }
        if (labels.Length <= suffixLabels) return false;
        host = value;
        return true;
    }
}
