using DiscordBot.Settings.Validation;
using Microsoft.Extensions.Configuration;

namespace DiscordBot.Components;

public interface IComponentCatalog
{
    IReadOnlyList<ComponentDescriptor> Components { get; }
}

public sealed class DefaultComponentCatalog : IComponentCatalog
{
    public DefaultComponentCatalog(
        FeatureConfigurationCatalog validation,
        IConfiguration configuration)
    {
        Components =
        [
            Core(ComponentIds.Configuration, "Configuration", "Validated configuration sources and options."),
            Core(ComponentIds.DiscordConnection, "Discord connection", "Discord gateway connection.", ComponentIds.Configuration),
            Core(ComponentIds.Logging, "Logging", "Console, file, and audit logging.", ComponentIds.Configuration),
            Core(ComponentIds.Database, "Database", "Persistent application database.", ComponentIds.Configuration, ComponentIds.Logging),
            Core(ComponentIds.CommandHandling, "Command handling", "Text and interaction command registration and dispatch.", ComponentIds.DiscordConnection, ComponentIds.Logging),
            Core(ComponentIds.Registry, "Component registry", "Component metadata, state, and controlled transitions.", ComponentIds.Configuration, ComponentIds.Logging),
            Feature(ComponentIds.UserActivity, "User activity", ComponentKind.ManagedEventService, true, "XP, thanks, welcome, and member activity.", ComponentIds.Updates, ComponentIds.DiscordConnection),
            Feature(ComponentIds.UserFun, "User fun commands", ComponentKind.FeatureModule, true, "Fun user commands and content.", ComponentIds.CommandHandling),
            Feature(ComponentIds.RoleAssignment, "Role assignment", ComponentKind.FeatureModule, true, "Self-service role assignment commands.", ComponentIds.CommandHandling),
            Feature(ComponentIds.Moderation, "Moderation", ComponentKind.ManagedEventService, true, "Moderation commands and audit event handling.", ComponentIds.CommandHandling, ComponentIds.Logging),
            Feature(ComponentIds.IntroductionWatcher, "Introduction watcher", ComponentKind.ManagedEventService, Enabled("Moderation:IntroductionWatcherEnabled"), "Duplicate introduction protection.", ComponentIds.DiscordConnection, ComponentIds.Logging),
            Feature(ComponentIds.Tickets, "Tickets", ComponentKind.FeatureModule, true, "Ticket commands.", ComponentIds.CommandHandling),
            Feature(ComponentIds.Feeds, "Feeds", ComponentKind.Dependency, true, "Unity release and blog feeds.", ComponentIds.DiscordConnection),
            Feature(ComponentIds.Recruitment, "Recruitment", ComponentKind.ManagedEventService, Enabled("Recruitment:Enabled"), "Recruitment forum moderation.", ComponentIds.DiscordConnection, ComponentIds.Logging, mutable: true),
            Feature(ComponentIds.UnityHelp, "Unity Help", ComponentKind.ManagedEventService, Enabled("UnityHelp:Enabled"), "Unity help forum lifecycle.", ComponentIds.DiscordConnection, ComponentIds.Logging),
            Feature(ComponentIds.BirthdayAnnouncements, "Birthday announcements", ComponentKind.ManagedWorker, Enabled("BirthdayAnnouncements:Enabled", defaultValue: true), "Periodic birthday announcements.", ComponentIds.DiscordConnection, ComponentIds.Logging, mutable: true),
            Feature(ComponentIds.Reminders, "Reminders", ComponentKind.ManagedWorker, true, "Persisted user reminders.", ComponentIds.DiscordConnection, ComponentIds.Logging, mutable: true),
            Feature(ComponentIds.KarmaReset, "Karma reset", ComponentKind.ManagedWorker, true, "Scheduled karma-period resets.", ComponentIds.Database, ComponentIds.Logging),
            Feature(ComponentIds.Updates, "Updates", ComponentKind.ManagedWorker, true, "Persistence and documentation update loops.", ComponentIds.Database),
            Feature(ComponentIds.Tips, "Tips", ComponentKind.FeatureModule, true, "Tip storage and commands.", ComponentIds.CommandHandling),
            Feature(ComponentIds.Casino, "Casino", ComponentKind.FeatureModule, Enabled("Casino:Enabled", defaultValue: true), "Casino commands and games.", ComponentIds.CommandHandling, ComponentIds.Database),
            Feature(ComponentIds.Weather, "Weather", ComponentKind.FeatureModule, true, "Weather lookups.", ComponentIds.CommandHandling),
            Feature(ComponentIds.Airport, "Airport", ComponentKind.FeatureModule, true, "Airport and flight lookups.", ComponentIds.CommandHandling)
        ];

        return;

        ComponentDescriptor Core(string id, string name, string description, params string[] dependencies) =>
            new(id, name, description, ComponentKind.Core, true, ComponentCapabilities.Health, dependencies);

        bool Enabled(string key, bool defaultValue = false) =>
            bool.TryParse(configuration[key], out var enabled) ? enabled : defaultValue;

        ComponentDescriptor Feature(
            string id,
            string name,
            ComponentKind kind,
            bool enabled,
            string description,
            string dependency,
            string? secondDependency = null,
            bool mutable = false)
        {
            var dependencies = secondDependency is null ? [dependency] : new[] { dependency, secondDependency };
            var status = validation.Get(id);
            var capabilities = ComponentCapabilities.Health;
            if (mutable)
                capabilities |= ComponentCapabilities.Toggleable | ComponentCapabilities.Restartable;
            return new ComponentDescriptor(
                id,
                name,
                description,
                kind,
                enabled,
                capabilities,
                dependencies,
                status.Errors);
        }
    }

    public IReadOnlyList<ComponentDescriptor> Components { get; }
}
