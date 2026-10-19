using DiscordBot.Services.Recruitment.Domain.Policies;
namespace DiscordBot.Services.Recruitment;

public sealed partial class RecruitmentEngine
{
    private readonly Func<long> _evidenceGeneration;
    private readonly Func<bool> _gatewayReady;
    private readonly Func<bool> _pendingIdentities;
    private long _inventoryGeneration = -1;
    private DateTimeOffset _nextAudit = DateTimeOffset.MaxValue;
    private bool _accessInvalid = true;
    private readonly Dictionary<string, string> _publicRenders = new();
    private readonly Dictionary<string, string> _staffRenders = new();
    private bool EvidenceCurrent => _gatewayReady() && !_pendingIdentities() && _inventoryComplete && _inventoryGeneration == _evidenceGeneration();
    private bool RoutinePendingIdentity => _ready && _gatewayReady() && _pendingIdentities() && _inventoryComplete && _inventoryGeneration == _evidenceGeneration();
    public event Action? Changed;

    public RecruitmentHealth Health(string work = "Idle") => new(_ready ? EvidenceCurrent ? "Operating" : RoutinePendingIdentity ? "Processing new listing identities" : "Inventory incomplete; acceptance and automatic actions held" : Status, work, Now,
        State?.Listings.Values.Count(p => p.Status == ListingStatus.Open) ?? 0,
        State?.Listings.Values.Count(p => p.Status == ListingStatus.Open && !p.AcknowledgedAt.HasValue) ?? 0,
        State?.Listings.Values.Count(p => p.Hold is not null) ?? 0, EvidenceCurrent,
        State?.LastSweepAt, NextWorkAt,
        State?.Forums.Select(f => new ForumHealth(f.Key, f.Value.ForumId,
            f.Value.Fault is not null ? "held" : f.Value.PendingTopic is not null ? "publication pending" :
            f.Value.ConfirmedTopic.Length == 0 ? "not verified" : "Guidelines and tags verified",
            f.Value.Fault?.Message, f.Value.Fault?.RetryAt, f.Value.Reference.ThreadId, f.Value.Reference.Fault?.Message)).ToArray() ?? Array.Empty<ForumHealth>(), State?.Overview.Fault?.Message, RoutinePendingIdentity: RoutinePendingIdentity, Attention: AttentionSummary(), Metrics: ActivitySummary());

    private DateTimeOffset? NextWorkAt
    {
        get
        {
            if (State is null || !_settings.Enabled) return null;
            var times = new List<DateTimeOffset>();
            if (_nextAudit != DateTimeOffset.MaxValue) times.Add(_nextAudit);
            if (State.RecoveryFault?.RetryAt is { } retry) times.Add(retry);
            // Expired child retries cannot run through a parent hold; scheduling them would wake every second.
            if (State.RecoveryFault is null)
                times.AddRange(State.Forums.Values.Where(f => f.Fault?.RetryAt is not null).Select(f => f.Fault!.RetryAt!.Value));
            if (_ready)
                times.AddRange(State.Forums.Values.Where(f => f.Reference.Fault?.RetryAt is not null).Select(f => f.Reference.Fault!.RetryAt!.Value));
            if (_ready && EvidenceCurrent)
            {
                times.Add(State.RotateAt);
                times.AddRange(State.Listings.Values.Select(ListingDue).Where(d => d.HasValue).Select(d => d!.Value));
            }
            return times.Count == 0 ? null : times.Min();
        }
    }

    private DateTimeOffset? ListingDue(Listing post)
    {
        if (post.Fault is { } fault) return fault.RetryAt;
        var pinDue = PinDue(post) ? (DateTimeOffset?)Now :
            post.Status is ListingStatus.Open or ListingStatus.Closed && post.PublicMessageId is not null
                ? post.PublicPin.Fault?.RetryAt : null;
        if (post.PendingAction is not null || post.PublicSendPending || post.StaffSendPending || post.PublicationPending) return Now;
        var attention = post.Attention.Where(n => n.MessageId is null || n.SendPending)
            .Select(n => n.Fault is null ? (DateTimeOffset?)Now : n.Fault.RetryAt).Where(t => t.HasValue).Min();
        if (post.Status != ListingStatus.Open) return new[] { attention, pinDue }.Where(t => t.HasValue).Min();
        if (post.PortfolioRegistryVersion != State!.PortfolioSites.Version) return Now;
        if (_recoverWindows.Contains(post.Id)) return Now;
        if (!post.PromptDeliveredAt.HasValue && !post.AcceptedAt.HasValue) return Now;
        var dates = new List<DateTimeOffset>();
        if (AcknowledgementNoticeDue(post) is { } noticeDue) dates.Add(noticeDue);
        if (attention.HasValue) dates.Add(attention.Value);
        if (pinDue.HasValue) dates.Add(pinDue.Value);
        if (!post.Exempt && !post.Pinned)
        {
            if (!post.AcceptedAt.HasValue && post.Deadline.HasValue) dates.Add(post.Deadline.Value);
        }
        if (post.AcknowledgedAt.HasValue && !post.AcceptedAt.HasValue)
        {
            var next = ListingEligibilityPolicy.Evaluate(State!, post, Now, _settings.CooldownDays);
            if (next.BlockingListingId is null) dates.Add(next.NextAllowedAt ?? Now);
        }
        return dates.Count == 0 ? null : dates.Min();
    }

    public async Task RunDueAsync(CancellationToken ct)
    {
        if (State is null) return;
        if (_nextAudit <= Now || State.RecoveryFault?.IsDue(Now) == true ||
            State.RecoveryFault is null && State.Forums.Values.Any(f => f.Fault?.IsDue(Now) == true) ||
            _ready && (State.Forums.Values.Any(f => f.Reference.Fault?.IsDue(Now) == true) || State.RotateAt <= Now))
        {
            await SweepAsync(ct, retryHeld: false);
            return;
        }
        if (!_ready || !EvidenceCurrent) return;
        foreach (var post in State.Listings.Values.Where(p => ListingDue(p) <= Now).OrderBy(p => p.CreatedAt).Take(20).ToArray())
            await ProcessSafelyAsync(post, ct);
    }

    public void RequestRecovery() => _nextAudit = Now;

    public void InvalidateAccess(string? forum = null)
    {
        _accessInvalid = true;
        _feedReady = false;
        _ready = false;
        _discord.InvalidateCache();
        _publicRenders.Clear(); _staffRenders.Clear();
        if (State is null) return;
        State.RecoveryFault = null;
        State.Overview.Fault = null;
        foreach (var f in State.Forums.Values.Where(f => forum is null || f.ForumId == forum)) f.Fault = null;
        foreach (var f in State.Forums.Values.Where(f => forum is null || f.ForumId == forum)) f.Reference.Fault = null;
        foreach (var post in State.Listings.Values.Where(p => forum is null || p.ForumId == forum))
        { post.Fault = null; post.PublicPin.Fault = null; post.AcknowledgementNotice.Fault = null; }
        _nextAudit = Now;
        MarkEvidenceGap();
    }

    public async Task ThreadChangedAsync(string id, CancellationToken ct)
    {
        _discord.InvalidateThread(id);
        if (State?.Forums.Values.Any(f => f.Reference.ThreadId == id) == true) return;
        if (State is null || !_ready) return;
        if (!State.Listings.TryGetValue(id, out var post))
        {
            var live = await _discord.GetThreadAsync(id, ct);
            // ThreadCreated also signals access to an existing thread; it cannot certify reply coverage.
            if (live is not null) { ObserveReferenceCandidate(live); await ObserveAsync(live, false, ct); }
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
            forum.Reference = new ReferenceReceipt(); _verifiedReferences.Remove(forum.ForumId);
            _referenceCandidates.Remove(forum.ForumId); _ignoredThreads.Remove(id);
            RequestRecovery(); Save();
        }
        if (State?.Listings.GetValueOrDefault(id) is not { } post) return;
        RecordAbsence(post);
        if (_ready) await ReconcileThreadAsync(id, ct);
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
        { State.Overview.MessageId = null; State.Overview.Fault = null; _overviewRender = null; Save(); }
        foreach (var post in State.Listings.Values.Where(p =>
                     channel == _settings.FeedChannelId.ToString() && p.StaffMessageId is not null && ids.Contains(p.StaffMessageId) ||
                     channel == p.Id && p.PublicMessageId is not null && ids.Contains(p.PublicMessageId)).ToArray())
        {
            if (channel == post.Id) { post.PublicMessageId = null; _publicRenders.Remove(post.Id); }
            else { post.StaffMessageId = null; _staffRenders.Remove(post.Id); }
            post.PublicationPending = true; post.Fault = null; Save();
            if (_ready) await ProcessSafelyAsync(post, ct);
        }
    }

    private async Task ProcessSafelyAsync(Listing post, CancellationToken ct, bool verifyHeld = false)
    {
        if (post.Fault is { } fault && !fault.IsDue(Now))
        {
            if (!verifyHeld || fault.RetryAt is not null) return;
            // A permanent write hold must not hide a deletion during downtime. Probe once per bounded audit.
            try
            {
                var live = await ReadAsync(post, ct, refresh: true);
                if (live is null) RecordAbsence(post);
                else if (!live.OwnerIsBot && !live.OwnerIsStaff) return;
            }
            catch (Exception ex) when (ex is not System.IO.IOException && ex is not OperationCanceledException) { return; }
        }
        var previousFault = post.Fault;
        post.Fault = null;
        try
        {
            await ProcessAsync(post, ct);
            await PinResponseAsync(post, ct);
            if (previousFault is not null) Save();
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not System.IO.IOException)
        {
            _discord.InvalidateThread(post.Id);
            post.Fault = RecruitmentFault.From(ex, Now, previousFault);
            post.Hold = post.Fault.Message;
            if (!post.AcceptedAt.HasValue && post.Status == ListingStatus.Open) _recoverWindows.Add(post.Id);
            Save();
        }
    }
}
