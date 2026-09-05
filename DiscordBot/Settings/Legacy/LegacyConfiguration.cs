using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Settings.Legacy;

public sealed record LegacyConfigurationReport(
    string SourcePath,
    IReadOnlyList<string> UnknownKeys,
    bool IsLegacySource,
    IReadOnlyList<string> MissingModularFiles);

public sealed class BotConfigurationException : Exception
{
    public BotConfigurationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed record LegacyConfiguration(
    BotSettings BotSettings,
    UserSettings UserSettings,
    LegacyConfigurationReport Report);

public static class LegacyConfigurationLoader
{
    private static readonly HashSet<string> KnownRootKeys = typeof(BotSettings)
        .GetProperties()
        .Select(property => property.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static LegacyConfiguration Load(string settingsPath, string userSettingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            throw new BotConfigurationException(
                $"Required bot configuration was not found at '{settingsPath}'. " +
                "Copy Settings/Settings.example.json to Settings/Settings.json, fill in the required local values, " +
                "or copy and fill in Settings/CoreSettings.example.json and Settings/FeatureSettings.example.json.");
        }

        var json = ReadStaticFile(settingsPath, "bot configuration");
        BotSettings settings;
        JObject document;

        try
        {
            document = JObject.Parse(json);
            settings = JsonConvert.DeserializeObject<BotSettings>(json) ??
                throw new JsonSerializationException("The root JSON value was null.");
        }
        catch (JsonException exception)
        {
            throw new BotConfigurationException(
                $"Bot configuration at '{settingsPath}' is malformed. The file was not changed. {exception.Message}",
                exception);
        }

        var unknownKeys = document.Properties()
            .Select(property => property.Name)
            .Where(name => !KnownRootKeys.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var userSettings = File.Exists(userSettingsPath)
            ? DeserializeStaticFile<UserSettings>(userSettingsPath, "user activity configuration")
            : new UserSettings();

        return new LegacyConfiguration(
            settings,
            userSettings,
            new LegacyConfigurationReport(settingsPath, unknownKeys, true, []));
    }

    private static T DeserializeStaticFile<T>(string path, string description)
    {
        try
        {
            return JsonConvert.DeserializeObject<T>(ReadStaticFile(path, description)) ??
                throw new JsonSerializationException("The root JSON value was null.");
        }
        catch (JsonException exception)
        {
            throw new BotConfigurationException(
                $"The {description} at '{path}' is malformed. The file was not changed. {exception.Message}",
                exception);
        }
    }

    private static string ReadStaticFile(string path, string description)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BotConfigurationException($"Unable to read {description} at '{path}'.", exception);
        }
    }
}

public static class LegacyBotSettingsAdapter
{
    public static IReadOnlyDictionary<string, string?> Project(LegacyConfiguration legacy)
    {
        var settings = legacy.BotSettings;
        var users = legacy.UserSettings;
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        Add(values, "DiscordConnection:Token", settings.Token);
        Add(values, "DiscordGuild:GuildId", settings.GuildId);
        Add(values, "DiscordGuild:Invite", settings.Invite);
        Add(values, "Storage:ServerRootPath", settings.ServerRootPath);
        Add(values, "Storage:AssetsRootPath", settings.AssetsRootPath);
        Add(values, "Database:ConnectionString", settings.DbConnectionString);
        Add(values, "Commands:Prefix", settings.Prefix);
        Add(values, "Commands:BotCommandsChannelId", settings.BotCommandsChannel?.Id);
        Add(values, "Logging:LogCommandExecutions", settings.LogCommandExecutions);
        Add(values, "Logging:AnnouncementChannelId", settings.BotAnnouncementChannel?.Id);
        Add(values, "Authorization:ModeratorRoleId", settings.ModeratorRoleId);

        Add(values, "UserActivity:WelcomeMessageDelaySeconds", settings.WelcomeMessageDelaySeconds);
        Add(values, "UserActivity:EveryoneScoldPeriodSeconds", settings.EveryoneScoldPeriodSeconds);
        AddList(values, "UserActivity:Thanks", users.Thanks);
        Add(values, "UserActivity:ThanksCooldown", users.ThanksCooldown);
        Add(values, "UserActivity:ThanksMinJoinTime", users.ThanksMinJoinTime);
        Add(values, "UserActivity:XpMinPerMessage", users.XpMinPerMessage);
        Add(values, "UserActivity:XpMaxPerMessage", users.XpMaxPerMessage);
        Add(values, "UserActivity:XpMinCooldown", users.XpMinCooldown);
        Add(values, "UserActivity:XpMaxCooldown", users.XpMaxCooldown);
        Add(values, "UserActivity:CodeReminderCooldown", users.CodeReminderCooldown);

        Add(values, "UserFun:SlapObjectsTable", settings.UserModuleSlapObjectsTable);
        AddList(values, "UserFun:SlapChoices", settings.UserModuleSlapChoices);
        AddList(values, "UserFun:SlapFailures", settings.UserModuleSlapFails);
        AddList(values, "RoleAssignment:AssignableRoles", settings.UserAssignableRoles?.Roles);

        Add(values, "Moderation:CommandsEnabled", settings.ModeratorCommandsEnabled);
        Add(values, "Moderation:BlockInviteLinks", settings.ModeratorNoInviteLinks);
        Add(values, "Moderation:IntroductionWatcherEnabled", settings.IntroductionWatcherServiceEnabled);
        Add(values, "Moderation:MutedRoleId", settings.MutedRoleId);
        Add(values, "Moderation:IntroductionChannelId", settings.IntroductionChannel?.Id);
        Add(values, "Moderation:GeneralChannelId", settings.GeneralChannel?.Id);
        Add(values, "Moderation:ReportedMessageChannelId", settings.ReportedMessageChannel?.Id);
        Add(values, "Moderation:MemeChannelId", settings.MemeChannel?.Id);
        Add(values, "Moderation:RulesChannelId", settings.RulesChannel?.Id);

        Add(values, "Tickets:OpenCategoryId", settings.ComplaintCategoryId);
        Add(values, "Tickets:OpenChannelPrefix", settings.ComplaintChannelPrefix);
        Add(values, "Tickets:ClosedCategoryId", settings.ClosedComplaintCategoryId);
        Add(values, "Tickets:ClosedChannelPrefix", settings.ClosedComplaintChannelPrefix);

        Add(values, "Feeds:NewsChannelId", settings.UnityNewsChannel?.Id);
        Add(values, "Feeds:ReleasesChannelId", settings.UnityReleasesChannel?.Id);
        Add(values, "Feeds:NewsSubscriberRoleId", settings.SubsNewsRoleId);
        Add(values, "Feeds:ReleasesSubscriberRoleId", settings.SubsReleasesRoleId);

        Add(values, "Recruitment:Enabled", settings.RecruitmentServiceEnabled);
        // The old single forum cannot identify the four new forums. Keeping Enabled
        // lets feature validation report migration rather than silently misrouting posts.

        Add(values, "UnityHelp:Enabled", settings.UnityHelpBabySitterEnabled);
        Add(values, "UnityHelp:ForumChannelId", settings.GenericHelpChannel?.Id);
        Add(values, "UnityHelp:ResolvedTagId", ParseId(settings.TagUnitHelpResolvedTag));

        Add(values, "BirthdayAnnouncements:Enabled", settings.BirthdayAnnouncementEnabled);
        Add(values, "BirthdayAnnouncements:CheckIntervalMinutes", settings.BirthdayCheckIntervalMinutes);
        Add(values, "BirthdayAnnouncements:AnnouncementChannelId", settings.BirthdayAnnouncementChannel?.Id);
        Add(values, "Reminders:FallbackChannelId", settings.BotCommandsChannel?.Id);

        Add(values, "Tips:ImageDirectory", settings.TipImageDirectory);
        Add(values, "Tips:MaxImageBytes", settings.TipMaxImageFileSize);
        Add(values, "Tips:MaxDirectoryBytes", settings.TipMaxDirectoryFileSize);
        Add(values, "Tips:HelperRoleId", settings.TipsUserRoleId);

        Add(values, "Casino:Enabled", settings.CasinoEnabled);
        Add(values, "Casino:StartingTokens", settings.CasinoStartingTokens);
        AddList(values, "Casino:AllowedChannelIds", settings.CasinoAllowedChannels);
        Add(values, "Casino:GameTimeoutMinutes", settings.CasinoGameTimeoutMinutes);
        Add(values, "Casino:DailyRewardTokens", settings.CasinoDailyRewardTokens);
        Add(values, "Casino:DailyRewardIntervalSeconds", settings.CasinoDailyRewardIntervalSeconds);

        Add(values, "Weather:ApiKey", settings.WeatherAPIKey);
        Add(values, "Airport:FlightApiKey", settings.FlightAPIKey);
        Add(values, "Airport:FlightApiSecret", settings.FlightAPISecret);
        Add(values, "Airport:AirLabsApiKey", settings.AirLabAPIKey);
        Add(values, "KnowledgeSearch:WikipediaSearchPage", settings.WikipediaSearchPage);

        return values;
    }

    private static ulong? ParseId(string? value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    private static void Add(
        IDictionary<string, string?> values,
        string key,
        object? value)
    {
        if (value is null)
            return;

        values[key] = value switch
        {
            bool boolean => boolean.ToString(CultureInfo.InvariantCulture),
            char character when character != default => character.ToString(),
            char => null,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private static void AddList<T>(
        IDictionary<string, string?> values,
        string key,
        IEnumerable<T>? items)
    {
        if (items is null)
            return;

        var index = 0;
        foreach (var item in items)
        {
            Add(values, $"{key}:{index}", item);
            index++;
        }
    }
}
