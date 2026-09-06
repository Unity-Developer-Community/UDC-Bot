using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Modules.Recruitment;
using DiscordBot.Components;
using DiscordBot.Policies;
using DiscordBot.Services;
using DiscordBot.Services.Recruitment;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Recruitment;

[TestClass]
public sealed class RecruitmentAdvisoryServiceTests
{
    [TestMethod]
    public async Task PinnedInteractionService_DiscoversOwnerRoutesModalAndStaffCommands()
    {
        using var client = new DiscordSocketClient();
        using var interactions = new InteractionService(client);
        await using var f = new RecruitmentAdvisoryFixture();
        var observer = new EmptyObserver();
        var options = Options.Create(f.Options);
        var observation = new RecruitmentObservationCoordinator(f.Store, observer, options, TimeProvider.System);
        var service = new RecruitService(f.Store, observation, observer, options, TimeProvider.System, f.Coordinator);
        using var services = new ServiceCollection()
            .AddSingleton(service).AddSingleton(f.Owners).AddSingleton(f.Coordinator).AddSingleton(f.Guidelines)
            .AddSingleton<IComponentStateReader, EnabledComponents>()
            .AddSingleton<IBotAuthorizationPolicy>(new BotAuthorizationPolicy(Options.Create(new AuthorizationOptions { ModeratorRoleId = 1 })))
            .AddSingleton(options).AddSingleton(Options.Create(new DiscordGuildOptions { GuildId = 1 }))
            .BuildServiceProvider();
        var owner = await interactions.AddModuleAsync<RecruitmentOwnerModule>(services);
        var setup = await interactions.AddModuleAsync<RecruitmentSetupModule>(services);
        Assert.AreEqual(5, owner.ComponentCommands.Count);
        Assert.AreEqual(1, owner.ModalCommands.Count);
        CollectionAssert.AreEquivalent(new[] { "preview", "publish" }, setup.SlashCommands.Select(command => command.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "forum", "expected-topic-hash", "repair-tag-hash" },
            setup.SlashCommands.Single(command => command.Name == "publish").Parameters.Select(parameter => parameter.Name).ToArray());
    }

    [TestMethod]
    public async Task Stop_CancelsAndDrainsInteractionWorkBeforeReleasingWriter()
    {
        await using var f = new RecruitmentAdvisoryFixture();
        var observer = new EmptyObserver();
        var options = Options.Create(f.Options);
        var observation = new RecruitmentObservationCoordinator(f.Store, observer, options, TimeProvider.System);
        var service = new RecruitService(f.Store, observation, observer, options, TimeProvider.System, f.Coordinator);
        await service.StartAsync(default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool drained = false;
        Task command = service.ExecuteAdvisoryAsync(async token =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { drained = true; }
            return true;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(default);
        await Assert.ThrowsAsync<OperationCanceledException>(() => command);
        Assert.IsTrue(drained); Assert.IsFalse(service.IsAdvisoryRunning);
        bool called = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAdvisoryAsync(_ => { called = true; return Task.FromResult(true); }));
        Assert.IsFalse(called);
        string root = Path.GetDirectoryName(Path.GetDirectoryName(f.Store.StatePath))!;
        await using var other = new RecruitmentStateStore(Options.Create(new StorageOptions { ServerRootPath = root }), Options.Create(new DiscordGuildOptions { GuildId = 1 }));
        Assert.IsNotNull(await other.LoadAsync());
    }

    [TestMethod]
    public async Task Observe_WithPublicServicesRegistered_DoesNotLoadTemplatesOrPublish()
    {
        await using var f = new RecruitmentAdvisoryFixture(); f.Options.Mode = RecruitmentMode.Observe;
        f.Options.GuidelinesDirectory = "missing-templates";
        var observer = new EmptyObserver();
        var options = Options.Create(f.Options);
        var observation = new RecruitmentObservationCoordinator(f.Store, observer, options, TimeProvider.System);
        var service = new RecruitService(f.Store, observation, observer, options, TimeProvider.System, f.Coordinator);
        await service.StartAsync(default);
        await observer.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(default);
        Assert.AreEqual(0, f.Discord.Publishes + f.Discord.Sends + f.Discord.TagAppends + f.Discord.Actions);
    }

    private sealed class EnabledComponents : IComponentStateReader
    {
        public event EventHandler? StateChanged { add { } remove { } }
        public IReadOnlyList<ComponentSnapshot> GetAll() => [];
        public ComponentSnapshot? Get(string componentId) => null;
        public bool IsEnabled(string componentId) => true;
    }

    private sealed class EmptyObserver : IRecruitmentObserver
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable Subscribe(Action<RecruitmentObservationEvent> receive) => new Subscription();
        public Task ValidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<RecruitmentThreadSnapshot>> GetActiveAsync(ulong forumId, CancellationToken cancellationToken)
        {
            ReadStarted.TrySetResult();
            return Task.FromResult<IReadOnlyList<RecruitmentThreadSnapshot>>([]);
        }
        public Task<RecruitmentArchivePage> GetArchivedAsync(ulong forumId, DateTimeOffset? before, CancellationToken cancellationToken) => Task.FromResult(new RecruitmentArchivePage([], null, true));
        public Task<RecruitmentThreadSnapshot?> GetThreadAsync(ulong threadId, CancellationToken cancellationToken) => Task.FromResult<RecruitmentThreadSnapshot?>(null);
        public Task<RecruitmentMessageSnapshot?> GetStarterAsync(ulong threadId, CancellationToken cancellationToken) => Task.FromResult<RecruitmentMessageSnapshot?>(null);
        public Task<RecruitmentMessagePage> GetRepliesAsync(ulong threadId, ulong afterId, CancellationToken cancellationToken) => Task.FromResult(new RecruitmentMessagePage([], true));
        public Task<RecruitmentAuthorFacts> GetAuthorAsync(ulong authorId, CancellationToken cancellationToken) => Task.FromResult(new RecruitmentAuthorFacts(RecruitmentActivity.Unknown, null));
        public Task<RecruitmentFeedPage> FindFeedAsync(string marker, DateTimeOffset since, ulong? beforeId, CancellationToken cancellationToken) => Task.FromResult(new RecruitmentFeedPage(null, null, true));
        public Task<ulong> SendFeedAsync(string content, CancellationToken cancellationToken) => Task.FromResult(500ul);
        public Task<bool> EditFeedAsync(ulong messageId, string marker, string content, CancellationToken cancellationToken) => Task.FromResult(true);
        private sealed class Subscription : IDisposable { public void Dispose() { } }
    }
}
