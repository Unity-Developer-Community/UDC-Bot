using DiscordBot.Components;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Settings.Validation;

public sealed record FeatureConfigurationStatus(
    string ComponentId,
    bool IsConfigured,
    IReadOnlyList<string> Errors);

public interface IFeatureConfigurationValidator
{
    FeatureConfigurationStatus Validate();
}

public sealed class FeatureConfigurationCatalog
{
    private readonly IReadOnlyDictionary<string, FeatureConfigurationStatus> _statuses;

    public FeatureConfigurationCatalog(IEnumerable<IFeatureConfigurationValidator> validators)
    {
        _statuses = validators
            .Select(validator => validator.Validate())
            .ToDictionary(status => status.ComponentId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<FeatureConfigurationStatus> Statuses => _statuses.Values.ToArray();

    public FeatureConfigurationStatus Get(string componentId) =>
        _statuses.TryGetValue(componentId, out var status)
            ? status
            : new FeatureConfigurationStatus(componentId, true, []);
}

public sealed class RecruitmentOptionsValidator(IOptions<RecruitmentOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "recruitment", RecruitmentOptions.SectionName, out var value, out var failure))
            return failure;
        if (!value.Enabled)
            return FeatureValidation.Valid("recruitment");

        var errors = FeatureValidation.MissingIds(
            ("Recruitment:ForumChannelId", value.ForumChannelId),
            ("Recruitment:LookingToHireTagId", value.LookingToHireTagId),
            ("Recruitment:LookingForWorkTagId", value.LookingForWorkTagId),
            ("Recruitment:UnpaidCollaborationTagId", value.UnpaidCollaborationTagId),
            ("Recruitment:PositionFilledTagId", value.PositionFilledTagId));
        if (value.EditPermissionMinutes <= 0)
            errors.Add("Recruitment:EditPermissionMinutes must be greater than zero when enabled.");
        return FeatureValidation.Result("recruitment", errors);
    }
}

public sealed class UserActivityOptionsValidator(IOptions<UserActivityOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, ComponentIds.UserActivity, UserActivityOptions.SectionName, out var value, out var failure))
            return failure;
        var errors = new List<string>();
        if (value.WelcomeMessageDelaySeconds < 0)
            errors.Add("UserActivity:WelcomeMessageDelaySeconds cannot be negative.");
        if (value.Thanks.Count == 0 || value.Thanks.Any(string.IsNullOrWhiteSpace))
            errors.Add("UserActivity:Thanks requires at least one non-empty phrase.");
        if (value.ThanksCooldown < 0)
            errors.Add("UserActivity:ThanksCooldown cannot be negative.");
        if (value.ThanksMinJoinTime < 0)
            errors.Add("UserActivity:ThanksMinJoinTime cannot be negative.");
        if (value.XpMinPerMessage < 0 || value.XpMaxPerMessage <= value.XpMinPerMessage)
            errors.Add("UserActivity:XpMaxPerMessage must be greater than the non-negative UserActivity:XpMinPerMessage.");
        if (value.XpMinCooldown < 0 || value.XpMaxCooldown <= value.XpMinCooldown)
            errors.Add("UserActivity:XpMaxCooldown must be greater than the non-negative UserActivity:XpMinCooldown.");
        if (value.CodeReminderCooldown < 0)
            errors.Add("UserActivity:CodeReminderCooldown cannot be negative.");
        return FeatureValidation.Result(ComponentIds.UserActivity, errors);
    }
}

public sealed class UnityHelpOptionsValidator(IOptions<UnityHelpOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "unity-help", UnityHelpOptions.SectionName, out var value, out var failure))
            return failure;
        return !value.Enabled
            ? FeatureValidation.Valid("unity-help")
            : FeatureValidation.ValidateRequiredIds("unity-help",
                ("UnityHelp:ForumChannelId", value.ForumChannelId),
                ("UnityHelp:ResolvedTagId", value.ResolvedTagId));
    }
}

public sealed class BirthdayOptionsValidator(IOptions<BirthdayOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "birthday-announcements", BirthdayOptions.SectionName, out var value, out var failure))
            return failure;
        var errors = FeatureValidation.MissingIds(("BirthdayAnnouncements:AnnouncementChannelId", value.AnnouncementChannelId));
        if (value.CheckIntervalMinutes <= 0)
            errors.Add("BirthdayAnnouncements:CheckIntervalMinutes must be greater than zero when enabled.");
        return FeatureValidation.Result("birthday-announcements", errors);
    }
}

public sealed class IntroductionWatcherOptionsValidator(IOptions<ModerationOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "introduction-watcher", ModerationOptions.SectionName, out var value, out var failure))
            return failure;
        return !value.IntroductionWatcherEnabled
            ? FeatureValidation.Valid("introduction-watcher")
            : FeatureValidation.ValidateRequiredIds("introduction-watcher",
                ("Moderation:IntroductionChannelId", value.IntroductionChannelId));
    }
}

public sealed class ReminderOptionsValidator(IOptions<ReminderOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "reminders", ReminderOptions.SectionName, out var value, out var failure))
            return failure;
        return FeatureValidation.ValidateRequiredIds("reminders",
            ("Reminders:FallbackChannelId", value.FallbackChannelId));
    }
}

public sealed class TicketOptionsValidator(IOptions<TicketOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "tickets", TicketOptions.SectionName, out var value, out var failure))
            return failure;
        var errors = FeatureValidation.MissingIds(
            ("Tickets:OpenCategoryId", value.OpenCategoryId),
            ("Tickets:ClosedCategoryId", value.ClosedCategoryId));
        if (string.IsNullOrWhiteSpace(value.OpenChannelPrefix))
            errors.Add("Tickets:OpenChannelPrefix is required.");
        if (string.IsNullOrWhiteSpace(value.ClosedChannelPrefix))
            errors.Add("Tickets:ClosedChannelPrefix is required.");
        return FeatureValidation.Result("tickets", errors);
    }
}

public sealed class FeedOptionsValidator(IOptions<FeedOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "feeds", FeedOptions.SectionName, out var value, out var failure))
            return failure;
        return FeatureValidation.ValidateRequiredIds("feeds",
            ("Feeds:NewsChannelId", value.NewsChannelId),
            ("Feeds:ReleasesChannelId", value.ReleasesChannelId));
    }
}

public sealed class UserFunOptionsValidator(IOptions<UserFunOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "user-fun", UserFunOptions.SectionName, out var value, out var failure))
            return failure;
        return string.IsNullOrWhiteSpace(value.SlapObjectsTable)
            ? FeatureValidation.Result("user-fun", ["UserFun:SlapObjectsTable is required for slap commands."])
            : FeatureValidation.Valid("user-fun");
    }
}

public sealed class RoleAssignmentOptionsValidator(IOptions<RoleAssignmentOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "role-assignment", RoleAssignmentOptions.SectionName, out var value, out var failure))
            return failure;
        return value.AssignableRoles.Count == 0
            ? FeatureValidation.Result("role-assignment", ["RoleAssignment:AssignableRoles requires at least one role."])
            : FeatureValidation.Valid("role-assignment");
    }
}

public sealed class KnowledgeSearchOptionsValidator(IOptions<KnowledgeSearchOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "updates", KnowledgeSearchOptions.SectionName, out var value, out var failure))
            return failure;
        return Uri.TryCreate(value.WikipediaSearchPage, UriKind.Absolute, out _)
            ? FeatureValidation.Valid("updates")
            : FeatureValidation.Result("updates", ["KnowledgeSearch:WikipediaSearchPage must be an absolute URI."]);
    }
}

public sealed class TipsOptionsValidator(IOptions<TipsOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "tips", TipsOptions.SectionName, out var value, out var failure))
            return failure;
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(value.ImageDirectory))
            errors.Add("Tips:ImageDirectory is required.");
        if (value.MaxImageBytes <= 0)
            errors.Add("Tips:MaxImageBytes must be greater than zero.");
        if (value.MaxDirectoryBytes < value.MaxImageBytes)
            errors.Add("Tips:MaxDirectoryBytes must be at least Tips:MaxImageBytes.");
        return FeatureValidation.Result("tips", errors);
    }
}

public sealed class CasinoOptionsValidator(IOptions<CasinoOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "casino", CasinoOptions.SectionName, out var value, out var failure))
            return failure;
        if (!value.Enabled)
            return FeatureValidation.Valid("casino");

        var errors = new List<string>();
        if (value.StartingTokens < 0)
            errors.Add("Casino:StartingTokens cannot be negative.");
        if (value.GameTimeoutMinutes <= 0)
            errors.Add("Casino:GameTimeoutMinutes must be greater than zero.");
        if (value.DailyRewardTokens < 0)
            errors.Add("Casino:DailyRewardTokens cannot be negative.");
        if (value.DailyRewardIntervalSeconds <= 0)
            errors.Add("Casino:DailyRewardIntervalSeconds must be greater than zero.");
        return FeatureValidation.Result("casino", errors);
    }
}

public sealed class WeatherOptionsValidator(IOptions<WeatherOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "weather", WeatherOptions.SectionName, out var value, out var failure))
            return failure;
        return string.IsNullOrWhiteSpace(value.ApiKey)
            ? FeatureValidation.Result("weather", ["Weather:ApiKey is required for weather commands."])
            : FeatureValidation.Valid("weather");
    }
}

public sealed class AirportOptionsValidator(IOptions<AirportOptions> options)
    : IFeatureConfigurationValidator
{
    public FeatureConfigurationStatus Validate()
    {
        if (!FeatureValidation.TryGet(options, "airport", AirportOptions.SectionName, out var value, out var failure))
            return failure;
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(value.FlightApiKey))
            errors.Add("Airport:FlightApiKey is required for flight lookups.");
        if (string.IsNullOrWhiteSpace(value.FlightApiSecret))
            errors.Add("Airport:FlightApiSecret is required for flight lookups.");
        if (string.IsNullOrWhiteSpace(value.AirLabsApiKey))
            errors.Add("Airport:AirLabsApiKey is required for airport lookups.");
        return FeatureValidation.Result("airport", errors);
    }
}

internal static class FeatureValidation
{
    public static bool TryGet<T>(
        IOptions<T> options,
        string componentId,
        string sectionName,
        out T value,
        out FeatureConfigurationStatus failure)
        where T : class
    {
        try
        {
            value = options.Value;
            failure = Valid(componentId);
            return true;
        }
        catch (Exception exception)
        {
            value = null!;
            failure = Result(
                componentId,
                [$"{sectionName} contains a value that could not be bound ({exception.GetType().Name})."]);
            return false;
        }
    }

    public static FeatureConfigurationStatus ValidateRequiredIds(
        string componentId,
        params (string Key, ulong Value)[] values) =>
        Result(componentId, MissingIds(values));

    public static List<string> MissingIds(params (string Key, ulong Value)[] values) =>
        values.Where(value => value.Value == 0)
            .Select(value => $"{value.Key} must be a non-zero Discord ID when the feature is enabled.")
            .ToList();

    public static FeatureConfigurationStatus Valid(string componentId) =>
        new(componentId, true, []);

    public static FeatureConfigurationStatus Result(string componentId, IReadOnlyList<string> errors) =>
        new(componentId, errors.Count == 0, errors);
}
