using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot
{
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
            var settings = services.GetRequiredService<Settings.Deserialized.Settings>();

            if (user.Roles.Any(x => x.Id == settings.ModeratorRoleId)) return Task.FromResult(PreconditionResult.FromSuccess());
            return Task.FromResult(PreconditionResult.FromError(user + " attempted to use a moderator command!"));
        }
    }
    
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class BotChannelOnlyAttribute : PreconditionAttribute
    {
        public override async Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var settings = services.GetRequiredService<Settings.Deserialized.Settings>();

            if (context.Channel.Id == settings.BotCommandsChannel.Id)
            {
                return await Task.FromResult(PreconditionResult.FromSuccess());
            }

            await context.Channel
                .SendMessageAsync($"This command can only be used in <#{settings.BotCommandsChannel.Id.ToString()}>.")
                .DeleteAfterSeconds(seconds: 8);
            await context.Message.DeleteAfterSeconds(seconds: 4);
            return await Task.FromResult(PreconditionResult.FromError(string.Empty));
        }
    }
}