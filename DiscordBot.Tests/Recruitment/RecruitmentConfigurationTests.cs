using DiscordBot;
using DiscordBot.Components;
using DiscordBot.Services;
using DiscordBot.Services.Recruitment;
using DiscordBot.Settings;
using DiscordBot.Settings.Options;
using DiscordBot.Settings.Validation;
using DiscordBot.Tests.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Recruitment;

[TestClass]
public sealed class RecruitmentConfigurationTests
{
    [TestMethod]
    public void FourForums_BindExactSnowflakes_AndEnvironmentStyleOverrides()
    {
        var values = ValidConfiguration();
        values["Recruitment:Forums:PaidRecruiting:ChannelId"] = "1545677520513540099";
        using var provider = Provider(values, new() { ["Recruitment:Forums:PaidForHire:ChannelId"] = "1545677571780255766" });
        var options = provider.GetRequiredService<IOptions<RecruitmentOptions>>().Value;
        Assert.AreEqual(1545677520513540099ul, options.Forums.PaidRecruiting.ChannelId);
        Assert.AreEqual(1545677571780255766ul, options.Forums.PaidForHire.ChannelId);
        Assert.AreEqual(103ul, options.Forums.HobbyRecruiting.ChannelId);
        Assert.IsTrue(Status(provider).IsConfigured);
    }

    [TestMethod]
    [DataRow("Recruitment:Forums:PaidRecruiting:ChanelId", "7")]
    [DataRow("Recruitment:Forums:PaidRecruiting:ChannelId", "not-an-id")]
    [DataRow("Recruitment:Forums:PaidRecruiting:ChannelId", "0")]
    [DataRow("Recruitment:Forums:PaidRecruiting:ChannelId", "102")]
    [DataRow("Recruitment:Forums:Unknown:ChannelId", "9")]
    [DataRow("Recruitment:Forums", "invalid-shape")]
    [DataRow("Recruitment:Mode", "99")]
    [DataRow("Recruitment:Mode", "Typo")]
    [DataRow("Recruitment:Mode:Nested", "Observe")]
    [DataRow("Recruitment:FeedChannelId", "101")]
    [DataRow("Recruitment:AcknowledgementMinutes", "0")]
    [DataRow("Recruitment:CooldownDays", "366")]
    [DataRow("Recruitment:GuidelinesDirectory", "../Settings")]
    [DataRow("Recruitment:GuidelinesDirectory", "/tmp/guidelines")]
    [DataRow("Recruitment:PostRetentionMonths", "1")]
    [DataRow("Recruitment:AuthorRetentionMonths", "1")]
    public void InvalidOptionalConfiguration_IsUnavailableWithoutExposingValues(string key, string value)
    {
        var values = ValidConfiguration(); values[key] = value;
        using var provider = Provider(values);
        var status = Status(provider);
        Assert.IsFalse(status.IsConfigured);
        Assert.IsFalse(string.Join(' ', status.Errors).Contains("not-an-id", StringComparison.Ordinal));
        // Resolving the classifier, used by UserService, must not propagate binding failures.
        _ = new RecruitmentForumClassifier(provider.GetRequiredService<IOptions<RecruitmentOptions>>());
    }

    [TestMethod]
    public void DisabledMalformedFeature_DoesNotBreakOtherServices()
    {
        using var provider = Provider(new()
        {
            ["Recruitment:Enabled"] = "false", ["Recruitment:Mode"] = "unfinished",
            ["Recruitment:Forums:PaidRecruiting:ChannelId"] = "unfinished"
        });
        Assert.IsTrue(Status(provider).IsConfigured);
        var classifier = new RecruitmentForumClassifier(provider.GetRequiredService<IOptions<RecruitmentOptions>>());
        Assert.IsNull(classifier.Classify(101));
    }

    [TestMethod]
    public void ForumClassifier_FiltersParentsAndChildren_WhileModerationIsDisabled()
    {
        var options = RecruitmentTestData.Options(); options.Enabled = false;
        var classifier = new RecruitmentForumClassifier(Microsoft.Extensions.Options.Options.Create(options));
        for (ulong id = 101; id <= 104; id++)
        {
            Assert.IsNotNull(classifier.Classify(id));
            Assert.IsNotNull(classifier.Classify(555, id));
        }
        Assert.IsNull(classifier.Classify(555, 999));
        Assert.IsNull(classifier.Classify(0, 0));
    }

    [TestMethod]
    public void NestedUnknownKeys_AppearInStartupReport()
    {
        using var root = TestConfigurationRoot.Create();
        File.WriteAllText(Path.Combine(root.Path, "Settings/FeatureSettings.json"),
            """{ "Recruitment": { "Forums": { "PaidRecruiting": { "ChanelId": 7 } } } }""");
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = root.Path });
        var report = builder.AddBotConfiguration(root.Path);
        CollectionAssert.Contains(report.UnknownKeys.ToArray(), "FeatureSettings.json:Recruitment:Forums:PaidRecruiting:ChanelId");
    }

    [TestMethod]
    public void EmptyScalarObject_IsInvalidRatherThanSilentlyUsingDefault()
    {
        using var root = TestConfigurationRoot.Create();
        var json = System.Text.Json.JsonSerializer.Serialize(RecruitmentTestData.Options());
        json = json.Replace("\"Mode\":2", "\"Mode\":{}");
        File.WriteAllText(Path.Combine(root.Path, "Settings/FeatureSettings.json"), "{\"Recruitment\":" + json + "}");
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = root.Path });
        builder.AddBotConfiguration(root.Path);
        using var provider = builder.Services.BuildServiceProvider();
        Assert.IsFalse(Status(provider).IsConfigured);
    }

    [TestMethod]
    public void LegacyEnabledFeature_RequiresExplicitFourForumMigration_ModularDisableWins()
    {
        using var root = TestConfigurationRoot.Create();
        File.WriteAllText(Path.Combine(root.Path, "Settings/Settings.json"),
            """{ "RecruitmentServiceEnabled": true, "RecruitmentChannel": { "Id": 123 } }""");
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = root.Path });
        builder.AddBotConfiguration(root.Path);
        using (var provider = builder.Services.BuildServiceProvider())
        {
            Assert.IsFalse(Status(provider).IsConfigured);
            StringAssert.Contains(string.Join(' ', Status(provider).Errors), "migrate");
            Assert.IsNull(builder.Configuration["Recruitment:ForumChannelId"]);
        }
        File.WriteAllText(Path.Combine(root.Path, "Settings/FeatureSettings.json"), """{ "Recruitment": { "Enabled": false } }""");
        var disabled = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = root.Path });
        disabled.AddBotConfiguration(root.Path);
        using var disabledProvider = disabled.Services.BuildServiceProvider();
        Assert.IsTrue(Status(disabledProvider).IsConfigured);
    }

    [TestMethod]
    public void HostBuilds_WithMalformedRecruitment_AndXpDependenciesStillResolve()
    {
        using var root = TestConfigurationRoot.Create();
        File.WriteAllText(Path.Combine(root.Path, "Settings/FeatureSettings.json"),
            """{ "Recruitment": { "Enabled": true, "Mode": "bad-mode" } }""");
        using var host = Program.BuildHost([], root.Path);
        Assert.IsFalse(Status(host.Services).IsConfigured);
        Assert.IsNotNull(host.Services.GetRequiredService<RecruitmentForumClassifier>());
        Assert.IsNotNull(host.Services.GetRequiredService<IOptions<DiscordGuildOptions>>().Value);
    }

    [TestMethod]
    public async Task FoundationRuntime_RejectsStart_AndNeverClaimsToBeRunning()
    {
        var service = new RecruitService();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));
        StringAssert.Contains(error.Message, "event coordinator");
        await service.StopAsync(CancellationToken.None);
        Assert.IsFalse(service.IsRunning);
    }

    private static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["Recruitment:Enabled"] = "true", ["Recruitment:Mode"] = "Observe",
        ["Recruitment:Forums:PaidRecruiting:ChannelId"] = "101",
        ["Recruitment:Forums:PaidForHire:ChannelId"] = "102",
        ["Recruitment:Forums:HobbyRecruiting:ChannelId"] = "103",
        ["Recruitment:Forums:HobbyForHire:ChannelId"] = "104",
        ["Recruitment:FeedChannelId"] = "105"
    };

    private static ServiceProvider Provider(Dictionary<string, string?> values, Dictionary<string, string?>? overrides = null)
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(values);
        if (overrides is not null) builder.AddInMemoryCollection(overrides);
        var config = builder.Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddDomainOptions(config);
        return services.BuildServiceProvider();
    }

    private static FeatureConfigurationStatus Status(IServiceProvider provider) =>
        provider.GetRequiredService<FeatureConfigurationCatalog>().Get(ComponentIds.Recruitment);
}
