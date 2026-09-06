using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment;

/// <summary>Complete, file-owned forum topics. Rendering never reads or writes Discord.</summary>
public sealed class RecruitmentGuidelines(IOptions<RecruitmentOptions> options, IOptions<StorageOptions> storage)
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly Regex Tokens = new(@"\{\{([^{}]+)\}\}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public string Load(RecruitmentForumKind forum)
    {
        string root = Path.GetFullPath(storage.Value.AssetsRootPath);
        string path = Path.GetFullPath(Path.Combine(root, options.Value.GuidelinesDirectory, FileName(forum)));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Guidelines must be inside the configured assets directory.");
        }
        if (new FileInfo(path).Length > 16384)
        {
            throw new InvalidDataException("Guideline template exceeds the input limit.");
        }
        return File.ReadAllText(path).Replace("\r\n", "\n").Trim();
    }

    public string Render(string template, string code)
    {
        if (!IsCode(code) || string.IsNullOrWhiteSpace(template) || template.Length > 8192 ||
            Tokens.Matches(template).Count(match => match.Groups[1].Value == "code") != 1)
        {
            throw new InvalidDataException("Guidelines require exactly one acknowledgement code token.");
        }
        Dictionary<string, string> values = new()
        {
            ["code"] = code,
            ["acknowledgement_minutes"] = options.Value.AcknowledgementMinutes.ToString(),
            ["cooldown_days"] = options.Value.CooldownDays.ToString(),
            ["unanswered_days"] = options.Value.UnansweredDays.ToString()
        };
        string topic = Tokens.Replace(template, match => values.TryGetValue(match.Groups[1].Value, out string? value)
            ? value : throw new InvalidDataException("Unknown guideline template token."));
        if (topic.Length > 4096 || topic.Contains("{{", StringComparison.Ordinal) || topic.Contains("}}", StringComparison.Ordinal) ||
            !topic.Contains($"UDC acknowledgement code: {code}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Guidelines must fit 4096 characters and contain the labelled acknowledgement code.");
        }
        return topic;
    }

    public void ValidateAll()
    {
        foreach (RecruitmentForumKind forum in Enum.GetValues<RecruitmentForumKind>())
        {
            Render(Load(forum), "ABCDE");
        }
    }

    public static string NewCode() => string.Concat(Enumerable.Range(0, 5).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]));
    public static bool IsCode(string? code) => code is { Length: 5 } && code.All(Alphabet.Contains);
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
    public static DateTimeOffset WeekStart(DateTimeOffset now) =>
        new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(-((int)now.UtcDateTime.DayOfWeek + 6) % 7);
    public static string FileName(RecruitmentForumKind forum) => forum switch
    {
        RecruitmentForumKind.PaidRecruiting => "paid-recruiting.md",
        RecruitmentForumKind.PaidForHire => "paid-for-hire.md",
        RecruitmentForumKind.HobbyRecruiting => "hobby-recruiting.md",
        RecruitmentForumKind.HobbyForHire => "hobby-for-hire.md",
        _ => throw new ArgumentOutOfRangeException(nameof(forum))
    };
    public static string Hash(string value) => RecruitmentObservationCoordinator.Hash(value);
    public static string TagHash(IReadOnlyList<RecruitmentForumTag> tags) => Hash(System.Text.Json.JsonSerializer.Serialize(tags));
}
