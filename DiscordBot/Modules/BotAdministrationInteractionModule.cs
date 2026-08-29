using Discord;
using Discord.Interactions;
using DiscordBot.Components;
using DiscordBot.Modules.Base;

namespace DiscordBot.Modules;

[Group("bot", "Inspect and control bot components.")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[RequireUserPermission(GuildPermission.Administrator)]
public sealed class BotAdministrationInteractionModule : BotInteractionModuleBase
{
    public IComponentRegistry Registry { get; set; } = null!;

    [SlashCommand("components", "Lists bot components and their current state.")]
    public Task Components() => RespondAsync(
        BotAdministrationModule.FormatComponents(Registry.GetAll()),
        ephemeral: true);

    [SlashCommand("status", "Shows safe status for one component.")]
    public async Task Status(string component)
    {
        var snapshot = await Registry.GetStatusAsync(component, CancellationToken.None);
        await RespondAsync(
            snapshot is null ? $"Unknown component `{component}`." : BotAdministrationModule.FormatStatus(snapshot),
            ephemeral: true);
    }

    [SlashCommand("enable", "Enables a supported component.")]
    public Task Enable(string component, string reason = "", bool confirm = false) =>
        MutateAsync(confirm, "enable", () => Registry.EnableAsync(component, Actor, reason, CancellationToken.None));

    [SlashCommand("disable", "Disables a supported component.")]
    public Task Disable(string component, string reason = "", bool confirm = false) =>
        MutateAsync(confirm, "disable", () => Registry.DisableAsync(component, Actor, reason, CancellationToken.None));

    [SlashCommand("restart", "Restarts a supported component.")]
    public Task Restart(string component, string reason = "", bool confirm = false) =>
        MutateAsync(confirm, "restart", () => Registry.RestartAsync(component, Actor, reason, CancellationToken.None));

    [SlashCommand("reset", "Clears a component override.")]
    public Task Reset(string component, string reason = "", bool confirm = false) =>
        MutateAsync(confirm, "reset", () => Registry.ResetAsync(component, Actor, reason, CancellationToken.None));

    private string Actor => $"{Context.User.Id}:{Context.User.Username}";

    private async Task MutateAsync(
        bool confirmed,
        string action,
        Func<Task<ComponentTransitionResult>> transition)
    {
        if (!confirmed)
        {
            await RespondAsync($"No change made. Run the `{action}` command again with `confirm: True`.", ephemeral: true);
            return;
        }

        var result = await transition();
        await RespondAsync(result.Message, ephemeral: true);
    }
}
