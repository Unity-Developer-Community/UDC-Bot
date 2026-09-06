using DiscordBot.Services.Recruitment.Actions;
using DiscordBot.Services.Recruitment.Observation;
using DiscordBot.Services.Recruitment.Policy;
using DiscordBot.Services.Recruitment.Presentation;
using DiscordBot.Services.Recruitment.Publishing;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;
using static DiscordBot.Tests.Recruitment.RecruitmentTestData;

namespace DiscordBot.Tests.Recruitment;

internal sealed class RecruitmentAdvisoryFixture : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "udc-advisory-" + Guid.NewGuid().ToString("N"));
    public RecruitmentOptions Options { get; } = RecruitmentTestData.Options();
    public AdvisoryClock Time { get; } = new();
    public FakePublisher Discord { get; } = new();
    public FakeBanner Banner { get; } = new();
    public StateStore Store { get; }
    public GuidelineTemplates Templates { get; }
    public GuidelinePublisher Guidelines { get; }
    public OwnerActions Owners { get; }
    public LifecycleExecutor Lifecycle { get; }
    public ObservationCoordinator Observations { get; }
    public FakeRecruitmentObservation Observer { get; }
    public EnforcementCoordinator Enforcement { get; }
    public StaffActions Staff { get; }
    public HistoryRetention Retention { get; }
    public PublicCoordinator Coordinator { get; }
    public OwnerContext Owner { get; } = new(1, 10, 123);
    public Forum Forum { get; } = new(ForumKind.PaidRecruiting, 101);

    public RecruitmentAdvisoryFixture()
    {
        Options.Mode = RecruitmentMode.Advisory;
        var storage = Microsoft.Extensions.Options.Options.Create(new StorageOptions
            { ServerRootPath = _root, AssetsRootPath = Path.Combine(AppContext.BaseDirectory, "Assets") });
        var guild = Microsoft.Extensions.Options.Options.Create(new DiscordGuildOptions { GuildId = 1 });
        var options = Microsoft.Extensions.Options.Options.Create(Options);
        Store = new(storage, guild);
        Templates = new(options, storage);
        Guidelines = new(Store, Discord, Templates, Time);
        Observer = new(Store, Discord);
        Observations = new(Store, Observer, options, Time);
        Lifecycle = new(Store, Discord, Observations, options, Time);
        Owners = new(Store, Discord, Guidelines, Lifecycle, options, guild, Time);
        Enforcement = new(Store, Discord, Observations, Lifecycle, options, Time);
        Staff = new(Store, Discord, Observations, Lifecycle, options, Time);
        Retention = new(Store, options, Time);
        Coordinator = new(Store, Discord, Templates, Guidelines, Banner, Owners, options, Time);
    }

    public async Task InitializeAsync()
    {
        await Store.LoadAsync();
        await Store.InitializeAsync(Now.AddDays(-1));
        await Store.UpdateAsync(state =>
        {
            foreach (ulong id in new ulong[] { 101, 102, 103, 104 }) state.Forums[id] = new();
            var post = RecruitmentTestData.Post();
            post.Acknowledgement = AcknowledgementStatus.NotPrompted;
            post.EnforcementEnrolled = false;
            post.Title = "Gameplay programmer · $40/hour";
            state.Posts[10] = post;
            return true;
        });
        await Coordinator.InitializeAsync(default);
    }

    public async Task StartAsync() { await InitializeAsync(); await Coordinator.TickAsync(default); }
    public async Task<StateDocument> State() => (await Store.LoadAsync())!;
    public async Task<PostRecord> Post() => (await State()).Posts[10];
    public async Task<string> Generation() => (await Post()).Advisory.Generation;
    public async Task<string> Code() => (await State()).Forums[101].Publication.Confirmed!.Code;
    public async ValueTask DisposeAsync()
    {
        await Store.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

internal sealed class AdvisoryClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = RecruitmentTestData.Now;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan duration) => Now += duration;
}

internal sealed class FakeBanner : IBannerRenderer
{
    public bool Fail;
    public Task<byte[]> RenderAsync(PostRecord post, PolicyEvaluator policy, CancellationToken token) =>
        Fail ? Task.FromException<byte[]>(new IOException("native render failed")) : Task.FromResult(new byte[] { 1, 2, 3 });
}

internal sealed class FakePublisher : IForumPublisher
{
    public Dictionary<ulong, ForumSetup> Forums { get; } = new ulong[] { 101, 102, 103, 104 }
        .ToDictionary(id => id, id => new ForumSetup(id, "", []));
    public Dictionary<ulong, PublicPost> Posts { get; } = new() { [10] = new(10, 101, 123, false, false, false, true, []) };
    public PublicPost? Post
    {
        get => Posts.GetValueOrDefault(10ul);
        set { if (value is null) Posts.Remove(10); else Posts[10] = value; }
    }
    public Dictionary<ulong, AdvisoryView> Messages { get; } = [];
    public Queue<AdvisorySearch> SearchPages { get; } = [];
    public bool FailEdits, FailSendAfterWrite, FailSendBeforeWrite, FailPublishAfterWrite, FailActionAfterWrite, FailReads;
    public ulong? FailingForum;
    public int Sends, Publishes, TagAppends, Actions;
    public bool BlockActions;
    public int InFlightActions;
    public TaskCompletionSource ActionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool LastSendHadImage;
    public Action? OnSend, OnEdit;

    public Task<ForumSetup> GetForumAsync(ulong forumId, CancellationToken token)
    {
        if (FailingForum == forumId) throw new IOException("forum unavailable");
        return Task.FromResult(Forums[forumId]);
    }
    public Task AppendClosedTagAsync(ulong forumId, string expectedTagHash, CancellationToken token)
    {
        var forum = Forums[forumId];
        if (GuidelineTemplates.TagHash(forum.Tags) != expectedTagHash) throw new InvalidOperationException("tag drift");
        Forums[forumId] = forum with { Tags = forum.Tags.Append(new ForumTag(forumId + 1000, "Closed", false, null, null)).ToArray() };
        TagAppends++;
        return Task.CompletedTask;
    }
    public Task PublishTopicAsync(ulong forumId, string expectedTopicHash, string topic, CancellationToken token)
    {
        var forum = Forums[forumId];
        if (GuidelineTemplates.Hash(forum.Topic) != expectedTopicHash) throw new InvalidOperationException("topic drift");
        Forums[forumId] = forum with { Topic = topic };
        Publishes++;
        if (FailPublishAfterWrite) throw new IOException("publication response lost");
        return Task.CompletedTask;
    }
    public Task<PublicPost?> GetPostAsync(ulong threadId, CancellationToken token) =>
        FailReads ? Task.FromException<PublicPost?>(new IOException("read failed")) : Task.FromResult(Posts.GetValueOrDefault(threadId));
    public Task<AdvisorySearch> FindAdvisoryAsync(ulong threadId, string marker, ulong? beforeId, CancellationToken token)
    {
        if (SearchPages.TryDequeue(out var page)) return Task.FromResult(page);
        var found = Messages.FirstOrDefault(pair => pair.Value.Marker == marker);
        return Task.FromResult(new AdvisorySearch(found.Key == 0 ? null : new(found.Key, Now), null, true));
    }
    public Task<PublicMessage> SendAdvisoryAsync(ulong threadId, AdvisoryView view, byte[]? image, CancellationToken token)
    {
        OnSend?.Invoke();
        if (FailSendBeforeWrite) throw new IOException("send failed");
        Sends++;
        LastSendHadImage = image is not null;
        ulong id = (ulong)(500 + Sends);
        Messages[id] = view;
        if (FailSendAfterWrite) throw new IOException("send response lost");
        return Task.FromResult(new PublicMessage(id, Now));
    }
    public Task<bool> EditAdvisoryAsync(ulong threadId, ulong messageId, AdvisoryView view, CancellationToken token)
    {
        OnEdit?.Invoke();
        if (FailEdits) throw new IOException("edit failed");
        if (!Messages.ContainsKey(messageId)) return Task.FromResult(false);
        Messages[messageId] = view;
        return Task.FromResult(true);
    }
    public async Task ApplyLifecycleActionAsync(PublicPost post, ActionKind action, ulong? closedTagId, string actionId, CancellationToken token)
    {
        InFlightActions++;
        try
        {
            ActionStarted.TrySetResult();
            if (BlockActions) await Task.Delay(Timeout.Infinite, token);
            token.ThrowIfCancellationRequested();
            Actions++;
        if (action == ActionKind.Delete) Posts.Remove(post.Id);
        else if (action == ActionKind.Reopen) Posts[post.Id] = post with { Archived = false, Locked = false, Tags = post.Tags.Where(id => id != closedTagId).ToArray() };
        else Posts[post.Id] = post with { Archived = true, Locked = true };
        if (FailActionAfterWrite) throw new IOException("action response lost");
        }
        finally { InFlightActions--; }
    }
}
