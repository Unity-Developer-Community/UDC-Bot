using DiscordBot.Services.Recruitment;
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
    public RecruitmentStateStore Store { get; }
    public RecruitmentGuidelines Templates { get; }
    public RecruitmentGuidelinePublisher Guidelines { get; }
    public RecruitmentOwnerActions Owners { get; }
    public RecruitmentAdvisoryCoordinator Coordinator { get; }
    public RecruitmentOwnerContext Owner { get; } = new(1, 10, 123);
    public RecruitmentForum Forum { get; } = new(RecruitmentForumKind.PaidRecruiting, 101);

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
        Owners = new(Store, Discord, Guidelines, options, guild, Time);
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
            post.Acknowledgement = RecruitmentAcknowledgement.NotPrompted;
            post.EnforcementEnrolled = false;
            post.Title = "Gameplay programmer · $40/hour";
            state.Posts[10] = post;
            return true;
        });
        await Coordinator.InitializeAsync(default);
    }

    public async Task StartAsync() { await InitializeAsync(); await Coordinator.TickAsync(default); }
    public async Task<RecruitmentStateDocument> State() => (await Store.LoadAsync())!;
    public async Task<RecruitmentPostRecord> Post() => (await State()).Posts[10];
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

internal sealed class FakeBanner : IRecruitmentBannerRenderer
{
    public bool Fail;
    public Task<byte[]> RenderAsync(RecruitmentPostRecord post, RecruitmentPolicyEvaluator policy, CancellationToken token) =>
        Fail ? Task.FromException<byte[]>(new IOException("native render failed")) : Task.FromResult(new byte[] { 1, 2, 3 });
}

internal sealed class FakePublisher : IRecruitmentPublisher
{
    public Dictionary<ulong, RecruitmentForumSetup> Forums { get; } = new ulong[] { 101, 102, 103, 104 }
        .ToDictionary(id => id, id => new RecruitmentForumSetup(id, "", []));
    public RecruitmentPublicPost? Post { get; set; } = new(10, 101, 123, false, false, false, true, []);
    public Dictionary<ulong, RecruitmentAdvisoryView> Messages { get; } = [];
    public Queue<RecruitmentAdvisorySearch> SearchPages { get; } = [];
    public bool FailEdits, FailSendAfterWrite, FailSendBeforeWrite, FailPublishAfterWrite, FailActionAfterWrite, FailReads;
    public ulong? FailingForum;
    public int Sends, Publishes, TagAppends, Actions;
    public bool LastSendHadImage;
    public Action? OnSend, OnEdit;

    public Task<RecruitmentForumSetup> GetForumAsync(ulong forumId, CancellationToken token)
    {
        if (FailingForum == forumId) throw new IOException("forum unavailable");
        return Task.FromResult(Forums[forumId]);
    }
    public Task AppendClosedTagAsync(ulong forumId, string expectedTagHash, CancellationToken token)
    {
        var forum = Forums[forumId];
        if (RecruitmentGuidelines.TagHash(forum.Tags) != expectedTagHash) throw new InvalidOperationException("tag drift");
        Forums[forumId] = forum with { Tags = forum.Tags.Append(new RecruitmentForumTag(forumId + 1000, "Closed", false, null, null)).ToArray() };
        TagAppends++;
        return Task.CompletedTask;
    }
    public Task PublishTopicAsync(ulong forumId, string expectedTopicHash, string topic, CancellationToken token)
    {
        var forum = Forums[forumId];
        if (RecruitmentGuidelines.Hash(forum.Topic) != expectedTopicHash) throw new InvalidOperationException("topic drift");
        Forums[forumId] = forum with { Topic = topic };
        Publishes++;
        if (FailPublishAfterWrite) throw new IOException("publication response lost");
        return Task.CompletedTask;
    }
    public Task<RecruitmentPublicPost?> GetPostAsync(ulong threadId, CancellationToken token) =>
        FailReads ? Task.FromException<RecruitmentPublicPost?>(new IOException("read failed")) : Task.FromResult(Post);
    public Task<RecruitmentAdvisorySearch> FindAdvisoryAsync(ulong threadId, string marker, ulong? beforeId, CancellationToken token)
    {
        if (SearchPages.TryDequeue(out var page)) return Task.FromResult(page);
        var found = Messages.FirstOrDefault(pair => pair.Value.Marker == marker);
        return Task.FromResult(new RecruitmentAdvisorySearch(found.Key == 0 ? null : new(found.Key, Now), null, true));
    }
    public Task<RecruitmentPublicMessage> SendAdvisoryAsync(ulong threadId, RecruitmentAdvisoryView view, byte[]? image, CancellationToken token)
    {
        OnSend?.Invoke();
        if (FailSendBeforeWrite) throw new IOException("send failed");
        Sends++;
        LastSendHadImage = image is not null;
        ulong id = (ulong)(500 + Sends);
        Messages[id] = view;
        if (FailSendAfterWrite) throw new IOException("send response lost");
        return Task.FromResult(new RecruitmentPublicMessage(id, Now));
    }
    public Task<bool> EditAdvisoryAsync(ulong threadId, ulong messageId, RecruitmentAdvisoryView view, CancellationToken token)
    {
        OnEdit?.Invoke();
        if (FailEdits) throw new IOException("edit failed");
        if (!Messages.ContainsKey(messageId)) return Task.FromResult(false);
        Messages[messageId] = view;
        return Task.FromResult(true);
    }
    public Task ApplyOwnerActionAsync(RecruitmentPublicPost post, RecruitmentActionKind action, ulong? closedTagId, string actionId, CancellationToken token)
    {
        Actions++;
        Post = action == RecruitmentActionKind.Delete ? null : post with { Archived = true, Locked = true };
        if (FailActionAfterWrite) throw new IOException("action response lost");
        return Task.CompletedTask;
    }
}
