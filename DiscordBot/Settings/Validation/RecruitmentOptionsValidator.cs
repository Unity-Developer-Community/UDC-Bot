using System.IO;
using DiscordBot.Components;
using DiscordBot.Services.Recruitment;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DiscordBot.Settings.Validation;

public sealed class RecruitmentOptionsValidator(
    IOptions<RecruitmentOptions> options,
    IConfiguration configuration) : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        // A deliberately disabled feature need not bind an unfinished configuration.
        if (bool.TryParse(configuration["Recruitment:Enabled"], out var enabled) && !enabled)
            return FeatureValidation.Valid(ComponentIds.Recruitment);
        if (!FeatureValidation.TryGet(options, ComponentIds.Recruitment, RecruitmentOptions.SectionName,
                out var value, out var failure))
            return failure;
        if (!value.Enabled)
            return FeatureValidation.Valid(ComponentIds.Recruitment);

        var errors = ValidateValues(value).ToList();
        ValidateShape(configuration.GetSection(RecruitmentOptions.SectionName), typeof(RecruitmentOptions), errors);
        return FeatureValidation.Result(ComponentIds.Recruitment, errors);
    }

    internal static IReadOnlyList<string> ValidateValues(RecruitmentOptions value)
    {
        var forums = RecruitmentForumClassifier.GetForums(value.Forums);
        var errors = FeatureValidation.MissingIds(forums.Select(f =>
            ($"Recruitment:Forums:{f.Kind}:ChannelId", f.ChannelId)).Append(
            ("Recruitment:FeedChannelId", value.FeedChannelId)).ToArray());
        if (forums.Any(f => f.ChannelId == 0))
            errors.Add("Recruitment requires four named Forums; migrate the legacy single-forum settings in FeatureSettings.json.");
        if (forums.Select(f => f.ChannelId).Where(id => id != 0).Distinct().Count() != forums.Count(f => f.ChannelId != 0))
            errors.Add("Recruitment:Forums channel IDs must be distinct.");
        if (value.FeedChannelId != 0 && forums.Any(f => f.ChannelId == value.FeedChannelId))
            errors.Add("Recruitment:FeedChannelId must differ from the forum IDs.");
        if (!Enum.IsDefined(value.Mode))
            errors.Add("Recruitment:Mode must be Observe, Advisory or Enforce.");
        Range(nameof(value.AcknowledgementMinutes), value.AcknowledgementMinutes, 1, 1440);
        Range(nameof(value.CooldownDays), value.CooldownDays, 1, 365);
        Range(nameof(value.UnansweredDays), value.UnansweredDays, 1, 365);
        Range(nameof(value.PostRetentionMonths), value.PostRetentionMonths, 1, 120);
        Range(nameof(value.AuthorRetentionMonths), value.AuthorRetentionMonths, 1, 120);
        if ((long)value.PostRetentionMonths * 28 < Math.Max(value.CooldownDays, value.UnansweredDays))
            errors.Add("Recruitment:PostRetentionMonths must cover cooldown and unanswered deadlines.");
        if (value.AuthorRetentionMonths < value.PostRetentionMonths)
            errors.Add("Recruitment:AuthorRetentionMonths must be at least PostRetentionMonths.");
        if (string.IsNullOrWhiteSpace(value.GuidelinesDirectory) || Path.IsPathRooted(value.GuidelinesDirectory) ||
            value.GuidelinesDirectory.Split('/', '\\').Any(part => part is ".." or "." or "") ||
            value.GuidelinesDirectory.Contains(':') || value.GuidelinesDirectory.Contains('\0'))
            errors.Add("Recruitment:GuidelinesDirectory must be a relative subdirectory of AssetsRootPath.");
        return errors;

        void Range(string name, int number, int min, int max)
        {
            if (number < min || number > max)
                errors.Add($"Recruitment:{name} must be between {min} and {max}.");
        }
    }

    private static void ValidateShape(IConfigurationSection section, Type type, List<string> errors)
    {
        if (section.Value is not null)
            errors.Add($"{section.Path} must be an object.");
        var properties = type.GetProperties().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetChildren())
        {
            if (!properties.TryGetValue(child.Key, out var property))
                errors.Add($"{child.Path} is not a supported recruitment setting.");
            else if (property.PropertyType == typeof(RecruitmentForumsOptions) ||
                     property.PropertyType == typeof(RecruitmentForumOptions))
                ValidateShape(child, property.PropertyType, errors);
            else if (child.GetChildren().Any() || child.Value is null)
                errors.Add($"{child.Path} must be a scalar value.");
        }
    }
}
