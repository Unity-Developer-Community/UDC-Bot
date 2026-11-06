namespace CasinoWeb.Configuration;

/// <summary>The web casino's own settings (environment variables <c>Casino__*</c>). Not to be confused with the bot's <c>Casino</c> section.</summary>
public sealed class CasinoHostOptions
{
    public const string SectionName = "Casino";

    /// <summary>The address players type in the browser. OAuth redirect URLs and the WebSocket Origin check derive from it.</summary>
    public string PublicOrigin { get; set; } = string.Empty;

    /// <summary>Where Data Protection keys persist so a restart does not log everyone out. Required outside Development; there, empty uses the framework default.</summary>
    public string? KeysPath { get; set; }

    /// <summary>The bot's <c>Settings.json</c>; the casino reads only <c>GuildId</c>, <c>DbConnectionString</c> and <c>Casino</c>.</summary>
    public string BotSettingsPath { get; set; } = "Settings/Settings.json";

    public Uri PublicOriginUri => new(PublicOrigin);

    public static bool IsValid(CasinoHostOptions options) =>
        Uri.TryCreate(options.PublicOrigin, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && uri.AbsolutePath == "/"
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.UserInfo);
}

/// <summary>The Discord application the bot already uses (environment variables <c>Discord__*</c>). The bot token is never read.</summary>
public sealed class DiscordOAuthOptions
{
    public const string SectionName = "Discord";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
