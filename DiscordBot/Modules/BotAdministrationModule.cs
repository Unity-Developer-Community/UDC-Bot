using System.Text;
using Discord.Commands;
using DiscordBot.Attributes;
using DiscordBot.Components;
using DiscordBot.Modules.Base;

namespace DiscordBot.Modules;

[Group("bot")]
[RequireAdmin]
public sealed class BotAdministrationModule : BotCommandModuleBase
{
    public IComponentRegistry Registry { get; set; } = null!;

    [Command("components")]
    [Summary("Lists bot components and their current state.")]
    public Task Components() => ReplyChunksAsync(FormatComponents(Registry.GetAll()));

    [Command("status")]
    [Summary("Shows safe status for one component.")]
    public async Task Status(string componentId)
    {
        var snapshot = await Registry.GetStatusAsync(componentId, CancellationToken.None);
        await ReplyAsync(snapshot is null ? $"Unknown component `{componentId}`." : FormatStatus(snapshot));
    }

    [Command("enable")]
    [Summary("Enables a supported component. Syntax: !bot enable <component> [reason]")]
    public Task Enable(string componentId, [Remainder] string reason = "") =>
        ReplyTransitionAsync(Registry.EnableAsync(componentId, Actor, reason, CancellationToken.None));

    [Command("disable")]
    [Summary("Disables a supported component. Syntax: !bot disable <component> [reason]")]
    public Task Disable(string componentId, [Remainder] string reason = "") =>
        ReplyTransitionAsync(Registry.DisableAsync(componentId, Actor, reason, CancellationToken.None));

    [Command("restart")]
    [Summary("Restarts a supported component. Syntax: !bot restart <component> [reason]")]
    public Task Restart(string componentId, [Remainder] string reason = "") =>
        ReplyTransitionAsync(Registry.RestartAsync(componentId, Actor, reason, CancellationToken.None));

    [Command("reset")]
    [Summary("Clears a component override. Syntax: !bot reset <component> [reason]")]
    public Task Reset(string componentId, [Remainder] string reason = "") =>
        ReplyTransitionAsync(Registry.ResetAsync(componentId, Actor, reason, CancellationToken.None));

    private string Actor => $"{Context.User.Id}:{Context.User.Username}";

    private async Task ReplyTransitionAsync(Task<ComponentTransitionResult> transition)
    {
        var result = await transition;
        await ReplyAsync(result.Message);
    }

    private async Task ReplyChunksAsync(string content)
    {
        foreach (var chunk in Split(content, 1900))
            await ReplyAsync(chunk);
    }

    internal static string FormatComponents(IReadOnlyList<ComponentSnapshot> components)
    {
        var builder = new StringBuilder("**Bot components**\n");
        foreach (var component in components)
        {
            var overrideText = component.OverrideEnabled.HasValue
                ? component.OverrideEnabled.Value ? "enabled" : "disabled"
                : "none";
            builder.Append('`').Append(component.Descriptor.Id).Append("` — ")
                .Append(component.RuntimeState).Append("; default=")
                .Append(component.Descriptor.EnabledByDefault ? "enabled" : "disabled")
                .Append("; override=").Append(overrideText).Append('\n');
        }
        return builder.ToString();
    }

    internal static string FormatStatus(ComponentSnapshot component)
    {
        var capabilities = component.Descriptor.Capabilities == ComponentCapabilities.None
            ? "none"
            : component.Descriptor.Capabilities.ToString();
        var dependencies = component.Descriptor.Dependencies.Count == 0
            ? "none"
            : string.Join(", ", component.Descriptor.Dependencies);
        var changed = component.LastTransitionUtc.HasValue
            ? $"{component.LastTransitionUtc:u} by {component.LastTransitionActor ?? "system"}"
            : "not recorded";
        var reason = string.IsNullOrWhiteSpace(component.LastTransitionReason)
            ? string.Empty
            : $"; reason: {component.LastTransitionReason}";
        var configurationErrors = component.Descriptor.ConfigurationErrors.Count == 0
            ? string.Empty
            : $"\nConfiguration: {string.Join(" ", component.Descriptor.ConfigurationErrors)}";
        return $"**{component.Descriptor.DisplayName}** (`{component.Descriptor.Id}`)\n" +
               $"Kind: {component.Descriptor.Kind}; version: `{component.Descriptor.Version}`\n" +
               $"Default: {component.Descriptor.EnabledByDefault}; override: {component.OverrideEnabled?.ToString() ?? "none"}; effective: {component.EffectiveDesiredState}\n" +
               $"Runtime: {component.RuntimeState} — {component.Summary}\n" +
               $"Capabilities: {capabilities}; dependencies: {dependencies}\n" +
               $"Last transition: {changed}{reason}" +
               configurationErrors +
               (component.LastError is null ? string.Empty : $"\nLast error: {component.LastError}");
    }

    private static IEnumerable<string> Split(string value, int maximumLength)
    {
        for (var offset = 0; offset < value.Length; offset += maximumLength)
            yield return value.Substring(offset, Math.Min(maximumLength, value.Length - offset));
    }
}
