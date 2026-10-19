using System.IO;
using System.Security.Cryptography;
using DiscordBot.Settings;
using Newtonsoft.Json;

namespace DiscordBot.Services.Recruitment;

public sealed class RecruitmentAssets
{
    public const int GuidelinesMaxLength = 1000;

    public Dictionary<string, string> Templates { get; }
    public Dictionary<string, List<TagDefinition>> Tags { get; }
    public RecruitmentAssets(string root, RecruitmentSettings settings)
    {
        var directory = Path.Combine(root, "recruitment");
        Tags = JsonConvert.DeserializeObject<Dictionary<string, List<TagDefinition>>>(File.ReadAllText(Path.Combine(directory, "tags.json")))
               ?? throw new InvalidDataException("Recruitment tag manifest is empty.");
        Templates = new();
        foreach (var purpose in settings.Forums.Mapping().Keys)
        {
            var template = File.ReadAllText(Path.Combine(directory, "guidelines", purpose + ".md"));
            if (template.Split("{{code}}").Length != 2 ||
                new[] { "acknowledgementMinutes", "cooldownDays" }.Any(p => !template.Contains("{{" + p + "}}")))
                throw new InvalidDataException($"Invalid Guidelines placeholders for {purpose}.");
            Templates[purpose] = template;
            if (!Tags.TryGetValue(purpose, out var tags) || tags.Count is < 1 or > 20 ||
                tags.Count(t => t.Closing) != 1 || tags.Any(t => string.IsNullOrWhiteSpace(t.Key) ||
                    t.Key.Length > 32 || t.Group.Length > 32 || string.IsNullOrWhiteSpace(t.Name) ||
                    t.Name.Length is < 1 or > 20 || (t.Closing && t.Group.Length != 0)) ||
                tags.Select(t => t.Key).Distinct().Count() != tags.Count ||
                tags.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != tags.Count)
                throw new InvalidDataException($"Invalid managed tags for {purpose}.");
            _ = Render(purpose, "ABCDE", settings);
        }
    }

    public string Render(string purpose, string code, RecruitmentSettings settings)
    {
        var text = Templates[purpose].Replace("{{code}}", code)
            .Replace("{{acknowledgementMinutes}}", settings.AcknowledgementMinutes.ToString())
            .Replace("{{cooldownDays}}", settings.CooldownDays.ToString())
            .Replace("{{unansweredDays}}", settings.UnansweredDays.ToString()).Trim();
        if (text.Length > GuidelinesMaxLength)
            throw new InvalidDataException($"Guidelines for {purpose} exceed the {GuidelinesMaxLength}-character budget after rendering.");
        if (text.Contains("{{")) throw new InvalidDataException($"Invalid Guidelines placeholders for {purpose}.");
        return text;
    }

    public static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 5).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }

    public string TagAdvice(string purpose, IEnumerable<string> appliedIds, ForumReceipt receipt)
    {
        var groups = Tags[purpose].Where(t => !t.Closing && t.Group.Length > 0).GroupBy(t => t.Group);
        var missing = groups.Where(g => !g.Any(t => receipt.Tags.TryGetValue(t.Key, out var managed) && appliedIds.Contains(managed.Id)))
            .Select(g => g.Key).ToArray();
        return missing.Length == 0 ? "Recommended tag groups present" : "Consider adding tags for: " + string.Join(", ", missing);
    }
}
