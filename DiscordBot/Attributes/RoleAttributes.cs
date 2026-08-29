using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAdminAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        var user = (SocketGuildUser)context.Message.Author;

        if (user.Roles.Any(x => x.Permissions.Administrator)) return Task.FromResult(PreconditionResult.FromSuccess());
        return Task.FromResult(PreconditionResult.FromError(user + " attempted to use admin only command!"));
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireModeratorAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        var user = (SocketGuildUser)context.Message.Author;
        var authorization = services.GetRequiredService<IBotAuthorizationPolicy>();

        if (authorization.IsModerator(user)) return Task.FromResult(PreconditionResult.FromSuccess());
        return Task.FromResult(PreconditionResult.FromError(user + " attempted to use a moderator command!"));
    }
}
