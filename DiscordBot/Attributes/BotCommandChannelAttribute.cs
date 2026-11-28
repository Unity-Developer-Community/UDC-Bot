using Discord.Commands;
using DiscordBot.Services.Moderation;
using DiscordBot.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class BotCommandChannelAttribute : PreconditionAttribute
{
    public override async Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        var settings = services.GetRequiredService<BotSettings>();

        if (context.Channel.Id == settings.Channels.BotCommands.Id)
        {
            return await Task.FromResult(PreconditionResult.FromSuccess());
        }

        context.Message.DeleteCommandMessageAsync(services.GetRequiredService<BotDeletionRegistry>(), TimeSpan.FromSeconds(10))
            .SafeFireAndForget(nameof(BotCommandChannelAttribute));
        return await Task.FromResult(PreconditionResult.FromError($"This command can only be used in <#{settings.Channels.BotCommands.Id.ToString()}>."));
    }
}