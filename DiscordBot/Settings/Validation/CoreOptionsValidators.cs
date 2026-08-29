using System.IO;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Settings.Validation;

public sealed class DiscordConnectionOptionsValidator : IValidateOptions<DiscordConnectionOptions>
{
    public ValidateOptionsResult Validate(string? name, DiscordConnectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Token) ||
            options.Token.Contains("Y O U R", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "DiscordConnection:Token is required. Set UDCBOT_DiscordConnection__Token or configure the legacy Settings.json value.");
        }

        return ValidateOptionsResult.Success;
    }
}

public sealed class DiscordGuildOptionsValidator : IValidateOptions<DiscordGuildOptions>
{
    public ValidateOptionsResult Validate(string? name, DiscordGuildOptions options) =>
        options.GuildId == 0
            ? ValidateOptionsResult.Fail("DiscordGuild:GuildId must be a non-zero Discord guild ID.")
            : ValidateOptionsResult.Success;
}

public sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        var failures = new List<string>();
        ValidatePath(options.ServerRootPath, "Storage:ServerRootPath", failures);
        ValidatePath(options.AssetsRootPath, "Storage:AssetsRootPath", failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidatePath(string path, string key, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            failures.Add($"{key} is required.");
            return;
        }

        try
        {
            _ = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            failures.Add($"{key} is not a valid path.");
        }
    }
}

public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options) =>
        string.IsNullOrWhiteSpace(options.ConnectionString)
            ? ValidateOptionsResult.Fail(
                "Database:ConnectionString is required. Set UDCBOT_Database__ConnectionString or configure the legacy Settings.json value.")
            : ValidateOptionsResult.Success;
}

public sealed class CommandOptionsValidator : IValidateOptions<CommandOptions>
{
    public ValidateOptionsResult Validate(string? name, CommandOptions options)
    {
        var failures = new List<string>();
        if (options.Prefix == default || char.IsWhiteSpace(options.Prefix))
            failures.Add("Commands:Prefix must be one visible character.");
        if (options.BotCommandsChannelId == 0)
            failures.Add("Commands:BotCommandsChannelId must be a non-zero Discord channel ID.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class AuthorizationOptionsValidator : IValidateOptions<AuthorizationOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthorizationOptions options) =>
        options.ModeratorRoleId == 0
            ? ValidateOptionsResult.Fail("Authorization:ModeratorRoleId must be a non-zero Discord role ID.")
            : ValidateOptionsResult.Success;
}
