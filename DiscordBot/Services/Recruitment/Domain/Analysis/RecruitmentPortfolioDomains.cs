using System.Globalization;
using System.IO;

namespace DiscordBot.Services.Recruitment;

public static class RecruitmentPortfolioDomains
{
    private static readonly IdnMapping Idn = new();
    private static readonly Lazy<HashSet<string>> Rules = new(() =>
    {
        using var stream = typeof(RecruitmentPortfolioDomains).Assembly.GetManifestResourceStream("Recruitment.PublicSuffixIcann")
            ?? throw new InvalidDataException("Bundled ICANN public suffix rules are missing.");
        using var reader = new StreamReader(stream);
        var rules = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
            var prefix = line.StartsWith('!') ? "!" : line.StartsWith("*.", StringComparison.Ordinal) ? "*." : "";
            rules.Add(prefix + Idn.GetAscii(line[prefix.Length..]).ToLowerInvariant());
        }
        return rules;
    });

    private static readonly Lazy<HashSet<string>> Tlds = new(() => Rules.Value
        .Select(r => r.Split('.').Last().TrimStart('!')).ToHashSet(StringComparer.Ordinal));

    public static bool TryNormalize(string? input, out string host)
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
        var rules = Rules.Value;
        // An unlisted TLD is not an Internet registry boundary (e.g. a local search suffix).
        if (!Tlds.Value.Contains(labels[^1])) return false;
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
