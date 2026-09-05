using System.IO;
using DiscordBot.Settings.Legacy;
using DiscordBot.Settings.Options;
using DiscordBot.Settings.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DiscordBot.Settings;

public static class ConfigurationRegistrationExtensions
{
    private const string EnvironmentVariablePrefix = "UDCBOT_";

    public const string LegacySettingsRelativePath = "Settings/Settings.json";
    public const string CoreSettingsRelativePath = "Settings/CoreSettings.json";
    public const string FeatureSettingsRelativePath = "Settings/FeatureSettings.json";
    public const string UserSettingsRelativePath = "Settings/UserSettings.json";

    public static LegacyConfigurationReport AddBotConfiguration(
        this HostApplicationBuilder builder,
        string? contentRootPath = null)
    {
        var root = contentRootPath ?? builder.Environment.ContentRootPath;
        var legacyPath = Path.Combine(root, LegacySettingsRelativePath);
        var corePath = Path.Combine(root, CoreSettingsRelativePath);
        var featurePath = Path.Combine(root, FeatureSettingsRelativePath);
        var userSettingsPath = Path.Combine(root, UserSettingsRelativePath);

        var hasLegacy = File.Exists(legacyPath);
        var hasCoreSettings = File.Exists(corePath);
        var hasFeatureSettings = File.Exists(featurePath);
        var hasModularConfiguration = hasCoreSettings || hasFeatureSettings;
        if (!hasLegacy && !hasModularConfiguration)
        {
            throw new BotConfigurationException(
                $"Required bot configuration was not found under '{Path.Combine(root, "Settings")}'. " +
                "Copy Settings/CoreSettings.example.json and Settings/FeatureSettings.example.json to their non-example names, " +
                "then fill in the required values. " +
                "Settings/Settings.json remains available as a read-only legacy source.");
        }

        LegacyConfigurationReport report;
        IReadOnlyDictionary<string, string?> legacyValues;
        if (hasLegacy)
        {
            var legacy = LegacyConfigurationLoader.Load(legacyPath, userSettingsPath);
            legacyValues = LegacyBotSettingsAdapter.Project(legacy);
            report = legacy.Report;
        }
        else
        {
            legacyValues = new Dictionary<string, string?>();
            report = new LegacyConfigurationReport(Path.Combine(root, "Settings"), [], false, []);
        }

        var missingModularFiles = new List<string>(2);
        if (!hasCoreSettings)
            missingModularFiles.Add(Path.GetFileName(corePath));
        if (!hasFeatureSettings)
            missingModularFiles.Add(Path.GetFileName(featurePath));

        report = report with
        {
            UnknownKeys = report.UnknownKeys
                .Concat(ModularConfigurationInspector.InspectCore(corePath))
                .Concat(ModularConfigurationInspector.InspectFeatures(featurePath))
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            MissingModularFiles = missingModularFiles
        };

        builder.Configuration.Sources.Clear();
        builder.Configuration.SetBasePath(root);
        builder.Configuration.AddInMemoryCollection(legacyValues);
        builder.Configuration.AddJsonFile(CoreSettingsRelativePath, optional: true, reloadOnChange: false);
        builder.Configuration.AddJsonFile(FeatureSettingsRelativePath, optional: true, reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables(EnvironmentVariablePrefix);
        CoreConfigurationShapeValidator.Validate(builder.Configuration);

        builder.Services.AddSingleton(report);
        builder.Services.AddDomainOptions(builder.Configuration);
        return report;
    }

    public static IServiceCollection AddDomainOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<DiscordConnectionOptions>, DiscordConnectionOptionsValidator>();
        services.AddSingleton<IValidateOptions<DiscordGuildOptions>, DiscordGuildOptionsValidator>();
        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        services.AddSingleton<IValidateOptions<CommandOptions>, CommandOptionsValidator>();
        services.AddSingleton<IValidateOptions<AuthorizationOptions>, AuthorizationOptionsValidator>();

        services.AddOptions<DiscordConnectionOptions>()
            .Bind(configuration.GetSection(DiscordConnectionOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<DiscordGuildOptions>()
            .Bind(configuration.GetSection(DiscordGuildOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<CommandOptions>()
            .Bind(configuration.GetSection(CommandOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<LoggingOptions>()
            .Bind(configuration.GetSection(LoggingOptions.SectionName));
        services.AddOptions<AuthorizationOptions>()
            .Bind(configuration.GetSection(AuthorizationOptions.SectionName))
            .ValidateOnStart();

        AddFeatureOptions<UserActivityOptions>(services, configuration, UserActivityOptions.SectionName);
        AddFeatureOptions<UserFunOptions>(services, configuration, UserFunOptions.SectionName);
        AddFeatureOptions<RoleAssignmentOptions>(services, configuration, RoleAssignmentOptions.SectionName);
        AddFeatureOptions<ModerationOptions>(services, configuration, ModerationOptions.SectionName);
        AddFeatureOptions<TicketOptions>(services, configuration, TicketOptions.SectionName);
        AddFeatureOptions<FeedOptions>(services, configuration, FeedOptions.SectionName);
        AddFeatureOptions<RecruitmentOptions>(services, configuration, RecruitmentOptions.SectionName);
        AddFeatureOptions<UnityHelpOptions>(services, configuration, UnityHelpOptions.SectionName);
        AddFeatureOptions<BirthdayOptions>(services, configuration, BirthdayOptions.SectionName);
        AddFeatureOptions<ReminderOptions>(services, configuration, ReminderOptions.SectionName);
        AddFeatureOptions<TipsOptions>(services, configuration, TipsOptions.SectionName);
        AddFeatureOptions<CasinoOptions>(services, configuration, CasinoOptions.SectionName);
        AddFeatureOptions<WeatherOptions>(services, configuration, WeatherOptions.SectionName);
        AddFeatureOptions<AirportOptions>(services, configuration, AirportOptions.SectionName);
        AddFeatureOptions<KnowledgeSearchOptions>(services, configuration, KnowledgeSearchOptions.SectionName);

        services.AddSingleton<IFeatureConfigurationValidator, RecruitmentOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, UserActivityOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, UnityHelpOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, BirthdayOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, IntroductionWatcherOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, ReminderOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, TicketOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, FeedOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, UserFunOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, RoleAssignmentOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, KnowledgeSearchOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, TipsOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, CasinoOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, WeatherOptionsValidator>();
        services.AddSingleton<IFeatureConfigurationValidator, AirportOptionsValidator>();
        services.AddSingleton<FeatureConfigurationCatalog>();

        return services;
    }

    private static void AddFeatureOptions<T>(
        IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where T : class =>
        services.AddOptions<T>().Bind(configuration.GetSection(sectionName));
}
