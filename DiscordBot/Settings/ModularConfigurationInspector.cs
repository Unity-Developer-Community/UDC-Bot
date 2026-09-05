using System.IO;
using System.Globalization;
using System.Text.Json;
using DiscordBot.Settings.Legacy;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Configuration;

namespace DiscordBot.Settings;

internal static class ModularConfigurationInspector
{
    private static readonly IReadOnlyDictionary<string, Type> CoreSections = CreateSections(
        typeof(DiscordConnectionOptions),
        typeof(DiscordGuildOptions),
        typeof(StorageOptions),
        typeof(DatabaseOptions),
        typeof(CommandOptions),
        typeof(LoggingOptions),
        typeof(AuthorizationOptions));

    private static readonly IReadOnlyDictionary<string, Type> FeatureSections = CreateSections(
        typeof(UserActivityOptions),
        typeof(UserFunOptions),
        typeof(RoleAssignmentOptions),
        typeof(ModerationOptions),
        typeof(TicketOptions),
        typeof(FeedOptions),
        typeof(RecruitmentOptions),
        typeof(UnityHelpOptions),
        typeof(BirthdayOptions),
        typeof(ReminderOptions),
        typeof(TipsOptions),
        typeof(CasinoOptions),
        typeof(WeatherOptions),
        typeof(AirportOptions),
        typeof(KnowledgeSearchOptions));

    public static IReadOnlyList<string> InspectCore(string path) => Inspect(path, CoreSections);

    public static IReadOnlyList<string> InspectFeatures(string path) => Inspect(path, FeatureSections);

    private static IReadOnlyList<string> Inspect(
        string path,
        IReadOnlyDictionary<string, Type> sections)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(path),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("The root JSON value must be an object.");

            var source = Path.GetFileName(path);
            var unknown = new List<string>();
            foreach (var section in document.RootElement.EnumerateObject())
            {
                if (!sections.TryGetValue(section.Name, out var optionsType))
                {
                    unknown.Add($"{source}:{section.Name}");
                    continue;
                }

                if (section.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var knownProperties = optionsType.GetProperties()
                    .Select(property => property.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                unknown.AddRange(section.Value.EnumerateObject()
                    .Where(property => !knownProperties.Contains(property.Name))
                    .Select(property => $"{source}:{section.Name}:{property.Name}"));
            }

            return unknown.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (JsonException exception)
        {
            throw new BotConfigurationException(
                $"Bot configuration at '{path}' is malformed. The file was not changed. {exception.Message}",
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BotConfigurationException($"Unable to read bot configuration at '{path}'.", exception);
        }
    }

    private static IReadOnlyDictionary<string, Type> CreateSections(params Type[] optionTypes) =>
        optionTypes.ToDictionary(
            type => (string)(type.GetField("SectionName")?.GetRawConstantValue()
                ?? throw new InvalidOperationException($"{type.Name} does not declare SectionName.")),
            StringComparer.OrdinalIgnoreCase);
}

internal static class CoreConfigurationShapeValidator
{
    private static readonly (string Key, Func<string, bool> IsValid)[] Rules =
    [
        ("DiscordGuild:GuildId", value => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _)),
        ("Commands:Prefix", value => value.Length == 1 && !char.IsWhiteSpace(value[0])),
        ("Commands:BotCommandsChannelId", value => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _)),
        ("Logging:LogCommandExecutions", value => bool.TryParse(value, out _)),
        ("Logging:AnnouncementChannelId", value => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _)),
        ("Authorization:ModeratorRoleId", value => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
    ];

    public static void Validate(IConfiguration configuration)
    {
        var invalidKeys = Rules
            .Where(rule => configuration[rule.Key] is { } value && !rule.IsValid(value))
            .Select(rule => rule.Key)
            .ToArray();
        if (invalidKeys.Length > 0)
        {
            throw new BotConfigurationException(
                $"Core configuration contains invalid values for: {string.Join(", ", invalidKeys)}.");
        }
    }
}
