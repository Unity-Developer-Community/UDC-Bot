using Discord.Commands;

namespace DiscordBot.Attributes;

/// <summary>
/// Simple attribute, if the command is used by a bot, it escapes early and doesn't run the command.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class IgnoreBotsAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        if (context.Message.Author.IsBot)
        {
            return Task.FromResult(PreconditionResult.FromError(string.Empty));
        }

        return Task.FromResult(PreconditionResult.FromSuccess());
    }
}