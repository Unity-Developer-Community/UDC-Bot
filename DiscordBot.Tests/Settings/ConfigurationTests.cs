using DiscordBot.Components;
using DiscordBot.Hosting;
using DiscordBot.Settings.Legacy;
using DiscordBot.Settings;
using DiscordBot.Settings.Options;
using DiscordBot.Settings.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Settings;

[TestClass]
public sealed class ConfigurationTests
{
    [TestMethod]
    public void ModularJson_BindsRequiredConnectionValues()
    {
        using var root = TestConfigurationRoot.Create();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = root.Path
        });
        builder.AddBotConfiguration(root.Path);
        using var provider = builder.Services.BuildServiceProvider();

        Assert.AreEqual(
            "file-token",
            provider.GetRequiredService<IOptions<DiscordConnectionOptions>>().Value.Token);
        Assert.AreEqual(
            "Host=localhost;Database=test",
            provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString);
    }

    [TestMethod]
    public void ModularConfiguration_ReportsMissingCompanionFile()
    {
        using var root = TestConfigurationRoot.Create();
        File.Delete(Path.Combine(root.Path, "Settings", "FeatureSettings.json"));
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = root.Path
        });

        var report = builder.AddBotConfiguration(root.Path);

        CollectionAssert.AreEqual(
            new[] { "FeatureSettings.json" },
            report.MissingModularFiles.ToArray());
    }

    [TestMethod]
    public void BuildHost_MissingConfiguration_FailsWithoutCreatingAFile()
    {
        using var root = TestConfigurationRoot.Create(includeCoreSettings: false);
        var expectedPath = Path.Combine(root.Path, "Settings", "Settings.json");

        var exception = Assert.Throws<BotConfigurationException>(() =>
            Program.BuildHost([], root.Path));

        StringAssert.Contains(exception.Message, "Settings/CoreSettings.example.json");
        Assert.IsFalse(File.Exists(expectedPath));
    }

    [TestMethod]
    public void LegacyLoader_MalformedConfiguration_PreservesOriginalFile()
    {
        using var root = TestConfigurationRoot.Create(includeCoreSettings: false);
        var settingsPath = Path.Combine(root.Path, "Settings", "Settings.json");
        const string malformed = "{ not-json";
        File.WriteAllText(settingsPath, malformed);

        Assert.Throws<BotConfigurationException>(() =>
            LegacyConfigurationLoader.Load(
                settingsPath,
                Path.Combine(root.Path, "Settings", "UserSettings.json")));

        Assert.AreEqual(malformed, File.ReadAllText(settingsPath));
    }

    [TestMethod]
    public void LegacyLoader_ReportsUnknownKeysWithoutIncludingValues()
    {
        using var root = TestConfigurationRoot.Create(includeCoreSettings: false);
        var settingsPath = Path.Combine(root.Path, "Settings", "Settings.json");
        File.WriteAllText(settingsPath, """
        {
          "Token": "secret-value",
          "UnexpectedSetting": "do-not-report-me"
        }
        """);

        var configuration = LegacyConfigurationLoader.Load(
            settingsPath,
            Path.Combine(root.Path, "Settings", "UserSettings.json"));

        CollectionAssert.AreEqual(
            new[] { "UnexpectedSetting" },
            configuration.Report.UnknownKeys.ToArray());
        Assert.IsFalse(string.Join(' ', configuration.Report.UnknownKeys).Contains("do-not-report-me"));
    }

    [TestMethod]
    public void ModularConfiguration_ReportsUnknownKeysWithoutIncludingValues()
    {
        using var root = TestConfigurationRoot.Create();
        var corePath = Path.Combine(root.Path, "Settings", "CoreSettings.json");
        File.WriteAllText(corePath, """
        {
          "DiscordGuild": {
            "GuildId": 1,
            "Invite": "https://example.test/invite",
            "UnexpectedValue": "do-not-report-me"
          },
          "Storage": { "ServerRootPath": "./SERVER", "AssetsRootPath": "./Assets" },
          "Commands": { "Prefix": "!", "BotCommandsChannelId": 2 },
          "Logging": { "LogCommandExecutions": true, "AnnouncementChannelId": 3 },
          "Authorization": { "ModeratorRoleId": 4 },
          "UnexpectedSection": { "Value": "also-secret" }
        }
        """);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = root.Path
        });
        var report = builder.AddBotConfiguration(root.Path);

        CollectionAssert.AreEqual(
            new[]
            {
                "CoreSettings.json:DiscordGuild:UnexpectedValue",
                "CoreSettings.json:UnexpectedSection"
            },
            report.UnknownKeys.ToArray());
        var diagnostics = string.Join(' ', report.UnknownKeys);
        Assert.IsFalse(diagnostics.Contains("do-not-report-me", StringComparison.Ordinal));
        Assert.IsFalse(diagnostics.Contains("also-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ModularConfiguration_MalformedFileIsPreserved()
    {
        using var root = TestConfigurationRoot.Create();
        var featurePath = Path.Combine(root.Path, "Settings", "FeatureSettings.json");
        const string malformed = "{ not-json";
        File.WriteAllText(featurePath, malformed);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = root.Path
        });

        Assert.Throws<BotConfigurationException>(() => builder.AddBotConfiguration(root.Path));

        Assert.AreEqual(malformed, File.ReadAllText(featurePath));
    }

    [TestMethod]
    public void InvalidOptionalFeature_IsUnavailableWithoutBecomingCoreValidation()
    {
        using var root = TestConfigurationRoot.Create();
        File.WriteAllText(
            Path.Combine(root.Path, "Settings", "FeatureSettings.json"),
            """{ "UserActivity": { "XpMinPerMessage": 30, "XpMaxPerMessage": 20 } }""");
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = root.Path
        });
        builder.AddBotConfiguration(root.Path);
        using var provider = builder.Services.BuildServiceProvider();

        var status = provider.GetRequiredService<FeatureConfigurationCatalog>()
            .Get(ComponentIds.UserActivity);

        Assert.IsFalse(status.IsConfigured);
        StringAssert.Contains(string.Join(' ', status.Errors), "UserActivity:XpMaxPerMessage");
    }

    [TestMethod]
    public void InvalidOptionalFeatureType_IsReportedWithoutExposingItsValue()
    {
        using var root = TestConfigurationRoot.Create();
        const string invalidValue = "sensitive-not-a-number";
        File.WriteAllText(
            Path.Combine(root.Path, "Settings", "FeatureSettings.json"),
            $$"""{ "UserActivity": { "XpMinPerMessage": "{{invalidValue}}" } }""");
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = root.Path
        });
        builder.AddBotConfiguration(root.Path);
        using var provider = builder.Services.BuildServiceProvider();

        var status = provider.GetRequiredService<FeatureConfigurationCatalog>()
            .Get(ComponentIds.UserActivity);
        var diagnostics = string.Join(' ', status.Errors);

        Assert.IsFalse(status.IsConfigured);
        StringAssert.Contains(diagnostics, UserActivityOptions.SectionName);
        Assert.IsFalse(diagnostics.Contains(invalidValue, StringComparison.Ordinal));
    }

    [TestMethod]
    public void InvalidCoreType_FailsWithKeyNameAndWithoutItsValue()
    {
        using var root = TestConfigurationRoot.Create();
        var corePath = Path.Combine(root.Path, "Settings", "CoreSettings.json");
        const string invalidValue = "sensitive-not-an-id";
        File.WriteAllText(corePath, File.ReadAllText(corePath).Replace(
            "\"BotCommandsChannelId\": 2",
            $"\"BotCommandsChannelId\": \"{invalidValue}\"",
            StringComparison.Ordinal));
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = root.Path
        });

        var exception = Assert.Throws<BotConfigurationException>(() =>
            builder.AddBotConfiguration(root.Path));

        StringAssert.Contains(exception.Message, "Commands:BotCommandsChannelId");
        Assert.IsFalse(exception.Message.Contains(invalidValue, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Host_InvalidCoreConfiguration_FailsBeforeGatewayLogin()
    {
        using var root = TestConfigurationRoot.Create();
        var corePath = Path.Combine(root.Path, "Settings", "CoreSettings.json");
        File.WriteAllText(
            corePath,
            File.ReadAllText(corePath).Replace("\"Token\": \"file-token\"", "\"Token\": \"\"", StringComparison.Ordinal));
        var gateway = new FakeGateway();
        var runtime = new FakeRuntimeCoordinator();
        using var host = BuildTestHost(root.Path, gateway, runtime);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        StringAssert.Contains(exception.Message, "DiscordConnection:Token");
        Assert.AreEqual(0, gateway.LoginCount);
        Assert.AreEqual(0, runtime.StartCount);
    }

    [TestMethod]
    public async Task Host_ReadyAndShutdown_AreAwaitedExactlyOnce()
    {
        using var root = TestConfigurationRoot.Create();
        var gateway = new FakeGateway();
        var runtime = new FakeRuntimeCoordinator();
        using var host = BuildTestHost(root.Path, gateway, runtime);

        await host.StartAsync();
        await host.StopAsync();

        Assert.AreEqual(1, gateway.LoginCount);
        Assert.AreEqual(1, gateway.StartCount);
        Assert.AreEqual(1, gateway.StopCount);
        Assert.AreEqual(1, runtime.StartCount);
        Assert.AreEqual(1, runtime.StopCount);
    }

    private static IHost BuildTestHost(
        string root,
        FakeGateway gateway,
        FakeRuntimeCoordinator runtime)
    {
        return Program.BuildHost(
            [],
            root,
            services =>
            {
                services.AddSingleton<IDiscordGateway>(gateway);
                services.AddSingleton<IBotRuntimeCoordinator>(runtime);
            });
    }

    private sealed class FakeGateway : IDiscordGateway
    {
        public event Func<Task>? Ready;

        public int LoginCount { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task LoginAsync(CancellationToken cancellationToken)
        {
            LoginCount++;
            return Task.CompletedTask;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            if (Ready is not null)
                await Ready.Invoke();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRuntimeCoordinator : IBotRuntimeCoordinator
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }
}

internal sealed class TestConfigurationRoot : IDisposable
{
    private TestConfigurationRoot(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TestConfigurationRoot Create(bool includeCoreSettings = true)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"udc-bot-settings-{Guid.NewGuid():N}");
        var settings = System.IO.Path.Combine(root, "Settings");
        Directory.CreateDirectory(settings);
        File.WriteAllText(System.IO.Path.Combine(settings, "Rules.json"), "{ \"Channel\": [] }");
        File.WriteAllText(System.IO.Path.Combine(settings, "FAQs.json"), "[]");

        if (includeCoreSettings)
        {
            File.WriteAllText(System.IO.Path.Combine(settings, "CoreSettings.json"), """
            {
              "DiscordConnection": { "Token": "file-token" },
              "DiscordGuild": { "GuildId": 1, "Invite": "https://example.test/invite" },
              "Storage": { "ServerRootPath": "./SERVER", "AssetsRootPath": "./Assets" },
              "Database": { "ConnectionString": "Host=localhost;Database=test" },
              "Commands": { "Prefix": "!", "BotCommandsChannelId": 2 },
              "Logging": { "LogCommandExecutions": true, "AnnouncementChannelId": 3 },
              "Authorization": { "ModeratorRoleId": 4 }
            }
            """);
            File.WriteAllText(System.IO.Path.Combine(settings, "FeatureSettings.json"), "{}");
        }

        return new TestConfigurationRoot(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
