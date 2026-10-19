using DiscordBot.Services.Recruitment.Domain.Analysis;
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

    public static bool TryNormalize(string? input, out string host) =>
        PortfolioHostnamePolicy.TryNormalize(input, Rules.Value, Tlds.Value, out host);
}
