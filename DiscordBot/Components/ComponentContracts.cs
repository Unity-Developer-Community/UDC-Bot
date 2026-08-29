using System.Reflection;

namespace DiscordBot.Components;

public static class ComponentIds
{
    public const string Configuration = "configuration";
    public const string DiscordConnection = "discord-connection";
    public const string Logging = "logging";
    public const string Database = "database";
    public const string CommandHandling = "command-handling";
    public const string Registry = "component-registry";
    public const string UserActivity = "user-activity";
    public const string UserFun = "user-fun";
    public const string RoleAssignment = "role-assignment";
    public const string Moderation = "moderation";
    public const string IntroductionWatcher = "introduction-watcher";
    public const string Tickets = "tickets";
    public const string Feeds = "feeds";
    public const string Recruitment = "recruitment";
    public const string UnityHelp = "unity-help";
    public const string BirthdayAnnouncements = "birthday-announcements";
    public const string Reminders = "reminders";
    public const string KarmaReset = "karma-reset";
    public const string Updates = "updates";
    public const string Tips = "tips";
    public const string Casino = "casino";
    public const string Weather = "weather";
    public const string Airport = "airport";
}

public enum ComponentKind
{
    Core,
    ManagedEventService,
    ManagedWorker,
    FeatureModule,
    Dependency
}

[Flags]
public enum ComponentCapabilities
{
    None = 0,
    Health = 1,
    Toggleable = 2,
    Restartable = 4
}

public enum ComponentRuntimeState
{
    Starting,
    Running,
    Degraded,
    Faulted,
    Stopping,
    Stopped,
    Disabled,
    Misconfigured
}

public sealed record ComponentDescriptor(
    string Id,
    string DisplayName,
    string Description,
    ComponentKind Kind,
    bool EnabledByDefault,
    ComponentCapabilities Capabilities = ComponentCapabilities.Health,
    IReadOnlyList<string>? Dependencies = null,
    IReadOnlyList<string>? ConfigurationErrors = null,
    string? Version = null)
{
    public IReadOnlyList<string> Dependencies { get; init; } = Dependencies ?? [];
    public IReadOnlyList<string> ConfigurationErrors { get; init; } = ConfigurationErrors ?? [];
    public string Version { get; init; } = Version ?? ComponentBuildVersion.Value;
    public bool IsCore => Kind == ComponentKind.Core;
    public bool IsConfigured => ConfigurationErrors.Count == 0;
}

public sealed record ComponentHealthSnapshot(
    ComponentRuntimeState State,
    string Summary,
    DateTimeOffset ObservedAtUtc);

public sealed record ComponentSnapshot(
    ComponentDescriptor Descriptor,
    bool? OverrideEnabled,
    bool EffectiveDesiredState,
    ComponentRuntimeState RuntimeState,
    string Summary,
    DateTimeOffset? LastTransitionUtc,
    string? LastTransitionActor,
    string? LastTransitionReason,
    string? LastError);

public sealed record ComponentTransitionResult(
    bool Succeeded,
    string Message,
    ComponentSnapshot? Snapshot = null);

public interface IManagedBotService
{
    string ComponentId { get; }
    bool IsRunning { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public sealed record ManagedComponentRegistration(
    string ComponentId,
    Func<IServiceProvider, IManagedBotService> Resolve);

public interface IComponentHealthContributor
{
    string ComponentId { get; }
    Task<ComponentHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken);
}

public interface IComponentStateReader
{
    event EventHandler? StateChanged;

    IReadOnlyList<ComponentSnapshot> GetAll();
    ComponentSnapshot? Get(string componentId);
    bool IsEnabled(string componentId);
}

public interface IComponentRegistry : IComponentStateReader
{
    Task<ComponentSnapshot?> GetStatusAsync(string componentId, CancellationToken cancellationToken);
    Task StartAllAsync(CancellationToken cancellationToken);
    Task StopAllAsync(CancellationToken cancellationToken);
    Task<ComponentTransitionResult> EnableAsync(
        string componentId,
        string actor,
        string? reason,
        CancellationToken cancellationToken);
    Task<ComponentTransitionResult> DisableAsync(
        string componentId,
        string actor,
        string? reason,
        CancellationToken cancellationToken,
        bool persist = true);
    Task<ComponentTransitionResult> RestartAsync(
        string componentId,
        string actor,
        string? reason,
        CancellationToken cancellationToken);
    Task<ComponentTransitionResult> ResetAsync(
        string componentId,
        string actor,
        string? reason,
        CancellationToken cancellationToken);
}

internal static class ComponentBuildVersion
{
    public static readonly string Value =
        typeof(ComponentBuildVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";
}
