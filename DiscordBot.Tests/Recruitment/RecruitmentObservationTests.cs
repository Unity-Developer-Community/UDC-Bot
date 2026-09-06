using DiscordBot.Components;
using DiscordBot.Services;
using DiscordBot.Services.Recruitment;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static DiscordBot.Tests.Recruitment.RecruitmentTestData;

namespace DiscordBot.Tests.Recruitment;

[TestClass]
public sealed class RecruitmentObservationTests
{
    [TestMethod]
    public async Task EnrollmentImportsExistingPosts_WithoutPromptsAcceptanceOrPenalties()
    {
        await using var f = new Fixture();
        f.Discord.Threads[10] = Thread(10, created: Now.AddDays(-40), archived: true);
        await f.StartAndTick();
        var state = await f.State(); var post = state.Posts[10];
        Assert.IsTrue(post.Observation.Imported);
        Assert.IsTrue(post.RequiresReview);
        Assert.IsTrue(post.Observation.HistoryUncertain);
        Assert.AreEqual(RecruitmentLifecycle.Open, post.Lifecycle); // Natural archive does not close a listing.
        Assert.AreEqual(RecruitmentAcknowledgement.NotPrompted, post.Acknowledgement);
        Assert.IsNull(post.AcceptedAtUtc); Assert.IsNull(post.ChallengeDeadlineUtc);
        Assert.IsFalse(post.EnforcementEnrolled); Assert.IsFalse(post.ChallengeEnforceable);
        Assert.AreEqual(0, state.Authors[123].ConsecutiveTimeouts);
        Assert.AreEqual(0, state.Authors[123].Groups.Count);
        Assert.AreEqual(1, f.Discord.Sends);
        StringAssert.Contains(f.Discord.Feed.Values.Single(), "ReviewRequired");
        Assert.IsTrue(f.Coordinator.HasGaps);
        Assert.IsFalse(File.ReadAllText(f.Store.StatePath).Contains("$25/hour", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task NewPostAndStarterEdit_RefreshSameFeedEntry_WithPlacementReminderOnly()
    {
        await using var f = new Fixture(); await f.Coordinator.InitializeAsync(default);
        f.Discord.Threads[10] = Thread(10);
        f.Discord.Threads[20] = Thread(20, parent: 102);
        await f.Coordinator.HandleAsync(new(RecruitmentEventKind.Changed, 10, 101), default);
        await f.Coordinator.HandleAsync(new(RecruitmentEventKind.Changed, 20, 102), default);
        await f.Coordinator.TickAsync(default);
        var state = await f.State();
        Assert.AreEqual(RecruitmentEligibilityKind.Eligible, new RecruitmentPolicyEvaluator(f.Options, f.Time).EvaluateEligibility(state, 20).Kind);
        Assert.IsTrue(f.Discord.Feed.Values.All(text => text.Contains("double-check", StringComparison.Ordinal)));
        Assert.AreEqual(RecruitmentPaymentSignal.Concrete, state.Posts[10].Payment);
        var feedId = state.Posts[10].FeedMessageId;
        f.Discord.Starter = "Revenue share only";
        f.Time.Advance(TimeSpan.FromMinutes(3));
        await f.Coordinator.HandleAsync(new(RecruitmentEventKind.Changed, 10, 101), default);
        await f.Coordinator.TickAsync(default);
        Assert.AreEqual(RecruitmentPaymentSignal.Ambiguous, (await f.State()).Posts[10].Payment);
        Assert.AreEqual(feedId, (await f.State()).Posts[10].FeedMessageId);
        Assert.AreEqual(2, f.Discord.Sends);
    }

    [TestMethod]
    public async Task ConfirmedDeletion_IsIdempotent_AndLateUpdatesCannotResurrectIt()
    {
        await using var f = new Fixture(); f.Discord.Threads[10] = Thread(10); await f.StartAndTick();
        await f.Coordinator.HandleAsync(new(RecruitmentEventKind.Deleted, 10), default);
        var observed = (await f.State()).Posts[10].DeletedObservedAtUtc;
        f.Time.Advance(TimeSpan.FromMinutes(2));
        await f.Coordinator.HandleAsync(new(RecruitmentEventKind.Deleted, 10), default);
        await f.Coordinator.HandleAsync(new(RecruitmentEventKind.Changed, 10, 101), default);
        await f.Coordinator.TickAsync(default);
        var post = (await f.State()).Posts[10];
        Assert.AreEqual(RecruitmentLifecycle.Deleted, post.Lifecycle);
        Assert.AreEqual(observed, post.DeletedObservedAtUtc);
        Assert.IsFalse(post.DeletionTimeUncertain);
    }

    [TestMethod]
    public async Task DisappearanceAndPermissionFailure_AreNotConfirmedDeletion()
    {
        await using var f = new Fixture(); f.Discord.Threads[10] = Thread(10); await f.StartAndTick();
        f.Discord.ThreadReadError = new UnauthorizedAccessException();
        await f.Coordinator.HandleAsync(new(RecruitmentEventKind.Changed, 10, 101), default);
        await f.Coordinator.TickAsync(default);
        Assert.AreEqual(RecruitmentLifecycle.Open, (await f.State()).Posts[10].Lifecycle);
        f.Discord.ThreadReadError = null; f.Discord.Threads.Clear();
        f.Time.Advance(TimeSpan.FromMinutes(3)); await f.Coordinator.TickAsync(default);
        var post = (await f.State()).Posts[10];
        Assert.AreEqual(RecruitmentLifecycle.Missing, post.Lifecycle);
        Assert.IsNull(post.DeletedObservedAtUtc); Assert.IsTrue(post.DeletionTimeUncertain);
        Assert.IsTrue(post.RequiresReview);
    }

    [TestMethod]
    public async Task ObservedHumanReply_SurvivesDeletionAndRestart_UnknownHistoricalRolesStayUnknown()
    {
        await using var f = new Fixture(); f.Discord.Threads[10] = Thread(10); await f.StartAndTick();
        var reply = Reply(11, Now.AddMinutes(1));
        await f.Coordinator.HandleAsync(new(RecruitmentEventKind.Message, 10, 101, reply), default);
        await f.Store.ReleaseAsync(); f.NewCoordinator();
        await f.StartAndTick(); // The reply is absent from REST history now.
        Assert.AreEqual(reply.Response.CreatedAtUtc, (await f.State()).Posts[10].FirstQualifyingResponseAtUtc);
        f.Discord.Threads[20] = Thread(20, parent: 102);
        f.Discord.Replies[20] = [Reply(21, Now.AddMinutes(1)) with { Response = new(456, Now.AddMinutes(1), false, false, null, null) }];
        await f.Coordinator.HandleAsync(new(RecruitmentEventKind.Changed, 20, 102), default);
        await f.Coordinator.TickAsync(default);
        var unknown = (await f.State()).Posts[20];
        Assert.IsNull(unknown.FirstQualifyingResponseAtUtc); Assert.IsTrue(unknown.Observation.HistoryUncertain);
        Assert.IsNull(unknown.ResponsesCheckedThroughUtc);
    }

    [TestMethod]
    public async Task IncompleteReplyScan_PersistsCursor_AndNeverClaimsAbsence()
    {
        await using var f = new Fixture(); f.Discord.Threads[10] = Thread(10);
        f.Discord.Replies[10] = Enumerable.Range(11, 101).Select(id =>
            Reply((ulong)id, Now.AddSeconds(id)) with { Response = new(123, Now.AddSeconds(id), false, false, false, false) }).ToList();
        await f.StartAndTick();
        Assert.AreEqual(110ul, (await f.State()).Posts[10].Observation.HistoryAfterId);
        Assert.IsNull((await f.State()).Posts[10].ResponsesCheckedThroughUtc);
        await f.Store.ReleaseAsync(); f.NewCoordinator(); f.Time.Advance(TimeSpan.FromMinutes(1));
        await f.StartAndTick();
        Assert.AreEqual(110ul, f.Discord.LastAfterId);
        Assert.AreEqual(111ul, (await f.State()).Posts[10].Observation.HistoryAfterId);
        Assert.IsNull((await f.State()).Posts[10].ResponsesCheckedThroughUtc); // Offline gap remains explicit.
    }

    [TestMethod]
    public async Task ArchiveScan_ResumesSavedPage_AcrossRestart()
    {
        await using var f = new Fixture();
        var cursor = Now.AddDays(-10);
        f.Discord.Archives = (id, before) => id != 101 ? new([], null, true) : before is null
            ? new([Thread(10, created: Now.AddDays(-1), archived: true)], cursor, false)
            : new([Thread(20, created: Now.AddDays(-20), archived: true)], null, true);
        await f.StartAndTick();
        Assert.AreEqual(cursor, (await f.State()).Forums[101].ArchiveBeforeUtc);
        await f.Store.ReleaseAsync(); f.NewCoordinator(); await f.StartAndTick();
        var state = await f.State();
        Assert.AreEqual(cursor, f.Discord.ArchiveRequests.Last(r => r.Id == 101).Before);
        Assert.AreEqual(2, state.Posts.Count);
        Assert.IsNull(state.Forums[101].ArchiveCompletedAtUtc); // Resume, then catch up the archive head missed offline.
        Assert.IsNull(state.Forums[101].ArchiveBeforeUtc);
        await f.Coordinator.TickAsync(default);
        Assert.IsNull(f.Discord.ArchiveRequests.Last(r => r.Id == 101).Before);
    }

    [TestMethod]
    public async Task ArchiveCursorFailure_IsVisibleAndDoesNotClaimCompleteInventory()
    {
        await using var f = new Fixture();
        f.Discord.Archives = (_, _) => new([], null, false);
        await f.StartAndTick();
        Assert.IsTrue((await f.State()).Forums.Values.All(x => x.Error is not null && x.ArchiveCompletedAtUtc is null));
        Assert.IsTrue(f.Coordinator.HasGaps);
    }

    [TestMethod]
    public async Task SendAcceptedButResponseLost_RecoversOneFeedEntryAfterRestart()
    {
        await using var f = new Fixture(); f.Discord.Threads[10] = Thread(10);
        f.Discord.ThrowAfterSend = true; await f.StartAndTick();
        Assert.IsNotNull((await f.State()).Posts[10].Observation.FeedSendRequestedAtUtc);
        Assert.IsNull((await f.State()).Posts[10].FeedMessageId);
        await f.Store.ReleaseAsync(); f.NewCoordinator(); f.Time.Advance(TimeSpan.FromMinutes(3));
        await f.StartAndTick();
        Assert.AreEqual(1, f.Discord.Sends); Assert.AreEqual(1, f.Discord.Feed.Count);
        Assert.IsNotNull((await f.State()).Posts[10].FeedMessageId);
        Assert.IsNull((await f.State()).Posts[10].Observation.FeedSendRequestedAtUtc);
    }

    [TestMethod]
    public async Task SendSavedOnDiscord_ButLocalCommitFails_RecoversDurableIntent()
    {
        await using var f = new Fixture(); f.Discord.Threads[10] = Thread(10);
        f.Discord.AfterSend = () => { File.Delete(f.Store.BackupPath); Directory.CreateDirectory(f.Store.BackupPath); };
        await f.Coordinator.InitializeAsync(default);
        await Assert.ThrowsAsync<IOException>(() => f.Coordinator.TickAsync(default));
        Assert.IsFalse(f.Store.IsHealthy);
        await f.Store.ReleaseAsync(); Directory.Delete(f.Store.BackupPath); f.Discord.AfterSend = null;
        f.NewCoordinator(); await f.StartAndTick();
        Assert.AreEqual(1, f.Discord.Sends); Assert.IsNotNull((await f.State()).Posts[10].FeedMessageId);
    }

    [TestMethod]
    public async Task FeedRecovery_PaginatesBeforeRetrying_AndRecoversDeletedFeedMessage()
    {
        await using var f = new Fixture(); f.Discord.Threads[10] = Thread(10); f.Discord.ThrowAfterSend = true;
        await f.StartAndTick();
        f.Discord.FindPages.Enqueue(new(null, 500, false));
        f.Time.Advance(TimeSpan.FromMinutes(3)); await f.Coordinator.TickAsync(default);
        Assert.AreEqual(500ul, (await f.State()).Posts[10].Observation.FeedSearchBeforeId);
        Assert.AreEqual(1, f.Discord.Sends);
        f.Time.Advance(TimeSpan.FromMinutes(1)); await f.Coordinator.TickAsync(default);
        Assert.AreEqual(1, f.Discord.Sends);
        f.Discord.Feed.Clear(); f.Discord.ThrowAfterSend = false;
        f.Time.Advance(TimeSpan.FromMinutes(3)); await f.Coordinator.TickAsync(default);
        Assert.AreEqual(2, f.Discord.Sends); Assert.AreEqual(1, f.Discord.Feed.Count);
    }

    [TestMethod]
    public async Task RepeatedStartStop_DrainsCallsAndReleasesWriter_WithoutDuplicateSubscriptions()
    {
        await using var f = new Fixture(); f.Discord.BlockReads = true;
        var service = f.Service();
        await service.StartAsync(default); await service.StartAsync(default);
        await f.Discord.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, f.Discord.Subscriptions);
        await service.StopAsync(default);
        Assert.AreEqual(0, f.Discord.Subscriptions); Assert.AreEqual(0, f.Discord.InFlight);
        Assert.IsFalse(service.IsRunning);
        await using (var other = f.OtherStore()) Assert.IsNotNull(await other.LoadAsync());
        await service.StartAsync(default); Assert.AreEqual(1, f.Discord.Subscriptions);
        await service.StopAsync(default);
    }

    [TestMethod]
    public async Task QueueOverflow_RecordsCoverageGap_InsteadOfSilentLoss()
    {
        await using var f = new Fixture();
        f.Discord.OnSubscribe = receive =>
        {
            f.Time.Advance(TimeSpan.FromMinutes(1));
            for (var i = 0; i < 300; i++) receive(new(RecruitmentEventKind.Changed, (ulong)i));
        };
        f.Discord.BlockReads = true;
        var service = f.Service(); await service.StartAsync(default);
        await f.Discord.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(44L, (await f.State()).DroppedObservationEvents);
        Assert.IsTrue(f.Coordinator.HasGaps);
        await service.StopAsync(default);
    }

    [TestMethod]
    [DataRow(RecruitmentMode.Advisory)]
    [DataRow(RecruitmentMode.Enforce)]
    public async Task EnforceOrMissingAdvisoryServices_RejectStartupBeforeSubscribingOrCreatingState(RecruitmentMode mode)
    {
        await using var f = new Fixture(); f.Options.Mode = mode;
        var service = f.Service();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(default));
        Assert.IsFalse(service.IsRunning); Assert.AreEqual(0, f.Discord.Subscriptions);
        Assert.IsFalse(File.Exists(f.Store.StatePath));
    }

    [TestMethod]
    public async Task FailedValidationAndCorruptState_UnsubscribeAndPreserveSource()
    {
        await using var f = new Fixture(); f.Discord.ValidationError = new InvalidOperationException("wrong guild");
        var service = f.Service(); await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(default));
        Assert.AreEqual(0, f.Discord.Subscriptions); Assert.IsFalse(File.Exists(f.Store.StatePath));
        f.Discord.ValidationError = null; Directory.CreateDirectory(Path.GetDirectoryName(f.Store.StatePath)!);
        File.WriteAllText(f.Store.StatePath, "damaged");
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => service.StartAsync(default));
        Assert.AreEqual("damaged", File.ReadAllText(f.Store.StatePath)); Assert.AreEqual(0, f.Discord.Subscriptions);
    }

    private static RecruitmentThreadSnapshot Thread(ulong id, ulong parent = 101, DateTimeOffset? created = null, bool archived = false) =>
        new(id, parent, 123, created ?? Now, "Listing", [], archived, false, false, false, created ?? Now);
    private static RecruitmentMessageSnapshot Reply(ulong id, DateTimeOffset created) => new(id, new(456, created, false, false, false, false), null);

    private sealed class MutableTime : TimeProvider
    {
        private DateTimeOffset _now = Now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "udc-observe-" + Guid.NewGuid().ToString("N"));
        public readonly RecruitmentOptions Options = RecruitmentTestData.Options();
        public readonly MutableTime Time = new();
        public readonly FakeObserver Discord = new();
        public RecruitmentStateStore Store { get; }
        public RecruitmentObservationCoordinator Coordinator { get; private set; }
        public Fixture() { Options.Mode = RecruitmentMode.Observe; Store = OtherStore(); Coordinator = NewCoordinator(); }
        public RecruitmentObservationCoordinator NewCoordinator() => Coordinator = new(Store, Discord, Microsoft.Extensions.Options.Options.Create(Options), Time);
        public RecruitService Service() => new(Store, Coordinator, Discord, Microsoft.Extensions.Options.Options.Create(Options), Time);
        public RecruitmentStateStore OtherStore() => new(Microsoft.Extensions.Options.Options.Create(new StorageOptions { ServerRootPath = _root }),
            Microsoft.Extensions.Options.Options.Create(new DiscordGuildOptions { GuildId = 1 }));
        public async Task StartAndTick() { await Coordinator.InitializeAsync(default); await Coordinator.TickAsync(default); }
        public async Task<RecruitmentStateDocument> State() => (await Store.LoadAsync())!;
        public async ValueTask DisposeAsync() { await Store.DisposeAsync(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }

    private sealed class FakeObserver : IRecruitmentObserver
    {
        public readonly Dictionary<ulong, RecruitmentThreadSnapshot> Threads = [];
        public readonly Dictionary<ulong, List<RecruitmentMessageSnapshot>> Replies = [];
        public readonly Dictionary<ulong, string> Feed = [];
        public readonly Queue<RecruitmentFeedPage> FindPages = [];
        public readonly List<(ulong Id, DateTimeOffset? Before)> ArchiveRequests = [];
        public Func<ulong, DateTimeOffset?, RecruitmentArchivePage>? Archives;
        public Action<Action<RecruitmentObservationEvent>>? OnSubscribe;
        public Action? AfterSend;
        public Exception? ThreadReadError, ValidationError;
        public string Starter = "Budget $25/hour";
        public int Sends, Subscriptions, InFlight;
        public ulong LastAfterId;
        public bool ThrowAfterSend, BlockReads;
        public readonly TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable Subscribe(Action<RecruitmentObservationEvent> receive)
        { Subscriptions++; OnSubscribe?.Invoke(receive); return new Subscription(() => Subscriptions--); }
        public Task ValidateAsync(CancellationToken cancellationToken) => ValidationError is null ? Task.CompletedTask : Task.FromException(ValidationError);
        public async Task<IReadOnlyList<RecruitmentThreadSnapshot>> GetActiveAsync(ulong forumId, CancellationToken cancellationToken)
        {
            InFlight++;
            try
            {
                ReadStarted.TrySetResult();
                if (BlockReads) await Task.Delay(Timeout.Infinite, cancellationToken);
                return Threads.Values.Where(t => t.ParentId == forumId && !t.Archived).ToArray();
            }
            finally { InFlight--; }
        }
        public Task<RecruitmentArchivePage> GetArchivedAsync(ulong forumId, DateTimeOffset? before, CancellationToken cancellationToken)
        {
            ArchiveRequests.Add((forumId, before));
            var result = Archives?.Invoke(forumId, before) ?? new(Threads.Values.Where(t => t.ParentId == forumId && t.Archived).ToArray(), null, true);
            foreach (var thread in result.Threads) Threads[thread.Id] = thread;
            return Task.FromResult(result);
        }
        public Task<RecruitmentThreadSnapshot?> GetThreadAsync(ulong threadId, CancellationToken cancellationToken) =>
            ThreadReadError is null ? Task.FromResult(Threads.GetValueOrDefault(threadId)) : Task.FromException<RecruitmentThreadSnapshot?>(ThreadReadError);
        public Task<RecruitmentMessageSnapshot?> GetStarterAsync(ulong threadId, CancellationToken cancellationToken) =>
            Task.FromResult<RecruitmentMessageSnapshot?>(new(threadId, new(123, Now, false, false, false, false), Starter));
        public Task<RecruitmentMessagePage> GetRepliesAsync(ulong threadId, ulong afterId, CancellationToken cancellationToken)
        {
            LastAfterId = afterId;
            var messages = Replies.GetValueOrDefault(threadId, []).Where(m => m.Id > afterId).OrderBy(m => m.Id).Take(100).ToArray();
            return Task.FromResult(new RecruitmentMessagePage(messages, messages.Length < 100));
        }
        public Task<RecruitmentAuthorFacts> GetAuthorAsync(ulong authorId, CancellationToken cancellationToken) => Task.FromResult(new RecruitmentAuthorFacts(RecruitmentActivity.Unknown, null));
        public Task<RecruitmentFeedPage> FindFeedAsync(string marker, DateTimeOffset since, ulong? beforeId, CancellationToken cancellationToken) =>
            Task.FromResult(FindPages.Count > 0 ? FindPages.Dequeue() : new RecruitmentFeedPage(
                Feed.Where(p => p.Value.EndsWith($"`{marker}`", StringComparison.Ordinal)).Select(p => (ulong?)p.Key).FirstOrDefault(), null, true));
        public Task<ulong> SendFeedAsync(string content, CancellationToken cancellationToken)
        {
            Assert.IsTrue(content.Length <= 2000, $"Feed text too long: {content.Length}");
            var id = (ulong)++Sends + 1000; Feed[id] = content; AfterSend?.Invoke();
            return ThrowAfterSend ? Task.FromException<ulong>(new IOException("send response lost")) : Task.FromResult(id);
        }
        public Task<bool> EditFeedAsync(ulong messageId, string marker, string content, CancellationToken cancellationToken)
        {
            if (!Feed.ContainsKey(messageId)) return Task.FromResult(false);
            Assert.IsTrue(Feed[messageId].EndsWith($"`{marker}`", StringComparison.Ordinal));
            Feed[messageId] = content; return Task.FromResult(true);
        }
        private sealed class Subscription(Action close) : IDisposable
        {
            private Action? _close = close;
            public void Dispose() => Interlocked.Exchange(ref _close, null)?.Invoke();
        }
    }
}
