using DiscordBot.Settings;
using Newtonsoft.Json;

namespace CasinoWeb.Configuration;

public static class BotSettingsLoader
{
    /// <summary>
    /// Reads the bot's settings file without creating it when missing (the bot's own loader would) and checks the parts
    /// the casino uses. Other placeholders in the file stay unrendered and unread.
    /// </summary>
    public static BotSettings Load(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Bot settings file not found at '{Path.GetFullPath(path)}'. Set Casino__BotSettingsPath.");

        BotSettings settings;
        try
        {
            settings = JsonConvert.DeserializeObject<BotSettings>(File.ReadAllText(path))
                ?? throw new InvalidOperationException($"Bot settings file '{path}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Bot settings file '{path}' is not valid JSON: {ex.Message}", ex);
        }

        var errors = Validate(settings);
        if (errors.Count > 0)
            throw new InvalidOperationException($"Bot settings are not usable by the casino: {string.Join("; ", errors)}");
        return settings;
    }

    public static List<string> Validate(BotSettings settings)
    {
        var errors = new List<string>();
        if (settings.GuildId == 0)
            errors.Add("GuildId is not configured");
        if (string.IsNullOrWhiteSpace(settings.DbConnectionString))
            errors.Add("DbConnectionString is not configured");
        if (settings.Casino.StartingTokens < 0)
            errors.Add("Casino.StartingTokens is negative");
        return errors;
    }
}
