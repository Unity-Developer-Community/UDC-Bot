using CasinoWeb.Configuration;
using CasinoWeb.Games;
using CasinoWeb.Logging;
using DiscordBot.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CasinoWeb.Tests;

public class StartupTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string json)
    {
        var path = Path.Combine(_directory, "Settings.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Settings_are_read_from_the_bot_file_and_unrelated_placeholders_are_ignored()
    {
        var path = Write("""
            { "Token": "${BOT_TOKEN}", "GuildId": "123", "DbConnectionString": "Host=db;Password=secret",
              "ApiKeys": { "Weather": "${WEATHER_KEY}" }, "Casino": { "Enabled": false, "StartingTokens": 250, "GameTimeoutMinutes": 7 } }
            """);

        var settings = BotSettingsLoader.Load(path);

        Assert.Equal(123UL, settings.GuildId);
        Assert.Equal("Host=db;Password=secret", settings.DbConnectionString);
        Assert.False(settings.Casino.Enabled);
        Assert.Equal(250, settings.Casino.StartingTokens);
        Assert.Equal(7, settings.Casino.GameTimeoutMinutes);
    }

    [Fact]
    public void A_missing_file_is_an_error_and_is_not_created()
    {
        var path = Path.Combine(_directory, "nope.json");

        Assert.Throws<InvalidOperationException>(() => BotSettingsLoader.Load(path));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("""{ "DbConnectionString": "x" }""", "GuildId")]
    [InlineData("""{ "GuildId": "1" }""", "DbConnectionString")]
    [InlineData("""{ "GuildId": "1", "DbConnectionString": "x", "Casino": { "StartingTokens": -1 } }""", "StartingTokens")]
    public void Settings_the_casino_needs_are_validated(string json, string expectedMention)
    {
        var path = Write(json);

        var error = Assert.Throws<InvalidOperationException>(() => BotSettingsLoader.Load(path));

        Assert.Contains(expectedMention, error.Message);
    }

    [Theory]
    [InlineData("https://casino.example.com", true)]
    [InlineData("http://localhost:5080", true)]
    [InlineData("https://casino.example.com/play", false)]
    [InlineData("casino.example.com", false)]
    [InlineData("", false)]
    [InlineData("ftp://casino.example.com", false)]
    [InlineData("https://user:pass@casino.example.com", false)]
    [InlineData("https://casino.example.com/#x", false)]
    public void The_public_origin_must_be_an_origin(string value, bool valid) =>
        Assert.Equal(valid, CasinoHostOptions.IsValid(new CasinoHostOptions { PublicOrigin = value }));

    private const string ValidWebhook = "https://discord.com/api/webhooks/123456789/abc-DEF_123";

    [Theory]
    [InlineData(ValidWebhook, true)]
    [InlineData("https://ptb.discord.com/api/webhooks/1/x", true)]
    [InlineData("https://canary.discord.com/api/webhooks/1/x", true)]
    [InlineData("https://discordapp.com/api/webhooks/1/x", true)]
    [InlineData("http://discord.com/api/webhooks/1/x", false)]
    [InlineData("https://example.com/api/webhooks/1/x", false)]
    [InlineData("https://discord.com.evil.example/api/webhooks/1/x", false)]
    [InlineData("https://evil.example@discord.com/api/webhooks/1/x", false)]
    [InlineData("https://discord.com:8443/api/webhooks/1/x", false)]
    [InlineData("https://discord.com/api/webhooks/1/x?wait=true", false)]
    [InlineData("https://discord.com/api/webhooks/1/x#y", false)]
    [InlineData("https://discord.com/api/webhooks/abc/x", false)]
    [InlineData("https://discord.com/api/webhooks/1", false)]
    [InlineData("https://discord.com/api/webhooks/1/x/extra", false)]
    [InlineData("https://discord.com/api/v10/webhooks/1/x", false)]
    [InlineData("discord.com/api/webhooks/1/x", false)]
    public void The_announcement_webhook_must_be_a_discord_webhook_address(string value, bool valid) =>
        Assert.Equal(valid, CasinoHostOptions.IsValidAnnouncementWebhook(value, allowEmpty: false));

    [Theory]
    [InlineData(null, true, true)]
    [InlineData("", true, true)]
    [InlineData("  ", true, true)]
    [InlineData(null, false, false)]
    [InlineData("", false, false)]
    public void An_empty_announcement_webhook_is_allowed_in_development_only(string? value, bool allowEmpty, bool valid) =>
        Assert.Equal(valid, CasinoHostOptions.IsValidAnnouncementWebhook(value, allowEmpty));

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Production", "")]
    [InlineData("Production", "http://discord.com/api/webhooks/1/x")]
    [InlineData("Production", "https://example.com/api/webhooks/1/secret-token-x")]
    [InlineData("Development", "https://example.com/api/webhooks/1/secret-token-x")]
    public void The_app_refuses_to_start_without_a_valid_webhook_and_the_message_names_the_setting_not_the_value(string environment, string? url)
    {
        using var factory = new CasinoWebFactory { Environment = environment, AnnouncementWebhookUrl = url };

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var message = (failure as OptionsValidationException ?? failure.InnerException as OptionsValidationException)?.Message ?? failure.ToString();
        Assert.Contains("Casino:AnnouncementWebhookUrl", message);
        Assert.DoesNotContain("secret-token-x", message);
    }

    [Fact]
    public void In_development_without_a_webhook_the_app_starts_and_says_once_that_big_wins_are_not_announced()
    {
        using var factory = new CasinoWebFactory { Environment = "Development", AnnouncementWebhookUrl = null };

        using var client = factory.CreateClient();

        Assert.Single(factory.Logs.Lines, line => line.Message.Contains("big wins are not announced", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_warm_up_says_when_the_slots_pictures_are_ready_and_reports_missing_art_as_an_error()
    {
        var logs = new CapturingLoggerProvider();
        using var loggers = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var options = Options.Create(new CasinoHostOptions { AnnouncementWebhookUrl = ValidWebhook });
        var renderer = new FakeSlotsRenderer();

        using var ready = new SlotsPictureWarmUp(renderer, options, new WebLoggingService(loggers.CreateLogger<WebLoggingService>()));
        await ready.StartAsync(CancellationToken.None);
        await logs.WaitForAsync((level, message) => level == LogLevel.Information && message.Contains("slots pictures ready"));
        await ready.StopAsync(CancellationToken.None);

        renderer.Fail = true;
        using var broken = new SlotsPictureWarmUp(renderer, options, new WebLoggingService(loggers.CreateLogger<WebLoggingService>()));
        await broken.StartAsync(CancellationToken.None);
        var error = await logs.WaitForAsync((level, message) => level == LogLevel.Error);
        Assert.Contains("without a picture", error);
    }
}
