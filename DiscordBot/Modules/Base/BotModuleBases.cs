using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Components;

namespace DiscordBot.Modules.Base;

public abstract class BotCommandModuleBase : ModuleBase
{
    public IComponentStateReader ComponentState { get; set; } = null!;
}

public abstract class BotInteractionModuleBase : InteractionModuleBase<SocketInteractionContext>
{
    public IComponentStateReader ComponentState { get; set; } = null!;
}
