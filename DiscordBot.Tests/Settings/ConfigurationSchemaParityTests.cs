using System.Text.Json;
using DiscordBot.Settings;
using DiscordBot.Settings.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Settings;

[TestClass]
public sealed class ConfigurationSchemaParityTests
{
    private static readonly IReadOnlyDictionary<string, Type> ExampleCoreSections = CreateSchema(
        typeof(DiscordConnectionOptions),
        typeof(DiscordGuildOptions),
        typeof(StorageOptions),
        typeof(DatabaseOptions),
        typeof(CommandOptions),
        typeof(LoggingOptions),
        typeof(AuthorizationOptions));

    private static readonly IReadOnlyDictionary<string, Type> CoreSections = CreateSchema(
        typeof(DiscordGuildOptions),
        typeof(StorageOptions),
        typeof(CommandOptions),
        typeof(LoggingOptions),
        typeof(AuthorizationOptions));

    private static readonly IReadOnlyDictionary<string, Type> FeatureSections = CreateSchema(
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
        typeof(KnowledgeSearchOptions));

    private static readonly IReadOnlyDictionary<string, Type> ExampleFeatureSections = CreateSchema(
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

    [TestMethod]
    public void Examples_ContainEverySupportedSetting()
    {
        AssertSections(
            File.ReadAllText(RepositoryPath("DiscordBot/Settings/CoreSettings.example.json")),
            ExampleCoreSections);
        AssertSections(
            File.ReadAllText(RepositoryPath("DiscordBot/Settings/FeatureSettings.example.json")),
            ExampleFeatureSections);
    }

    [DataTestMethod]
    [DataRow("dev")]
    [DataRow("prod")]
    public void KubernetesConfiguration_IsModularNonSecretAndSchemaAligned(string environment)
    {
        var config = File.ReadAllText(RepositoryPath($"k8s/{environment}/bot-config.yaml"));
        var deployment = File.ReadAllText(RepositoryPath($"k8s/{environment}/bot.yaml"));

        AssertSections(ExtractYamlLiteral(config, "CoreSettings.json"), CoreSections);
        AssertSections(ExtractYamlLiteral(config, "FeatureSettings.json"), FeatureSections);
        Assert.IsFalse(config.Contains("\"Token\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(config.Contains("\"ApiKey\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(config.Contains("\"FlightApiKey\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(config.Contains("\"FlightApiSecret\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(config.Contains("\"AirLabsApiKey\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(config.Contains("\"ConnectionString\"", StringComparison.OrdinalIgnoreCase));

        StringAssert.Contains(deployment, "UDCBOT_DiscordConnection__Token");
        StringAssert.Contains(deployment, "UDCBOT_Database__ConnectionString");
        StringAssert.Contains(deployment, "UDCBOT_Weather__ApiKey");
        StringAssert.Contains(deployment, "UDCBOT_Airport__FlightApiKey");
        StringAssert.Contains(deployment, "UDCBOT_Airport__FlightApiSecret");
        StringAssert.Contains(deployment, "UDCBOT_Airport__AirLabsApiKey");
        Assert.IsFalse(deployment.Contains("envsubst", StringComparison.Ordinal));
        Assert.IsFalse(deployment.Contains("render-config", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DockerCompose_UsesTheDocumentedEnvironmentContract()
    {
        var compose = File.ReadAllText(RepositoryPath("docker-compose.yml"));

        StringAssert.Contains(compose, "UDCBOT_DiscordConnection__Token");
        StringAssert.Contains(compose, "UDCBOT_Database__ConnectionString");
        StringAssert.Contains(compose, "UDCBOT_Weather__ApiKey");
        StringAssert.Contains(compose, "UDCBOT_Airport__FlightApiKey");
        StringAssert.Contains(compose, "UDCBOT_Airport__FlightApiSecret");
        StringAssert.Contains(compose, "UDCBOT_Airport__AirLabsApiKey");
    }

    private static void AssertSections(
        string json,
        IReadOnlyDictionary<string, Type> allowedSections)
    {
        using var document = JsonDocument.Parse(json);
        var actual = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);
        var unknown = actual.Keys.Where(section => !allowedSections.ContainsKey(section)).ToArray();
        Assert.AreEqual(0, unknown.Length, $"Unknown configuration sections: {string.Join(", ", unknown)}");
        foreach (var required in allowedSections)
        {
            Assert.IsTrue(actual.ContainsKey(required.Key), $"Missing configuration section '{required.Key}'.");
            AssertObject(actual[required.Key].Value, required.Value, required.Key);
        }
    }

    private static void AssertObject(JsonElement value, Type type, string path)
    {
        Assert.AreEqual(JsonValueKind.Object, value.ValueKind, $"'{path}' must be an object.");
        var actualProperties = value.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedProperties = type.GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownProperties = actualProperties.Where(property => !expectedProperties.Contains(property)).ToArray();
        var missingProperties = expectedProperties.Where(property => !actualProperties.Contains(property)).ToArray();
        Assert.AreEqual(
            0,
            unknownProperties.Length,
            $"Unknown properties in '{path}': {string.Join(", ", unknownProperties)}");
        Assert.AreEqual(
            0,
            missingProperties.Length,
            $"Missing properties in '{path}': {string.Join(", ", missingProperties)}");
        foreach (var property in type.GetProperties().Where(p =>
                     p.PropertyType == typeof(RecruitmentForumsOptions) || p.PropertyType == typeof(RecruitmentForumOptions)))
            AssertObject(value.GetProperty(property.Name), property.PropertyType, $"{path}:{property.Name}");
    }

    private static IReadOnlyDictionary<string, Type> CreateSchema(params Type[] optionTypes) =>
        optionTypes.ToDictionary(
            type => (string)(type.GetField("SectionName")?.GetRawConstantValue()
                ?? throw new InvalidOperationException($"{type.Name} does not declare SectionName.")),
            StringComparer.OrdinalIgnoreCase);

    private static string ExtractYamlLiteral(string yaml, string key)
    {
        var lines = yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var marker = $"  {key}: |";
        var start = Array.FindIndex(lines, line => line == marker);
        Assert.IsTrue(start >= 0, $"YAML key '{key}' was not found.");

        var content = new List<string>();
        for (var index = start + 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length > 0 && !line.StartsWith("    ", StringComparison.Ordinal))
                break;
            content.Add(line.Length >= 4 ? line[4..] : string.Empty);
        }
        return string.Join('\n', content);
    }

    private static string RepositoryPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../", relativePath));
}
