using DiscordBot.Settings;
using DiscordBot.Utils;

namespace DiscordBot.Services.Recruitment.Application;

/// <summary>Applies queued facts and identity changes on the existing worker.</summary>
internal sealed class ListingEvents(
    RecruitmentState state,
    RecruitmentSettings settings,
    IRecruitmentDiscord discord,
    TimeProvider time,
    Action save,
    ListingWorkflow workflow,
    ListingReader reader,
    ReferenceReconciler references,
    ListingPublisher publisher,
    RecruitmentOverviewPublisher overview,
    Func<bool> ready,
    Action requestRecovery)
{
    private readonly RecruitmentState State = state;
    private readonly RecruitmentSettings _settings = settings;
    private readonly IRecruitmentDiscord _discord = discord;
    private readonly ListingWorkflow _workflow = workflow;
    private readonly ListingReader _reader = reader;
    private readonly ReferenceReconciler _references = references;
    private readonly ListingPublisher _publisher = publisher;
    private readonly RecruitmentOverviewPublisher _overview = overview;
    private DateTimeOffset Now => time.GetUtcNow();
    private void Save() => save();
    public async Task ReconcileThreadAsync(string thread, CancellationToken ct)
    {
        if (!ready() || State?.Listings.GetValueOrDefault(thread) is not { } post) return;
        await _workflow!.ProcessSafelyAsync(post, ct);
        foreach (var waiting in State.Listings.Values.Where(p => p.Id != post.Id && p.OwnerId == post.OwnerId &&
                     p.Status == ListingStatus.Open && !p.AcceptedAt.HasValue).OrderBy(p => p.CreatedAt).ToArray())
            await _workflow!.ProcessSafelyAsync(waiting, ct);
    }

    public async Task ThreadCreatedAsync(ThreadCreation creation, CancellationToken ct)
    {
        if (State is null || !ready() || !State.Forums.Values.Any(f => f.ForumId == creation.ForumId)) return;
        await ThreadChangedAsync(creation.Id, ct);
        if (State.Listings.ContainsKey(creation.Id) || creation.CreatedAt <= Now.AddMinutes(-_settings.AttemptWindowMinutes)) return;
        // THREAD_CREATE can also announce access to an old thread. Only recent, confirmed-absent
        // creations with a known member identity count here; successful discovery already counted itself.
        if (await _discord.GetThreadAsync(creation.Id, ct, refresh: true) is not null) return;
        var author = await _discord.GetAuthorAsync(creation.OwnerId, ct);
        if (author is null || author.IsBot || author.IsStaff) return;
        var post = new Listing
        {
            Id = creation.Id, ForumId = creation.ForumId, OwnerId = creation.OwnerId,
            Title = DiscordDisplayText.Literal(creation.Title), CreatedAt = creation.CreatedAt.ToUniversalTime(), UpdatedAt = Now,
            Status = ListingStatus.Deleted, LastOutcome = "Thread absent before first read; creation observed, deletion actor unknown",
            PublicationPending = true
        };
        State.Listings[post.Id] = post;
        _workflow!.RecordCreation(post); Save();
        await _workflow!.ProcessSafelyAsync(post, ct);
    }

    public async Task ThreadChangedAsync(string id, CancellationToken ct)
    {
        _discord.InvalidateThread(id);
        if (State?.Forums.Values.Any(f => f.Reference.ThreadId == id) == true) return;
        if (State is null || !ready()) return;
        if (!State.Listings.TryGetValue(id, out var post))
        {
            var live = await _discord.GetThreadAsync(id, ct);
            // ThreadCreated also signals access to an existing thread; it cannot certify reply coverage.
            if (live is not null) { _references!.ObserveReferenceCandidate(live); await _workflow.ObserveAsync(live, false, ct); }
            if (!State.Listings.TryGetValue(id, out post)) return;
        }
        post.Fault = null;
        await ReconcileThreadAsync(id, ct);
    }

    public async Task ThreadDeletedAsync(string id, CancellationToken ct)
    {
        _discord.InvalidateThread(id);
        if (State?.Forums.Values.FirstOrDefault(f => f.Reference.ThreadId == id) is { } forum)
        {
            forum.Reference = new ReferenceReceipt(); _references!.Forget(forum.ForumId, id);
            requestRecovery(); Save();
        }
        if (State?.Listings.GetValueOrDefault(id) is not { } post) return;
        _reader!.RecordAbsence(post);
        if (ready()) await ReconcileThreadAsync(id, ct);
    }

    public Task FactsChangedAsync(string id, CancellationToken ct)
    {
        _discord.InvalidateFacts(id);
        return ThreadChangedAsync(id, ct);
    }

    public async Task MessageDeletedAsync(string channel, IReadOnlySet<string> ids, CancellationToken ct)
    {
        if (State is null) return;
        if (channel == _settings.FeedChannelId.ToString() && State.Overview.MessageId is { } overview && ids.Contains(overview))
        { State.Overview.MessageId = null; State.Overview.Fault = null; _overview!.InvalidateRender(); Save(); }
        foreach (var post in State.Listings.Values.Where(p =>
                     channel == _settings.FeedChannelId.ToString() && p.StaffMessageId is not null && ids.Contains(p.StaffMessageId) ||
                     channel == p.Id && p.PublicMessageId is not null && ids.Contains(p.PublicMessageId)).ToArray())
        {
            if (channel == post.Id) { post.PublicMessageId = null; _publisher!.InvalidatePublic(post.Id); }
            else { post.StaffMessageId = null; _publisher!.InvalidateStaff(post.Id); }
            post.PublicationPending = true; post.Fault = null; Save();
            if (ready()) await _workflow!.ProcessSafelyAsync(post, ct);
        }
    }

}
