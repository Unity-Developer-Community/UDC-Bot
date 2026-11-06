using CasinoWeb.Configuration;
using DiscordBot.Settings;

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
}
