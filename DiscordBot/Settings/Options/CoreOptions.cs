namespace DiscordBot.Settings.Options;

public sealed class DiscordConnectionOptions
{
    public const string SectionName = "DiscordConnection";

    public string Token { get; set; } = string.Empty;
}

public sealed class DiscordGuildOptions
{
    public const string SectionName = "DiscordGuild";

    public ulong GuildId { get; set; }
    public string Invite { get; set; } = string.Empty;
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string ServerRootPath { get; set; } = string.Empty;
    public string AssetsRootPath { get; set; } = "./Assets";
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class CommandOptions
{
    public const string SectionName = "Commands";

    public char Prefix { get; set; } = '!';
    public ulong BotCommandsChannelId { get; set; }
}

public sealed class LoggingOptions
{
    public const string SectionName = "Logging";

    public bool LogCommandExecutions { get; set; } = true;
    public ulong AnnouncementChannelId { get; set; }
}

public sealed class AuthorizationOptions
{
    public const string SectionName = "Authorization";

    public ulong ModeratorRoleId { get; set; }
}
