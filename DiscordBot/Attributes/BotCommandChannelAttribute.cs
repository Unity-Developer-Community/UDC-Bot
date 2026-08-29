using Discord.Commands;
using DiscordBot.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class BotCommandChannelAttribute : PreconditionAttribute
{
    public override async Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        var policy = services.GetRequiredService<ICommandChannelPolicy>();

        if (policy.IsCommandChannel(context.Channel.Id))
        {
            return await Task.FromResult(PreconditionResult.FromSuccess());
        }

        Task task = context.Message.DeleteAfterSeconds(seconds: 10);
        return await Task.FromResult(PreconditionResult.FromError($"This command can only be used in {policy.CommandChannelMention}."));
    }
}
