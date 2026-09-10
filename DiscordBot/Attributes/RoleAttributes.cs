using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAdminAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        if (context.Message.Author is not SocketGuildUser user)
            return Task.FromResult(PreconditionResult.FromError("This command can only be used in a server."));

        if (user.Roles.Any(x => x.Permissions.Administrator)) return Task.FromResult(PreconditionResult.FromSuccess());
        return Task.FromResult(PreconditionResult.FromError(user + " attempted to use admin only command!"));
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireModeratorAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        if (context.Message.Author is not SocketGuildUser user)
            return Task.FromResult(PreconditionResult.FromError("This command can only be used in a server."));

        var settings = services.GetRequiredService<BotSettings>();

        if (user.Roles.Any(x => x.Id == settings.Roles.Moderator)) return Task.FromResult(PreconditionResult.FromSuccess());
        return Task.FromResult(PreconditionResult.FromError(user + " attempted to use a moderator command!"));
    }
}