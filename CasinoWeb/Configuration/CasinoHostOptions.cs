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

    /// <summary>
    /// The Discord webhook that announces big wins in the casino channel. It carries the webhook's token, so it is a secret:
    /// never logged. Required outside Development; there, empty turns the announcements off.
    /// </summary>
    public string? AnnouncementWebhookUrl { get; set; }

    /// <summary>The folder holding <c>slots/</c> and <c>fonts/</c>, from which the announcement picture is drawn. Relative paths start at the working directory.</summary>
    public string AssetsPath { get; set; } = "Assets";

    /// <summary>
    /// Lets a seated player add AI seats at a poker table. They check or call and never touch a balance, so a human who wins
    /// from them is paid tokens created out of nothing: on only in the dev environment and local Development.
    /// </summary>
    public bool AllowAiSeats { get; set; }

    public Uri PublicOriginUri => new(PublicOrigin);

    public bool AnnouncementsConfigured => !string.IsNullOrWhiteSpace(AnnouncementWebhookUrl);

    private static readonly string[] WebhookHosts = ["discord.com", "ptb.discord.com", "canary.discord.com", "discordapp.com"];

    /// <summary>
    /// Only a Discord webhook address, so a mistyped or planted URL cannot send player names elsewhere. Empty is valid
    /// only when <paramref name="allowEmpty"/> (Development).
    /// </summary>
    public static bool IsValidAnnouncementWebhook(string? value, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(value))
            return allowEmpty;

        var parts = Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.AbsolutePath.Split('/') : [];
        return uri is not null
            && uri.Scheme == Uri.UriSchemeHttps
            && WebhookHosts.Contains(uri.Host)
            && uri.IsDefaultPort
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && string.IsNullOrEmpty(uri.UserInfo)
            && parts is ["", "api", "webhooks", { Length: > 0 } id, { Length: > 0 } token]
            && id.All(char.IsAsciiDigit)
            && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }

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
