using Discord.Commands;
using Discord.Interactions;
using DiscordBot.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireComponentEnabledAttribute(string componentId) : Discord.Commands.PreconditionAttribute
{
    public string ComponentId { get; } = componentId;

    public override Task<Discord.Commands.PreconditionResult> CheckPermissionsAsync(
        ICommandContext context,
        Discord.Commands.CommandInfo command,
        IServiceProvider services)
    {
        var state = services.GetRequiredService<IComponentStateReader>();
        return Task.FromResult(state.IsEnabled(componentId)
            ? Discord.Commands.PreconditionResult.FromSuccess()
            : Discord.Commands.PreconditionResult.FromError($"Component '{componentId}' is currently unavailable."));
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireInteractionComponentEnabledAttribute(string componentId)
    : Discord.Interactions.PreconditionAttribute
{
    public string ComponentId { get; } = componentId;

    public override Task<Discord.Interactions.PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context,
        Discord.Interactions.ICommandInfo commandInfo,
        IServiceProvider services)
    {
        var state = services.GetRequiredService<IComponentStateReader>();
        return Task.FromResult(state.IsEnabled(componentId)
            ? Discord.Interactions.PreconditionResult.FromSuccess()
            : Discord.Interactions.PreconditionResult.FromError($"Component '{componentId}' is currently unavailable."));
    }
}
