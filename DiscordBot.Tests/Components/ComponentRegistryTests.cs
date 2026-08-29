using Discord;
using DiscordBot.Components;
using DiscordBot.Services.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Components;

[TestClass]
public sealed class ComponentRegistryTests
{
    [TestMethod]
    public async Task StartStop_AreIdempotentAndDependencyOrdered()
    {
        var calls = new List<string>();
        var core = new FakeManagedService("core", calls);
        var feature = new FakeManagedService("feature", calls);
        var registry = CreateRegistry(
            [
                Descriptor("core", ComponentKind.Core),
                Descriptor("feature", ComponentKind.ManagedWorker, "core")
            ],
            [core, feature]);

        await registry.StartAllAsync(CancellationToken.None);
        await registry.StartAllAsync(CancellationToken.None);
        await registry.StopAllAsync(CancellationToken.None);
        await registry.StopAllAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "start:core", "start:feature", "stop:feature", "stop:core" },
            calls);
    }

    [TestMethod]
    public async Task CoreAndUnsupportedComponents_RefuseRuntimeMutation()
    {
        var registry = CreateRegistry(
            [
                Descriptor("core", ComponentKind.Core),
                Descriptor("legacy", ComponentKind.ManagedEventService, "core") with
                {
                    Capabilities = ComponentCapabilities.Health
                }
            ],
            []);
        await registry.StartAllAsync(CancellationToken.None);

        var coreResult = await registry.DisableAsync("core", "test", null, CancellationToken.None);
        var legacyResult = await registry.DisableAsync("legacy", "test", null, CancellationToken.None);

        Assert.IsFalse(coreResult.Succeeded);
        StringAssert.Contains(coreResult.Message, "Core");
        Assert.IsFalse(legacyResult.Succeeded);
        StringAssert.Contains(legacyResult.Message, "lifecycle checks");
    }

    [TestMethod]
    public async Task DisableAndReset_PersistAndRestoreConfiguredDefault()
    {
        var store = new InMemoryOverrideStore();
        var service = new FakeManagedService("feature", []);
        var registry = CreateRegistry(
            [Descriptor("feature", ComponentKind.ManagedWorker)],
            [service],
            store);
        await registry.StartAllAsync(CancellationToken.None);

        var disabled = await registry.DisableAsync("feature", "123:tester", "maintenance", CancellationToken.None);

        Assert.IsTrue(disabled.Succeeded);
        Assert.AreEqual(false, store.Values["feature"].Enabled);

        var reset = await registry.ResetAsync("feature", "123:tester", "done", CancellationToken.None);

        Assert.IsTrue(reset.Succeeded);
        Assert.IsFalse(store.Values.ContainsKey("feature"));
        Assert.IsTrue(service.IsRunning);
        Assert.AreEqual(ComponentRuntimeState.Running, registry.Get("feature")?.RuntimeState);
    }

    [TestMethod]
    public async Task ConcurrentEnable_StartsServiceOnce()
    {
        var service = new FakeManagedService("feature", []) { StartDelay = TimeSpan.FromMilliseconds(25) };
        var registry = CreateRegistry(
            [Descriptor("feature", ComponentKind.ManagedWorker) with { EnabledByDefault = false }],
            [service]);
        await registry.StartAllAsync(CancellationToken.None);

        var results = await Task.WhenAll(
            registry.EnableAsync("feature", "one", null, CancellationToken.None),
            registry.EnableAsync("feature", "two", null, CancellationToken.None));

        Assert.IsTrue(results.All(result => result.Succeeded));
        Assert.AreEqual(1, service.StartCount);
    }

    [TestMethod]
    public async Task OverridePersistenceFailure_DoesNotMutateRuntimeState()
    {
        var service = new FakeManagedService("feature", []);
        var store = new InMemoryOverrideStore { FailSaves = true };
        var registry = CreateRegistry(
            [Descriptor("feature", ComponentKind.ManagedWorker) with { EnabledByDefault = false }],
            [service],
            store);
        await registry.StartAllAsync(CancellationToken.None);

        var result = await registry.EnableAsync("feature", "test", null, CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(service.IsRunning);
        Assert.IsNull(registry.Get("feature")?.OverrideEnabled);
        Assert.AreEqual(ComponentRuntimeState.Disabled, registry.Get("feature")?.RuntimeState);
    }

    [TestMethod]
    public async Task OptionalServiceResolutionFailure_DoesNotStopOtherFeatures()
    {
        var healthy = new FakeManagedService("healthy", []);
        var descriptors = new[]
        {
            Descriptor("broken", ComponentKind.ManagedWorker),
            Descriptor("healthy", ComponentKind.ManagedWorker)
        };
        var registry = new ComponentRegistry(
            new FakeCatalog(descriptors),
            new ServiceCollection().BuildServiceProvider(),
            [
                new ManagedComponentRegistration("broken", _ =>
                    throw new InvalidOperationException("Sensitive implementation detail.")),
                new ManagedComponentRegistration("healthy", _ => healthy)
            ],
            new InMemoryOverrideStore(),
            new FakeLoggingService());

        await registry.StartAllAsync(CancellationToken.None);

        Assert.AreEqual(ComponentRuntimeState.Faulted, registry.Get("broken")?.RuntimeState);
        Assert.IsFalse(registry.Get("broken")?.Summary.Contains("Sensitive", StringComparison.Ordinal));
        Assert.IsTrue(healthy.IsRunning);
        Assert.AreEqual(ComponentRuntimeState.Running, registry.Get("healthy")?.RuntimeState);
    }

    [TestMethod]
    public async Task SlowHealthCheck_IsBoundedAndReportedAsDegraded()
    {
        var service = new FakeManagedService("feature", [])
        {
            HealthDelay = TimeSpan.FromMinutes(1)
        };
        var registry = CreateRegistry(
            [Descriptor("feature", ComponentKind.ManagedWorker)],
            [service]);

        await registry.StartAllAsync(CancellationToken.None);
        var snapshot = registry.Get("feature");

        Assert.AreEqual(ComponentRuntimeState.Degraded, snapshot?.RuntimeState);
        Assert.AreEqual("Health check timed out.", snapshot?.Summary);
    }

    [TestMethod]
    public async Task OverrideStore_CorruptDocumentIsBackedUpAndIgnored()
    {
        var root = Path.Combine(Path.GetTempPath(), $"udc-components-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, ComponentOverrideStore.FileName);
            await File.WriteAllTextAsync(path, "{ invalid");
            var store = new ComponentOverrideStore(Options.Create(new DiscordBot.Settings.Options.StorageOptions
            {
                ServerRootPath = root,
                AssetsRootPath = root
            }));

            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(0, loaded.Count);
            var backup = Directory.GetFiles(root, "*.bak").Single();
            Assert.AreEqual("{ invalid", await File.ReadAllTextAsync(backup));
            Assert.IsFalse(File.Exists(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ComponentRegistry CreateRegistry(
        IReadOnlyList<ComponentDescriptor> descriptors,
        IReadOnlyList<IManagedBotService> services,
        InMemoryOverrideStore? store = null) =>
        new(
            new FakeCatalog(descriptors),
            new ServiceCollection().BuildServiceProvider(),
            services.Select(service => new ManagedComponentRegistration(service.ComponentId, _ => service)),
            store ?? new InMemoryOverrideStore(),
            new FakeLoggingService());

    private static ComponentDescriptor Descriptor(
        string id,
        ComponentKind kind,
        params string[] dependencies) =>
        new(
            id,
            id,
            id,
            kind,
            true,
            kind == ComponentKind.Core
                ? ComponentCapabilities.Health
                : ComponentCapabilities.Health | ComponentCapabilities.Toggleable | ComponentCapabilities.Restartable,
            dependencies);

    private sealed class FakeCatalog(IReadOnlyList<ComponentDescriptor> descriptors) : IComponentCatalog
    {
        public IReadOnlyList<ComponentDescriptor> Components { get; } = descriptors;
    }

    private sealed class FakeManagedService(string id, List<string> calls)
        : IManagedBotService, IComponentHealthContributor
    {
        public string ComponentId => id;
        public bool IsRunning { get; private set; }
        public int StartCount { get; private set; }
        public TimeSpan StartDelay { get; init; }
        public TimeSpan HealthDelay { get; init; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (IsRunning)
                return;
            if (StartDelay > TimeSpan.Zero)
                await Task.Delay(StartDelay, cancellationToken);
            IsRunning = true;
            StartCount++;
            calls.Add($"start:{id}");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (!IsRunning)
                return Task.CompletedTask;
            IsRunning = false;
            calls.Add($"stop:{id}");
            return Task.CompletedTask;
        }

        public async Task<ComponentHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken)
        {
            if (HealthDelay > TimeSpan.Zero)
                await Task.Delay(HealthDelay, cancellationToken);
            return new ComponentHealthSnapshot(
                IsRunning ? ComponentRuntimeState.Running : ComponentRuntimeState.Stopped,
                IsRunning ? "Running." : "Stopped.",
                DateTimeOffset.UtcNow);
        }
    }

    private sealed class InMemoryOverrideStore : IComponentOverrideStore
    {
        public bool FailSaves { get; init; }
        public Dictionary<string, ComponentOverride> Values { get; private set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyDictionary<string, ComponentOverride>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, ComponentOverride>>(Values);

        public Task SaveAsync(
            IReadOnlyDictionary<string, ComponentOverride> overrides,
            CancellationToken cancellationToken)
        {
            if (FailSaves)
                throw new IOException("Simulated persistence failure.");
            Values = new Dictionary<string, ComponentOverride>(overrides, StringComparer.OrdinalIgnoreCase);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLoggingService : ILoggingService
    {
        public void LogXp(string channel, string user, float baseXp, float bonusXp, float xpReduce, int totalXp)
        {
        }

        public Task Log(
            LogBehaviour behaviour,
            string message,
            ExtendedLogSeverity severity = ExtendedLogSeverity.Info,
            Embed? embed = null) => Task.CompletedTask;
    }
}
