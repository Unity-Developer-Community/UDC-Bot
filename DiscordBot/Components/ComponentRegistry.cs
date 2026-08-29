using System.Collections.Concurrent;
using DiscordBot.Services;

namespace DiscordBot.Components;

public sealed class ComponentRegistry : IComponentRegistry
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(2);

    private readonly IReadOnlyDictionary<string, ComponentDescriptor> _descriptors;
    private readonly IServiceProvider _services;
    private readonly IReadOnlyDictionary<string, ManagedComponentRegistration> _managedRegistrations;
    private readonly ConcurrentDictionary<string, IManagedBotService> _managedServices = new(StringComparer.OrdinalIgnoreCase);
    private readonly IComponentOverrideStore _overrideStore;
    private readonly ILoggingService _logging;
    private readonly ConcurrentDictionary<string, MutableComponentState> _states;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _transitionLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ComponentOverride> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public ComponentRegistry(
        IComponentCatalog catalog,
        IServiceProvider services,
        IEnumerable<ManagedComponentRegistration> managedServices,
        IComponentOverrideStore overrideStore,
        ILoggingService logging)
    {
        var duplicates = catalog.Components
            .GroupBy(component => component.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"Duplicate component IDs: {string.Join(", ", duplicates)}.");

        _descriptors = catalog.Components.ToDictionary(
            component => component.Id,
            StringComparer.OrdinalIgnoreCase);
        _services = services;
        _managedRegistrations = managedServices.ToDictionary(
            service => service.ComponentId,
            StringComparer.OrdinalIgnoreCase);
        _overrideStore = overrideStore;
        _logging = logging;
        _states = new ConcurrentDictionary<string, MutableComponentState>(
            catalog.Components.ToDictionary(
                component => component.Id,
                component => new MutableComponentState
                {
                    RuntimeState = InitialState(component),
                    Summary = InitialSummary(component)
                },
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var undeclaredServices = _managedRegistrations.Keys.Where(id => !_descriptors.ContainsKey(id)).ToArray();
        if (undeclaredServices.Length > 0)
            throw new InvalidOperationException($"Managed services lack component descriptors: {string.Join(", ", undeclaredServices)}.");
    }

    public event EventHandler? StateChanged;

    public IReadOnlyList<ComponentSnapshot> GetAll() =>
        _descriptors.Values
            .OrderBy(component => component.Kind)
            .ThenBy(component => component.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(CreateSnapshot)
            .ToArray();

    public ComponentSnapshot? Get(string componentId) =>
        _descriptors.ContainsKey(componentId) ? CreateSnapshot(_descriptors[componentId]) : null;

    public bool IsEnabled(string componentId)
    {
        var snapshot = Get(componentId);
        return snapshot is not null &&
               snapshot.EffectiveDesiredState &&
               snapshot.RuntimeState is ComponentRuntimeState.Running or ComponentRuntimeState.Degraded;
    }

    public async Task<ComponentSnapshot?> GetStatusAsync(
        string componentId,
        CancellationToken cancellationToken)
    {
        if (!_descriptors.TryGetValue(componentId, out var descriptor))
            return null;
        IManagedBotService service;
        try
        {
            if (!TryGetManagedService(componentId, out service))
                return CreateSnapshot(descriptor);
        }
        catch (Exception exception)
        {
            var safeError = $"Health source unavailable ({exception.GetType().Name}).";
            UpdateState(descriptor.Id, ComponentRuntimeState.Faulted, safeError, null, null, safeError);
            return CreateSnapshot(descriptor);
        }

        var health = await GetHealthAsync(descriptor, service, cancellationToken);
        UpdateHealth(descriptor.Id, health.State, health.Summary);
        return CreateSnapshot(descriptor);
    }

    public async Task StartAllAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        foreach (var descriptor in TopologicalOrder())
        {
            if (!EffectiveDesiredState(descriptor) || !descriptor.IsConfigured)
                continue;

            var blockedBy = descriptor.Dependencies.Where(dependency => !IsEnabled(dependency)).ToArray();
            if (blockedBy.Length > 0)
            {
                UpdateState(
                    descriptor.Id,
                    ComponentRuntimeState.Stopped,
                    $"Blocked by: {string.Join(", ", blockedBy)}.",
                    "startup",
                    null,
                    null);
                continue;
            }

            try
            {
                if (TryGetManagedService(descriptor.Id, out var service))
                {
                    var result = await StartServiceAsync(descriptor, service, "startup", null, cancellationToken);
                    if (!result.Succeeded && descriptor.IsCore)
                        throw new InvalidOperationException(result.Message);
                }
                else
                {
                    UpdateState(descriptor.Id, ComponentRuntimeState.Running, "Available.", "startup", null, null);
                }
            }
            catch (Exception exception) when (!descriptor.IsCore)
            {
                var safeError = $"Startup failed ({exception.GetType().Name}).";
                UpdateState(descriptor.Id, ComponentRuntimeState.Faulted, safeError, "startup", null, safeError);
            }
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        foreach (var descriptor in TopologicalOrder().Reverse())
        {
            if (!_managedServices.TryGetValue(descriptor.Id, out var service) || !service.IsRunning)
                continue;

            await StopServiceAsync(descriptor, service, "shutdown", null, cancellationToken);
        }
    }

    public Task<ComponentTransitionResult> EnableAsync(
        string componentId,
        string actor,
        string? reason,
        CancellationToken cancellationToken) =>
        TransitionAsync(componentId, ComponentTransition.Enable, actor, reason, cancellationToken, persist: true);

    public Task<ComponentTransitionResult> DisableAsync(
        string componentId,
        string actor,
        string? reason,
        CancellationToken cancellationToken,
        bool persist = true) =>
        TransitionAsync(componentId, ComponentTransition.Disable, actor, reason, cancellationToken, persist);

    public Task<ComponentTransitionResult> RestartAsync(
        string componentId,
        string actor,
        string? reason,
        CancellationToken cancellationToken) =>
        TransitionAsync(componentId, ComponentTransition.Restart, actor, reason, cancellationToken, persist: false);

    public Task<ComponentTransitionResult> ResetAsync(
        string componentId,
        string actor,
        string? reason,
        CancellationToken cancellationToken) =>
        TransitionAsync(componentId, ComponentTransition.Reset, actor, reason, cancellationToken, persist: true);

    private async Task<ComponentTransitionResult> TransitionAsync(
        string componentId,
        ComponentTransition transition,
        string actor,
        string? reason,
        CancellationToken cancellationToken,
        bool persist)
    {
        await EnsureInitializedAsync(cancellationToken);
        reason = NormalizeReason(reason);
        if (!_descriptors.TryGetValue(componentId, out var descriptor))
            return new ComponentTransitionResult(false, $"Unknown component '{componentId}'.");

        var transitionLock = _transitionLocks.GetOrAdd(descriptor.Id, _ => new SemaphoreSlim(1, 1));
        await transitionLock.WaitAsync(cancellationToken);
        try
        {
            await AuditAsync("attempted", descriptor.Id, transition, actor, reason);
            var refusal = GetRefusal(descriptor, transition);
            if (refusal is not null)
                return await RefusedAsync(descriptor, transition, actor, reason, refusal);

            await AuditAsync("accepted", descriptor.Id, transition, actor, reason);

            ComponentTransitionResult result;
            try
            {
                switch (transition)
                {
                    case ComponentTransition.Enable:
                        result = await EnableCoreAsync(descriptor, actor, reason, cancellationToken, persist);
                        break;
                    case ComponentTransition.Disable:
                        result = await DisableCoreAsync(descriptor, actor, reason, cancellationToken, persist);
                        break;
                    case ComponentTransition.Restart:
                        result = await RestartCoreAsync(descriptor, actor, reason, cancellationToken);
                        break;
                    case ComponentTransition.Reset:
                        result = await ResetCoreAsync(descriptor, actor, reason, cancellationToken);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(transition));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var safeError = $"Transition failed ({exception.GetType().Name}).";
                result = new ComponentTransitionResult(
                    false,
                    $"Component '{descriptor.Id}' could not complete the requested change.",
                    CreateSnapshot(descriptor));
                await AuditAsync("failed", descriptor.Id, transition, actor, reason, safeError);
                return result;
            }

            await AuditAsync(result.Succeeded ? "completed" : "failed", descriptor.Id, transition, actor, reason, result.Message);
            return result;
        }
        finally
        {
            transitionLock.Release();
        }
    }

    private string? GetRefusal(ComponentDescriptor descriptor, ComponentTransition transition)
    {
        if (descriptor.IsCore)
            return "Core components cannot be changed at runtime.";
        if ((transition is ComponentTransition.Enable or ComponentTransition.Disable or ComponentTransition.Reset) &&
            !descriptor.Capabilities.HasFlag(ComponentCapabilities.Toggleable))
            return "This component has not passed the runtime toggle lifecycle checks.";
        if (transition == ComponentTransition.Restart &&
            !descriptor.Capabilities.HasFlag(ComponentCapabilities.Restartable))
            return "This component has not passed the restart lifecycle checks.";
        if (transition == ComponentTransition.Enable && !descriptor.IsConfigured)
            return $"The component is misconfigured: {string.Join(" ", descriptor.ConfigurationErrors)}";
        return null;
    }

    private async Task<ComponentTransitionResult> EnableCoreAsync(
        ComponentDescriptor descriptor,
        string actor,
        string? reason,
        CancellationToken cancellationToken,
        bool persist)
    {
        var blockers = descriptor.Dependencies.Where(dependency => !IsEnabled(dependency)).ToArray();
        if (blockers.Length > 0)
            return new ComponentTransitionResult(false, $"Blocked by: {string.Join(", ", blockers)}.", CreateSnapshot(descriptor));

        if (persist)
            await SetOverrideAsync(descriptor.Id, true, actor, reason, cancellationToken);

        if (TryGetManagedService(descriptor.Id, out var service))
        {
            var start = await StartServiceAsync(descriptor, service, actor, reason, cancellationToken);
            if (!start.Succeeded)
                return start;
        }
        else
        {
            UpdateState(descriptor.Id, ComponentRuntimeState.Running, "Enabled.", actor, reason, null);
        }

        return new ComponentTransitionResult(true, $"Component '{descriptor.Id}' is enabled.", CreateSnapshot(descriptor));
    }

    private async Task<ComponentTransitionResult> DisableCoreAsync(
        ComponentDescriptor descriptor,
        string actor,
        string? reason,
        CancellationToken cancellationToken,
        bool persist)
    {
        var blockers = _descriptors.Values
            .Where(candidate => candidate.Dependencies.Contains(descriptor.Id, StringComparer.OrdinalIgnoreCase))
            .Where(candidate => IsEnabled(candidate.Id))
            .Select(candidate => candidate.Id)
            .ToArray();
        if (blockers.Length > 0)
            return new ComponentTransitionResult(false, $"Running dependants block disable: {string.Join(", ", blockers)}.", CreateSnapshot(descriptor));

        if (persist)
            await SetOverrideAsync(descriptor.Id, false, actor, reason, cancellationToken);

        if (TryGetManagedService(descriptor.Id, out var service) && service.IsRunning)
        {
            var stop = await StopServiceAsync(descriptor, service, actor, reason, cancellationToken);
            if (!stop.Succeeded)
                return stop;
        }
        else
        {
            UpdateState(descriptor.Id, ComponentRuntimeState.Disabled, "Disabled.", actor, reason, null);
        }

        return new ComponentTransitionResult(true, $"Component '{descriptor.Id}' is disabled.", CreateSnapshot(descriptor));
    }

    private async Task<ComponentTransitionResult> RestartCoreAsync(
        ComponentDescriptor descriptor,
        string actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (!TryGetManagedService(descriptor.Id, out var service))
            return new ComponentTransitionResult(false, "The component has no managed lifecycle.", CreateSnapshot(descriptor));

        var stopped = await StopServiceAsync(descriptor, service, actor, reason, cancellationToken);
        return stopped.Succeeded
            ? await StartServiceAsync(descriptor, service, actor, reason, cancellationToken)
            : stopped;
    }

    private async Task<ComponentTransitionResult> ResetCoreAsync(
        ComponentDescriptor descriptor,
        string actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        await RemoveOverrideAsync(descriptor.Id, cancellationToken);

        if (descriptor.EnabledByDefault)
            return await EnableCoreAsync(descriptor, actor, reason, cancellationToken, persist: false);
        return await DisableCoreAsync(descriptor, actor, reason, cancellationToken, persist: false);
    }

    private async Task<ComponentTransitionResult> StartServiceAsync(
        ComponentDescriptor descriptor,
        IManagedBotService service,
        string actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (service.IsRunning)
            return new ComponentTransitionResult(true, $"Component '{descriptor.Id}' is already running.", CreateSnapshot(descriptor));

        UpdateState(descriptor.Id, ComponentRuntimeState.Starting, "Starting.", actor, reason, null);
        try
        {
            await service.StartAsync(cancellationToken);
            var health = await GetHealthAsync(descriptor, service, cancellationToken);
            UpdateState(descriptor.Id, health.State, health.Summary, actor, reason, null);
            return new ComponentTransitionResult(true, $"Component '{descriptor.Id}' started.", CreateSnapshot(descriptor));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            UpdateState(descriptor.Id, ComponentRuntimeState.Stopped, "Start cancelled.", actor, reason, null);
            throw;
        }
        catch (Exception exception)
        {
            var safeError = $"Start failed ({exception.GetType().Name}).";
            UpdateState(descriptor.Id, ComponentRuntimeState.Faulted, safeError, actor, reason, safeError);
            return new ComponentTransitionResult(false, $"Component '{descriptor.Id}' failed to start.", CreateSnapshot(descriptor));
        }
    }

    private async Task<ComponentTransitionResult> StopServiceAsync(
        ComponentDescriptor descriptor,
        IManagedBotService service,
        string actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        UpdateState(descriptor.Id, ComponentRuntimeState.Stopping, "Stopping.", actor, reason, null);
        try
        {
            await service.StopAsync(cancellationToken);
            UpdateState(descriptor.Id, ComponentRuntimeState.Stopped, "Stopped.", actor, reason, null);
            return new ComponentTransitionResult(true, $"Component '{descriptor.Id}' stopped.", CreateSnapshot(descriptor));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var safeError = $"Stop failed ({exception.GetType().Name}).";
            UpdateState(descriptor.Id, ComponentRuntimeState.Faulted, safeError, actor, reason, safeError);
            return new ComponentTransitionResult(false, $"Component '{descriptor.Id}' failed to stop.", CreateSnapshot(descriptor));
        }
    }

    private async Task<ComponentHealthSnapshot> GetHealthAsync(
        ComponentDescriptor descriptor,
        IManagedBotService service,
        CancellationToken cancellationToken)
    {
        if (service is not IComponentHealthContributor contributor)
        {
            return new ComponentHealthSnapshot(
                service.IsRunning ? ComponentRuntimeState.Running : ComponentRuntimeState.Stopped,
                service.IsRunning ? "Running." : "Stopped.",
                DateTimeOffset.UtcNow);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HealthTimeout);
        try
        {
            return await contributor.GetHealthAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ComponentHealthSnapshot(ComponentRuntimeState.Degraded, "Health check timed out.", DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            return new ComponentHealthSnapshot(
                ComponentRuntimeState.Degraded,
                $"Health check failed ({exception.GetType().Name}).",
                DateTimeOffset.UtcNow);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            foreach (var entry in await _overrideStore.LoadAsync(cancellationToken))
            {
                if (_descriptors.TryGetValue(entry.Key, out var descriptor) &&
                    !descriptor.IsCore &&
                    descriptor.Capabilities.HasFlag(ComponentCapabilities.Toggleable))
                {
                    _overrides[descriptor.Id] = entry.Value;
                }
            }

            foreach (var descriptor in _descriptors.Values)
            {
                if (!descriptor.IsConfigured)
                    UpdateState(descriptor.Id, ComponentRuntimeState.Misconfigured, "Configuration is invalid.", null, null, null);
                else if (!EffectiveDesiredState(descriptor))
                    UpdateState(descriptor.Id, ComponentRuntimeState.Disabled, "Disabled by configuration or override.", null, null, null);
            }

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private bool TryGetManagedService(string componentId, out IManagedBotService service)
    {
        if (_managedServices.TryGetValue(componentId, out service!))
            return true;
        if (!_managedRegistrations.TryGetValue(componentId, out var registration))
            return false;

        service = _managedServices.GetOrAdd(componentId, _ => registration.Resolve(_services));
        if (!service.ComponentId.Equals(componentId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Managed service '{service.GetType().Name}' returned component ID '{service.ComponentId}' instead of '{componentId}'.");
        return true;
    }

    private async Task SetOverrideAsync(
        string componentId,
        bool enabled,
        string actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        var hadPrevious = _overrides.TryGetValue(componentId, out var previous);
        _overrides[componentId] = new ComponentOverride(enabled, actor, DateTimeOffset.UtcNow, reason);
        try
        {
            await _overrideStore.SaveAsync(_overrides, cancellationToken);
        }
        catch
        {
            if (hadPrevious)
                _overrides[componentId] = previous!;
            else
                _overrides.Remove(componentId);
            throw;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RemoveOverrideAsync(string componentId, CancellationToken cancellationToken)
    {
        if (!_overrides.Remove(componentId, out var previous))
            return;

        try
        {
            await _overrideStore.SaveAsync(_overrides, cancellationToken);
        }
        catch
        {
            _overrides[componentId] = previous;
            throw;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<ComponentTransitionResult> RefusedAsync(
        ComponentDescriptor descriptor,
        ComponentTransition transition,
        string actor,
        string? reason,
        string message)
    {
        await AuditAsync("refused", descriptor.Id, transition, actor, reason, message);
        return new ComponentTransitionResult(false, message, CreateSnapshot(descriptor));
    }

    private Task AuditAsync(
        string outcome,
        string componentId,
        ComponentTransition transition,
        string actor,
        string? reason,
        string? detail = null)
    {
        var reasonText = string.IsNullOrWhiteSpace(reason) ? "none" : reason.Trim();
        var detailText = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" Detail: {detail}";
        return _logging.Log(
            LogBehaviour.Console | LogBehaviour.File,
            $"Component transition {outcome}: component={componentId}, action={transition.ToString().ToLowerInvariant()}, actor={actor}, reason={reasonText}.{detailText}",
            outcome == "failed" ? ExtendedLogSeverity.Warning : ExtendedLogSeverity.Info);
    }

    private ComponentSnapshot CreateSnapshot(ComponentDescriptor descriptor)
    {
        _states.TryGetValue(descriptor.Id, out var state);
        _overrides.TryGetValue(descriptor.Id, out var componentOverride);
        var usePersistedTransition = componentOverride is not null && state?.LastTransitionActor is null;
        return new ComponentSnapshot(
            descriptor,
            componentOverride?.Enabled,
            EffectiveDesiredState(descriptor),
            state?.RuntimeState ?? InitialState(descriptor),
            state?.Summary ?? InitialSummary(descriptor),
            usePersistedTransition ? componentOverride!.ChangedAtUtc : state?.LastTransitionUtc,
            state?.LastTransitionActor ?? componentOverride?.Actor,
            state?.LastTransitionReason ?? componentOverride?.Reason,
            state?.LastError);
    }

    private bool EffectiveDesiredState(ComponentDescriptor descriptor) =>
        _overrides.TryGetValue(descriptor.Id, out var componentOverride)
            ? componentOverride.Enabled
            : descriptor.EnabledByDefault;

    private IReadOnlyList<ComponentDescriptor> TopologicalOrder()
    {
        var result = new List<ComponentDescriptor>(_descriptors.Count);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in _descriptors.Values)
            Visit(descriptor);
        return result;

        void Visit(ComponentDescriptor descriptor)
        {
            if (visited.Contains(descriptor.Id))
                return;
            if (!visiting.Add(descriptor.Id))
                throw new InvalidOperationException($"Component dependency cycle includes '{descriptor.Id}'.");

            foreach (var dependency in descriptor.Dependencies)
            {
                if (!_descriptors.TryGetValue(dependency, out var dependencyDescriptor))
                    throw new InvalidOperationException($"Component '{descriptor.Id}' depends on unknown component '{dependency}'.");
                Visit(dependencyDescriptor);
            }

            visiting.Remove(descriptor.Id);
            visited.Add(descriptor.Id);
            result.Add(descriptor);
        }
    }

    private void UpdateState(
        string componentId,
        ComponentRuntimeState runtimeState,
        string summary,
        string? actor,
        string? reason,
        string? lastError)
    {
        _states[componentId] = new MutableComponentState
        {
            RuntimeState = runtimeState,
            Summary = summary,
            LastTransitionUtc = DateTimeOffset.UtcNow,
            LastTransitionActor = actor,
            LastTransitionReason = reason,
            LastError = lastError
        };
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateHealth(
        string componentId,
        ComponentRuntimeState runtimeState,
        string summary)
    {
        _states.TryGetValue(componentId, out var current);
        _states[componentId] = new MutableComponentState
        {
            RuntimeState = runtimeState,
            Summary = summary,
            LastTransitionUtc = current?.LastTransitionUtc,
            LastTransitionActor = current?.LastTransitionActor,
            LastTransitionReason = current?.LastTransitionReason,
            LastError = current?.LastError
        };
    }

    private static ComponentRuntimeState InitialState(ComponentDescriptor descriptor) =>
        !descriptor.IsConfigured
            ? ComponentRuntimeState.Misconfigured
            : descriptor.EnabledByDefault
                ? ComponentRuntimeState.Stopped
                : ComponentRuntimeState.Disabled;

    private static string InitialSummary(ComponentDescriptor descriptor) =>
        !descriptor.IsConfigured
            ? "Configuration is invalid."
            : descriptor.EnabledByDefault
                ? "Awaiting startup."
                : "Disabled by configuration.";

    private static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return null;
        var normalized = reason.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }

    private sealed class MutableComponentState
    {
        public ComponentRuntimeState RuntimeState { get; init; }
        public string Summary { get; init; } = string.Empty;
        public DateTimeOffset? LastTransitionUtc { get; init; }
        public string? LastTransitionActor { get; init; }
        public string? LastTransitionReason { get; init; }
        public string? LastError { get; init; }
    }

    private enum ComponentTransition
    {
        Enable,
        Disable,
        Restart,
        Reset
    }
}
