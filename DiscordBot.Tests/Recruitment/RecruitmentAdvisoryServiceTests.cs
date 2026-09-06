using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Modules.Recruitment;
using DiscordBot.Components;
using DiscordBot.Policies;
using DiscordBot.Services;
using DiscordBot.Services.Recruitment.Observation;
using DiscordBot.Services.Recruitment.State;
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
    public async Task EnforceStopDrainsAnInFlightMutation_AndRestartCancelsStaleIntent()
    {
        await using var f = new RecruitmentAdvisoryFixture();
        await RecruitmentEnforcementTests.StartEnforce(f);
        var service = new RecruitmentService(f.Store, f.Observations, f.Observer, Options.Create(f.Options), f.Time,
            f.Coordinator, f.Enforcement, f.Retention);
        await service.StartAsync(default);
        await service.ExecutePublicAsync(_ => Task.FromResult(true));
        f.Time.Advance(TimeSpan.FromMinutes(31));
        f.Discord.BlockActions = true;
        Task command = service.ExecutePublicAsync(async token => { await f.Enforcement.TickAsync(token); return true; });
        await f.Discord.ActionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(default);
        await Assert.ThrowsAsync<OperationCanceledException>(() => command);
        Assert.AreEqual(0, f.Discord.InFlightActions); Assert.AreEqual(0, f.Discord.Actions);
        Assert.IsFalse(service.IsRunning); Assert.IsNull(f.Observer.Receive);
        f.Discord.BlockActions = false;
        await service.StartAsync(default);
        await service.ExecutePublicAsync(_ => Task.FromResult(true));
        Assert.AreEqual(0, f.Discord.Actions);
        Assert.IsNotNull((await f.Post()).PendingAction!.CancelledAtUtc);
        f.Time.Advance(TimeSpan.FromMinutes(2));
        await service.ExecutePublicAsync(async token => { await f.Coordinator.TickAsync(token); return true; });
        Assert.AreEqual(f.Time.Now.AddMinutes(30), (await f.Post()).ChallengeDeadlineUtc);
        await service.StopAsync(default);
    }

    [TestMethod]
    public async Task EnforcementBoundarySurvivesRestart_AndDoesNotEnrollEarlierPracticePosts()
    {
        await using var f = new RecruitmentAdvisoryFixture(); await f.InitializeAsync();
        await f.Observations.InitializeAsync(default);
        f.Time.Advance(TimeSpan.FromMinutes(1)); f.Options.Mode = RecruitmentMode.Enforce;
        await f.Observations.InitializeAsync(default);
        DateTimeOffset? boundary = (await f.State()).EnforcementStartedAtUtc;
        Assert.IsFalse((await f.Post()).EnforcementEnrolled);
        f.Time.Advance(TimeSpan.FromSeconds(1));
        f.Observer.CreatedTimes[11] = f.Time.Now;
        f.Discord.Posts[11] = new(11, 101, 123, false, false, false, true, []);
        await f.Observations.ReconcileAsync(11, default);
        Assert.IsTrue((await f.State()).Posts[11].EnforcementEnrolled);
        await f.Store.ReleaseAsync(); await f.Observations.InitializeAsync(default);
        Assert.AreEqual(boundary, (await f.State()).EnforcementStartedAtUtc);
        Assert.IsTrue((await f.State()).Posts[11].EnforcementEnrolled);
        f.Options.Mode = RecruitmentMode.Advisory; await f.Observations.InitializeAsync(default);
        f.Time.Advance(TimeSpan.FromMinutes(1)); f.Options.Mode = RecruitmentMode.Enforce;
        await f.Observations.InitializeAsync(default);
        Assert.AreNotEqual(boundary, (await f.State()).EnforcementStartedAtUtc);
        Assert.IsFalse((await f.State()).Posts[11].EnforcementEnrolled);
    }

    [TestMethod]
    public async Task PinnedInteractionService_DiscoversOwnerRoutesModalAndStaffCommands()
    {
        using var client = new DiscordSocketClient();
        using var interactions = new InteractionService(client);
        await using var f = new RecruitmentAdvisoryFixture();
        var observer = new EmptyObserver();
        var options = Options.Create(f.Options);
        var observation = new ObservationCoordinator(f.Store, observer, options, TimeProvider.System);
        var service = new RecruitmentService(f.Store, observation, observer, options, TimeProvider.System, f.Coordinator);
        using var services = new ServiceCollection()
            .AddSingleton(service).AddSingleton(f.Owners).AddSingleton(f.Coordinator).AddSingleton(f.Guidelines).AddSingleton(f.Staff)
            .AddSingleton<IComponentStateReader, EnabledComponents>()
            .AddSingleton<IBotAuthorizationPolicy>(new BotAuthorizationPolicy(Options.Create(new AuthorizationOptions { ModeratorRoleId = 1 })))
            .AddSingleton(options).AddSingleton(Options.Create(new DiscordGuildOptions { GuildId = 1 }))
            .BuildServiceProvider();
        var owner = await interactions.AddModuleAsync<RecruitmentOwnerModule>(services);
        var setup = await interactions.AddModuleAsync<RecruitmentSetupModule>(services);
        Assert.AreEqual(5, owner.ComponentCommands.Count);
        Assert.AreEqual(1, owner.ModalCommands.Count);
        CollectionAssert.IsSubsetOf(new[] { "preview", "publish", "status", "review", "adopt", "reopen", "remove", "resolve-missing" }, setup.SlashCommands.Select(command => command.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "forum", "expected-topic-hash", "repair-tag-hash" },
            setup.SlashCommands.Single(command => command.Name == "publish").Parameters.Select(parameter => parameter.Name).ToArray());
    }

    [TestMethod]
    public async Task Stop_CancelsAndDrainsInteractionWorkBeforeReleasingWriter()
    {
        await using var f = new RecruitmentAdvisoryFixture();
        var observer = new EmptyObserver();
        var options = Options.Create(f.Options);
        var observation = new ObservationCoordinator(f.Store, observer, options, TimeProvider.System);
        var service = new RecruitmentService(f.Store, observation, observer, options, TimeProvider.System, f.Coordinator);
        await service.StartAsync(default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool drained = false;
        Task command = service.ExecutePublicAsync(async token =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { drained = true; }
            return true;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(default);
        await Assert.ThrowsAsync<OperationCanceledException>(() => command);
        Assert.IsTrue(drained); Assert.IsFalse(service.IsPublicRunning);
        bool called = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecutePublicAsync(_ => { called = true; return Task.FromResult(true); }));
        Assert.IsFalse(called);
        string root = Path.GetDirectoryName(Path.GetDirectoryName(f.Store.StatePath))!;
        await using var other = new StateStore(Options.Create(new StorageOptions { ServerRootPath = root }), Options.Create(new DiscordGuildOptions { GuildId = 1 }));
        Assert.IsNotNull(await other.LoadAsync());
    }

    [TestMethod]
    public async Task Observe_WithPublicServicesRegistered_DoesNotLoadTemplatesOrPublish()
    {
        await using var f = new RecruitmentAdvisoryFixture(); f.Options.Mode = RecruitmentMode.Observe;
        f.Options.GuidelinesDirectory = "missing-templates";
        var observer = new EmptyObserver();
        var options = Options.Create(f.Options);
        var observation = new ObservationCoordinator(f.Store, observer, options, TimeProvider.System);
        var service = new RecruitmentService(f.Store, observation, observer, options, TimeProvider.System, f.Coordinator);
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

    private sealed class EmptyObserver : IForumObserver
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable Subscribe(Action<ObservationEvent> receive) => new Subscription();
        public Task ValidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ThreadSnapshot>> GetActiveAsync(ulong forumId, CancellationToken cancellationToken)
        {
            ReadStarted.TrySetResult();
            return Task.FromResult<IReadOnlyList<ThreadSnapshot>>([]);
        }
        public Task<ArchivePage> GetArchivedAsync(ulong forumId, DateTimeOffset? before, CancellationToken cancellationToken) => Task.FromResult(new ArchivePage([], null, true));
        public Task<ThreadSnapshot?> GetThreadAsync(ulong threadId, CancellationToken cancellationToken) => Task.FromResult<ThreadSnapshot?>(null);
        public Task<MessageSnapshot?> GetStarterAsync(ulong threadId, CancellationToken cancellationToken) => Task.FromResult<MessageSnapshot?>(null);
        public Task<MessagePage> GetRepliesAsync(ulong threadId, ulong afterId, CancellationToken cancellationToken) => Task.FromResult(new MessagePage([], true));
        public Task<AuthorFacts> GetAuthorAsync(ulong authorId, CancellationToken cancellationToken) => Task.FromResult(new AuthorFacts(ActivityStatus.Unknown, null));
        public Task<FeedPage> FindFeedAsync(string marker, DateTimeOffset since, ulong? beforeId, CancellationToken cancellationToken) => Task.FromResult(new FeedPage(null, null, true));
        public Task<ulong> SendFeedAsync(string content, CancellationToken cancellationToken) => Task.FromResult(500ul);
        public Task<bool> EditFeedAsync(ulong messageId, string marker, string content, CancellationToken cancellationToken) => Task.FromResult(true);
        private sealed class Subscription : IDisposable { public void Dispose() { } }
    }
}
