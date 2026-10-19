using System.IO;
using DiscordBot.Settings;

namespace DiscordBot.Services.Recruitment;

/// <summary>All entry points are serialized by RecruitmentService, including staff and owner commands.</summary>
public sealed partial class RecruitmentEngine : IDisposable
{
    private readonly BotSettings _bot;
    private readonly RecruitmentSettings _settings;
    private readonly IRecruitmentDiscord _discord;
    private readonly TimeProvider _time;
    private readonly Func<string, bool> _pendingThreadWork;
    private RecruitmentStateStore? _store;
    private RecruitmentAssets? _assets;
    private RecruitmentSetup? _setup;
    private bool _ready;
    private bool _inventoryComplete;
    private readonly HashSet<string> _recoverWindows = new();
    private readonly Dictionary<string, RemovalConfirmation> _confirmations = new();
    public string Status { get; private set; } = "Not started";
    public RecruitmentState? State => _store?.State;
    private DateTimeOffset Now => _time.GetUtcNow();
    private sealed record RemovalConfirmation(string Guild, string Thread, string Owner, long Version, DateTimeOffset Expires);

    public RecruitmentEngine(BotSettings bot, IRecruitmentDiscord discord, TimeProvider? time = null, Func<string, bool>? pendingThreadWork = null, Func<long>? evidenceGeneration = null, Func<bool>? gatewayReady = null, Func<bool>? pendingIdentities = null)
    {
        (_bot, _settings, _discord, _time) = (bot, bot.Recruitment, discord, time ?? TimeProvider.System);
        _pendingThreadWork = pendingThreadWork ?? (_ => false);
        _evidenceGeneration = evidenceGeneration ?? (() => 0);
        _gatewayReady = gatewayReady ?? (() => true);
        _pendingIdentities = pendingIdentities ?? (() => false);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_settings.Enabled) { Status = "Disabled"; return; }
        Status = "Starting recruitment";
        var errors = _settings.Validate();
        if (errors.Count > 0) { Status = string.Join("; ", errors); return; }
        try
        {
            _assets = new RecruitmentAssets(_bot.AssetsRootPath, _settings);
            _store = new RecruitmentStateStore(_bot.ServerRootPath, _bot.GuildId.ToString(),
                _settings.Forums.Mapping().ToDictionary(x => x.Key, x => x.Value.ToString()), Now);
            _setup = new RecruitmentSetup(_store, _assets, _settings, _discord);
            _setup.Progress += () => Changed?.Invoke();
            MarkEvidenceGap();
            await SweepAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { Status = $"Recruitment stopped: {ex.GetType().Name}: {ex.Message}"; _ready = false; }
    }

    public void MarkEvidenceGap(bool invalidateAccess = true)
    {
        if (State is null) return;
        _inventoryComplete = false;
        _inventoryGeneration = -1;
        _accessInvalid = true;
        if (invalidateAccess) _feedReady = false;
        _discord.InvalidateCache();
        _verifiedReferences.Clear(); _referenceCandidates.Clear(); _ignoredThreads.Clear();
        foreach (var post in State.Listings.Values.Where(p => p.Status == ListingStatus.Open))
        {
            if (!post.AcceptedAt.HasValue) _recoverWindows.Add(post.Id);
        }
        Save();
    }

    public async Task SweepAsync(CancellationToken ct, bool retryHeld = true)
    {
        if (_store is null || _setup is null) return;
        var previousFault = State!.RecoveryFault;
        var auditDue = _nextAudit <= Now;
        var held = !retryHeld && State.RecoveryFault is { } recovery && !recovery.IsDue(Now);
        _nextAudit = Now.AddMinutes(15 + Random.Shared.NextDouble() * 3);
        _publicRenders.Clear(); _staffRenders.Clear();
        _discord.InvalidateCache();
        try
        {
            if (_accessInvalid || auditDue || retryHeld)
            {
                _feedReady = false;
                var feedGeneration = _evidenceGeneration();
                _feedEmbeds = await _discord.ValidateFeedAsync(ct);
                _feedGeneration = feedGeneration;
                _feedReady = true;
                if (!held) await _discord.ValidateAccessAsync(ct);
                _accessInvalid = false;
            }
            if (held) { Changed?.Invoke(); return; }
            State!.RecoveryFault = null;
            await _setup.ReconcileAsync(Now, false, ct, retryHeld);
            if (State!.Forums.Values.FirstOrDefault(f => f.Fault is not null) is { } failed)
            {
                _ready = false;
                Status = "Recruitment held: " + failed.Fault!.Message;
                Changed?.Invoke();
                return;
            }
            _ready = true;
            State.RecoveryFault = null;
            await DiscoverAsync(ct);
            if (retryHeld)
                foreach (var post in State.Listings.Values) { post.PublicPin.Fault = null; post.AcknowledgementNotice.Fault = null; }
            if (retryHeld)
                foreach (var notice in State.Listings.Values.SelectMany(p => p.Attention).Where(n => n.MessageId is null || n.SendPending)) notice.Fault = null;
            var cursor = ulong.TryParse(State.SweepAfterId, out var savedCursor) ? savedCursor : 0;
            var batch = State.Listings.Values
                .Where(p => p.Status == ListingStatus.Open || p.PendingAction is not null || p.StaffSendPending || p.PublicSendPending || p.PublicationPending || AttentionDue(p) || PinDue(p))
                .OrderBy(p => ulong.Parse(p.Id) <= cursor).ThenBy(p => ulong.Parse(p.Id)).Take(10).ToArray();
            foreach (var post in batch)
            {
                ct.ThrowIfCancellationRequested();
                if (retryHeld) { post.Fault = null; post.PublicPin.Fault = null; }
                _discord.InvalidateFacts(post.Id);
                await ProcessSafelyAsync(post, ct, verifyHeld: true);
            }
            if (batch.Length > 0) State.SweepAfterId = batch[^1].Id;
            await ReconcileReferencesAsync(ct, retryHeld);
            RecruitmentPolicy.Prune(State, Now, _settings.PostRetentionMonths, _settings.AuthorRetentionMonths, _settings.CooldownDays);
            if (EvidenceCurrent) State.LastSweepAt = Now;
            else _nextAudit = Now.AddSeconds(10);
            Save();
            Status = EvidenceCurrent ? $"Operating; {State.Listings.Count} retained listings; {State.Listings.Values.Count(p => p.Hold is not null)} held for recovery/review" : "Inventory incomplete; acceptance and automatic actions held";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ready = false;
            try { MarkEvidenceGap(invalidateAccess: false); } catch (IOException) { return; }
            if (State is not null)
            {
                State.RecoveryFault = RecruitmentFault.From(ex, Now, previousFault);
                try { Save(); } catch { return; }
            }
            Status = $"Recruitment held: {ex.GetType().Name}: {ex.Message}";
        }
        Changed?.Invoke();
    }

    private async Task DiscoverAsync(CancellationToken ct)
    {
        var state = State!;
        var generation = _evidenceGeneration();
        var cursors = state.Forums.Values.ToDictionary(f => f.ForumId, f =>
        {
            var cursor = f.ArchiveCursor ?? new ArchiveCursor(state.DiscoveryFloor);
            return f.Reference.CreatePending && f.Reference.CreateStartedAt is { } started && cursor.Since > started.AddMinutes(-5)
                ? new ArchiveCursor(started.AddMinutes(-5)) : cursor;
        });
        foreach (var id in _ignoredThreads.Where(p => p.Value < state.DiscoveryFloor).Select(p => p.Key).ToArray()) _ignoredThreads.Remove(id);
        var known = state.Listings.Keys.Concat(_ignoredThreads.Keys).Concat(state.Forums.Values.Select(f => f.Reference.ThreadId).OfType<string>()).ToHashSet();
        var inventory = await _discord.InventoryAsync(state.DiscoveryFloor, known, cursors, ct);
        _inventoryComplete = inventory.Complete;
        _inventoryGeneration = generation;
        _unreadableReferenceForums.Clear();
        foreach (var live in inventory.Threads.OrderBy(t => t.CreatedAt).ThenBy(t => ulong.Parse(t.Id)))
        {
            ObserveReferenceCandidate(live);
            await ObserveAsync(live, false, ct);
        }
        if (inventory.Cursors is not null)
            foreach (var forum in state.Forums.Values) forum.ArchiveCursor = inventory.Cursors[forum.ForumId];
        if (!_inventoryComplete)
            foreach (var p in state.Listings.Values.Where(p => p.Status == ListingStatus.Open && !p.AcceptedAt.HasValue)) _recoverWindows.Add(p.Id);
        Save();
    }

    public async Task ReconcileThreadAsync(string thread, CancellationToken ct)
    {
        if (!_ready || State?.Listings.GetValueOrDefault(thread) is not { } post) return;
        await ProcessSafelyAsync(post, ct);
        foreach (var waiting in State.Listings.Values.Where(p => p.Id != post.Id && p.OwnerId == post.OwnerId &&
                     p.Status == ListingStatus.Open && !p.AcceptedAt.HasValue).OrderBy(p => p.CreatedAt).ToArray())
            await ProcessSafelyAsync(waiting, ct);
    }

    public Task ObserveAsync(ThreadSnapshot live, bool observedFromCreation, CancellationToken ct)
    {
        if (State is null || !_ready || live.OwnerIsBot || live.OwnerIsStaff || !State.Forums.Values.Any(f => f.ForumId == live.ForumId)) return Task.CompletedTask;
        if (State.Listings.ContainsKey(live.Id) || live.CreatedAt < State.DiscoveryFloor) return Task.CompletedTask;
        var purpose = Purpose(live.ForumId);
        var post = new Listing
        {
            Id = live.Id, ForumId = live.ForumId, OwnerId = live.OwnerId, Title = RecruitmentPresentation.Safe(live.Title),
            CreatedAt = live.CreatedAt.ToUniversalTime(), UpdatedAt = Now, Pinned = live.Pinned,
            ResponseCoverageComplete = observedFromCreation,
            Facts = new InitialFacts
            {
                AccountCreatedAt = live.AccountCreatedAt, JoinedAt = live.JoinedAt,
                Payment = RecruitmentContent.Payment(live.Starter), MemberRoleCount = live.MemberRoleCount,
                Portfolio = RecruitmentContent.Portfolio(live.Starter),
                TagAdvice = _assets!.TagAdvice(purpose, live.Tags, State.Forums[purpose]),
                PriorAcceptedListing = State.Authors.GetValueOrDefault(live.OwnerId)?.LastAcceptedCreation is not null
            }
        };
        State.Listings[post.Id] = post;
        RefreshFacts(post, live);
        RecordCreation(post);
        Save();
        return Task.CompletedTask;
    }

    private async Task ProcessAsync(Listing post, CancellationToken ct)
    {
        if (post.Status == ListingStatus.Deleted)
        {
            if (post.PublicationPending || post.StaffSendPending || AttentionDue(post)) await PublishAsync(post, ct);
            return;
        }
        if (post.Status == ListingStatus.Reference)
        {
            if (post.PendingAction is not null) await ExecuteActionAsync(post, ct);
            return;
        }
        var live = await ReadAsync(post, ct);
        if (live is null)
        {
            // Unknown Channel proves absence, not who deleted it or when. Never invent a timeout or cooldown.
            RecordAbsence(post);
            await PublishStaffAsync(post, ct); post.PublicationPending = false; Save();
            return;
        }
        if (live.OwnerIsBot || live.OwnerIsStaff)
        {
            ObserveReferenceCandidate(live);
            // Retire records made by older versions without editing staff content or erasing accepted history.
            post.Status = ListingStatus.Reference; post.PendingAction = null; post.Deadline = null;
            post.Hold = null; post.Fault = null; post.ValidCodes.Clear(); _recoverWindows.Remove(post.Id);
            post.LastOutcome = "Staff or bot post; excluded from member recruitment"; post.Version++; Save();
            await BeginActionAsync(post, RecruitmentActionKind.RemoveAdvisory, "system", post.LastOutcome, "", false, ct);
            return;
        }
        post.Title = RecruitmentPresentation.Safe(live.Title);
        RefreshFacts(post, live);
        post.Facts.TagAdvice = _assets!.TagAdvice(Purpose(post.ForumId), live.Tags, State!.Forums[Purpose(post.ForumId)]);
        if (!post.PublicSendPending)
        {
            post.PublicRestoreArchive = post.PublicPin.RestoreArchive || live.Archived && post.AcceptedAt.HasValue;
            post.PublicRestoreLocked = post.PublicRestoreArchive && (post.PublicPin.RestoreArchive ? post.PublicPin.RestoreLocked : live.Locked);
        }
        post.Hold = null;
        if (live.Starter is null && !post.PromptDeliveredAt.HasValue && !post.AcceptedAt.HasValue)
        {
            post.Hold = "Starter message is not available yet";
            post.Fault = new RecruitmentFault(post.Hold, Now.AddSeconds(15));
            _discord.InvalidateThread(post.Id); Save(); return;
        }
        if (post.PendingAction is not null) { await ExecuteActionAsync(post, ct); return; }
        if (post.Status != ListingStatus.Open)
        {
            if (post.StaffSendPending || post.PublicSendPending || post.PublicationPending || AttentionDue(post)) await PublishAsync(post, ct);
            return;
        }
        if (EvidenceCurrent && !post.AcceptedAt.HasValue && !post.Exempt && !post.Pinned)
        {
            await RefreshBlockersAsync(post, ct);
            var eligibility = RecruitmentPolicy.Evaluate(State!, post, Now, _settings.CooldownDays);
            if (!eligibility.Allowed)
            {
                await BeginActionAsync(post, RecruitmentActionKind.Close, "system", EligibilityReason(eligibility), "ineligible", true, ct);
                return;
            }
        }
        if (_recoverWindows.Remove(post.Id))
        {
            post.PromptDeliveredAt = post.Deadline = null;
            // A timeout that was never sent must not run immediately after an outage.
            post.Version++;
        }
        var receipt = State!.Forums[Purpose(post.ForumId)];
        if (!post.AcceptedAt.HasValue && !post.ValidCodes.Contains(receipt.VisibleCode)) post.ValidCodes.Add(receipt.VisibleCode);
        Save();
        await PublishAsync(post, ct);
        if (!post.PromptDeliveredAt.HasValue && !post.AcceptedAt.HasValue)
        {
            post.PromptDeliveredAt = Now;
            post.Deadline = Now.AddMinutes(_settings.AcknowledgementMinutes);
            post.Version++; Save();
            await PublishAsync(post, ct);
        }
        await PublishAcknowledgementNoticeAsync(post, ct);
        if (EvidenceCurrent && post.AcknowledgedAt.HasValue && !post.AcceptedAt.HasValue)
            await RefreshBlockersAsync(post, ct);
        if (EvidenceCurrent && RecruitmentPolicy.TryAccept(State, post, Now, _settings.CooldownDays))
        { Save(); await PublishAsync(post, ct); }
        if (!EvidenceCurrent || post.Exempt || post.Pinned) return;
        if (live.Tags.Contains(ClosingTag(post)))
            await BeginActionAsync(post, RecruitmentActionKind.Close, "system", "Closed tag applied", "closed-tag", true, ct);
        else if (!post.AcceptedAt.HasValue && post.Deadline <= Now)
        {
            if (!post.AcknowledgedAt.HasValue)
                await BeginActionAsync(post, RecruitmentActionKind.Delete, "system", "Guidelines acknowledgement timed out", "timeout", true, ct);
            else
                await BeginActionAsync(post, RecruitmentActionKind.Close, "system", "Acknowledged but still ineligible at deadline", "ineligible", true, ct);
        }
    }

    private void RecordAbsence(Listing post)
    {
        post.Status = ListingStatus.Deleted; post.PendingAction = null; post.UpdatedAt = Now;
        post.PublicationPending = true; post.PublicSendPending = false; post.Hold = null; post.Fault = null;
        post.AvailabilityUnknown = false; post.Deadline = null; _recoverWindows.Remove(post.Id);
        post.LastOutcome = "Thread absent; deletion actor unknown"; post.Version++;
        RecruitmentPolicy.Author(State!, post.OwnerId, Now);
        Save();
    }

    private async Task RefreshBlockersAsync(Listing candidate, CancellationToken ct)
    {
        // Eligibility cannot depend on which half of a bounded sweep happened to run first.
        for (var count = 0; count < 10; count++)
        {
            var id = RecruitmentPolicy.Evaluate(State!, candidate, Now, _settings.CooldownDays).BlockingListingId;
            if (id is null) return;
            var blocker = State!.Listings[id];
            try
            {
                if (await ReadAsync(blocker, ct, refresh: true) is not null) return;
                RecordAbsence(blocker);
            }
            catch (Exception ex) when (ex is not IOException && ex is not OperationCanceledException)
            { blocker.Hold = "Author capacity could not be verified; existing slot retained"; Save(); return; }
        }
    }

    private async Task<ThreadSnapshot?> ReadAsync(Listing post, CancellationToken ct, bool refresh = false)
    {
        try
        {
            var live = await _discord.GetThreadAsync(post.Id, ct, refresh);
            if (live is not null && (live.OwnerId != post.OwnerId || live.ForumId != post.ForumId))
                throw new InvalidOperationException("Thread identity changed; staff review required.");
            post.AvailabilityUnknown = false;
            if (live is not null) post.Pinned = live.Pinned;
            return live;
        }
        catch { post.AvailabilityUnknown = true; Save(); throw; }
    }

    private async Task PublishStaffAsync(Listing post, CancellationToken ct)
    {
        await PublishAttentionAsync(post, ct);
        var message = RecruitmentPresentation.Staff(State!, post, _settings);
        if (!post.StaffSendPending && post.StaffMessageId is not null && _staffRenders.GetValueOrDefault(post.Id) == message.Content) return;
        var recovering = post.StaffSendPending;
        var delivery = await _discord.UpsertAsync(_settings.FeedChannelId.ToString(), post.StaffMessageId, recovering,
            post.StaffSendStartedAt ?? post.CreatedAt, message, () =>
            {
                GuardFeedWrite();
                if (!recovering) post.StaffSendStartedAt = Now;
                post.StaffSendPending = true; Save();
            }, ct);
        var changed = post.StaffMessageId != delivery.Id || post.StaffSendPending;
        post.StaffMessageId = delivery.Id; post.StaffSendPending = false;
        _staffRenders[post.Id] = message.Content;
        if (changed) Save();
    }

    private async Task PublishAsync(Listing post, CancellationToken ct)
    {
        await PublishStaffAsync(post, ct);
        if (post.Status is ListingStatus.Deleted or ListingStatus.Reference)
        { post.PublicationPending = false; post.PublicSendPending = false; Save(); return; }
        var message = RecruitmentPresentation.PublicV2(State!, post, _settings, Now);
        var renderKey = message.RenderKey + "\nPortfolio registry " + State!.PortfolioSites.Version;
        if (!post.PublicSendPending && !post.PublicationPending && post.PublicMessageId is not null && _publicRenders.GetValueOrDefault(post.Id) == renderKey) return;
        var recovering = post.PublicSendPending;
        var delivery = await _discord.UpsertAsync(post.Id, post.PublicMessageId, recovering, post.PublicSendStartedAt ?? post.CreatedAt,
            message with
            { RestoreArchive = post.PublicRestoreArchive || post.Status == ListingStatus.Closed, RestoreLocked = post.PublicRestoreLocked || post.Status == ListingStatus.Closed }, () =>
            {
                if (!recovering) post.PublicSendStartedAt = Now;
                post.PublicSendPending = true; Save();
            }, ct);
        var changed = post.PublicMessageId != delivery.Id || post.PublicSendPending || post.PublicationPending ||
            post.PublicMediaAttachmentId != delivery.MediaAttachmentId || post.PublicMediaFileName != delivery.MediaFileName;
        post.PublicMediaAttachmentId = delivery.MediaAttachmentId; post.PublicMediaFileName = delivery.MediaFileName;
        post.PublicMessageId = delivery.Id; post.PublicSendPending = false; post.PublicationPending = false;
        _publicRenders[post.Id] = renderKey;
        if (delivery.NewlySent && post.Deadline.HasValue && !post.AcceptedAt.HasValue)
        { post.PromptDeliveredAt = null; post.Deadline = null; post.Version++; changed = true; }
        if (changed) Save();
    }

    public async Task<string> AcknowledgeAsync(string guild, string thread, string owner, string code, CancellationToken ct, DateTimeOffset? receivedAt = null)
    {
        var post = Owner(guild, thread, owner);
        if (post is null) return "This control is unavailable or does not belong to you.";
        // Startup/recovery establishes cross-forum identity coverage; ordinary acknowledgements only
        // refresh the candidate and its blocking author records.
        if (!_ready || !_feedReady || _feedGeneration != _evidenceGeneration()) return "Recruitment is waiting for recovery. Please try again shortly.";
        if (post.Status != ListingStatus.Open || post.AcceptedAt.HasValue) return "This listing no longer needs acknowledgement.";
        var submittedAt = receivedAt ?? Now;
        if (!post.Deadline.HasValue || post.Deadline < submittedAt || post.PromptDeliveredAt > submittedAt)
            return "The acknowledgement window is unavailable or has expired.";
        if (post.LastCodeAttemptAt?.AddSeconds(5) > Now) return "Please wait a few seconds before trying again.";
        post.LastCodeAttemptAt = Now; Save();
        if (!post.ValidCodes.Contains(code.Trim().ToUpperInvariant())) return "That code does not match. Read the native Guidelines for this forum and try again.";
        // Persist a timely valid submission before any processing can execute an overdue timeout.
        // Invalid/late submissions never extend the window or fence the deadline worker.
        post.AcknowledgedAt = submittedAt; post.Version++; post.UpdatedAt = Now;
        var author = RecruitmentPolicy.Author(State!, owner, Now);
        author.ConsecutiveTimeouts = 0; author.TimeoutAlerted = false;
        Save();
        _discord.InvalidateThread(post.Id);
        post.Fault = null;
        await ProcessAsync(post, ct);
        if (post.PendingAction is null && post.Status == ListingStatus.Open && !post.AcceptedAt.HasValue)
            await ProcessAsync(post, ct); // A recovered timeout intent may have just been cancelled.
        if (post.Status != ListingStatus.Open) return "This listing is no longer open. Use your listing controls to check its status.";
        return post.AcceptedAt.HasValue ? "Guidelines acknowledged. Your listing is accepted." : !EvidenceCurrent
            ? "Guidelines acknowledged. Acceptance is waiting for inventory recovery."
            : "Guidelines acknowledged. The existing listing or creation cooldown still blocks acceptance; see the advisory.";
    }

    public async Task<string> CloseOwnerAsync(string guild, string thread, string owner, CancellationToken ct)
    {
        var post = Owner(guild, thread, owner);
        if (post is null || post.Status != ListingStatus.Open) return "This open listing does not belong to you or recruitment is unavailable.";
        await BeginActionAsync(post, RecruitmentActionKind.Close, owner, "Owner closed listing", "", false, ct);
        return "Listing closed. Its accepted creation cooldown is unchanged.";
    }

    public (string Message, string? Token) PrepareRemoval(string guild, string thread, string owner)
    {
        var post = Owner(guild, thread, owner);
        if (post is null || post.Status is ListingStatus.Deleted or ListingStatus.Reference) return ("This listing is unavailable or does not belong to you.", null);
        foreach (var key in _confirmations.Where(x => x.Value.Expires <= Now || x.Value.Owner == owner).Select(x => x.Key).ToArray()) _confirmations.Remove(key);
        var token = Guid.NewGuid().ToString("N");
        _confirmations[token] = new RemovalConfirmation(guild, thread, owner, post.Version, Now.AddMinutes(2));
        var next = State!.Authors.GetValueOrDefault(owner)?.LastAcceptedCreation?.AddDays(_settings.CooldownDays);
        var timing = !post.AcceptedAt.HasValue ? "Removing this unaccepted attempt adds no accepted-listing cooldown; its creation still counts toward the attempt limit." : next.HasValue
            ? "Its creation cooldown remains " + RecruitmentPresentation.Timestamp(next.Value) + "." : "The accepted creation cooldown has been waived.";
        return ($"Permanently remove this forum post? {timing} This confirmation expires in two minutes.", token);
    }

    public async Task<string> ConfirmRemovalAsync(string guild, string thread, string owner, string token, CancellationToken ct)
    {
        if (!_confirmations.TryGetValue(token, out var confirm) || confirm.Guild != guild || confirm.Thread != thread || confirm.Owner != owner)
            return "This confirmation does not belong to this listing and owner.";
        _confirmations.Remove(token);
        var post = Owner(guild, thread, owner);
        if (post is null || confirm.Expires <= Now || confirm.Version != post.Version || post.Status is ListingStatus.Deleted or ListingStatus.Reference)
            return "This confirmation expired or the listing changed. Request removal again.";
        await BeginActionAsync(post, RecruitmentActionKind.Delete, owner, "Owner confirmed removal", "", false, ct);
        return "Post removed. Any accepted creation cooldown is unchanged.";
    }

    private Listing? Owner(string guild, string thread, string owner) =>
        _ready && State?.GuildId == guild && State.Listings.TryGetValue(thread, out var post) && post.OwnerId == owner ? post : null;

    private async Task BeginActionAsync(Listing post, RecruitmentActionKind kind, string actor, string reason, string condition, bool automatic, CancellationToken ct, bool requiresModerator = false)
    {
        if (post.PendingAction is not null) throw new InvalidOperationException("An earlier action still needs reconciliation.");
        post.PendingAction = new RecruitmentAction { Kind = kind, ActorId = actor, Reason = reason, Condition = condition,
            Automatic = automatic, RequiresModerator = requiresModerator, GuidelineTimeout = condition == "timeout", CreatedAt = Now };
        if (!automatic) Audit(post, actor, reason);
        Save();
        await ExecuteActionAsync(post, ct);
    }

    private async Task ExecuteActionAsync(Listing post, CancellationToken ct)
    {
        var action = post.PendingAction!;
        if (action.Automatic && action.Condition == "unanswered")
        {
            // Cancel unsent legacy expiry; preserve a closure already applied before the upgrade.
            var previous = action.CallStarted ? await ReadAsync(post, ct, refresh: true) : null;
            if (action.CallStarted && previous is null) { RecordAbsence(post); return; }
            var alreadyClosed = action.CallStarted && previous is { Locked: true, Archived: true };
            if (alreadyClosed) post.Status = ListingStatus.Closed;
            post.PendingAction = null; post.PublicationPending = true;
            post.LastOutcome = alreadyClosed ? "Closure applied before upgrade confirmed" : "Automatic expiry cancelled; listing remains open";
            Save(); return;
        }
        if (action.RequiresModerator && !await _discord.IsModeratorAsync(State!.GuildId, action.ActorId, ct))
        {
            post.PendingAction = null; post.LastOutcome = "Staff action cancelled: moderator access no longer present";
            post.PublicationPending = true; Save(); await PublishStaffAsync(post, ct); return;
        }
        // Intent and staff evidence must exist before removing or changing member content.
        await PublishStaffAsync(post, ct);
        var live = await ReadAsync(post, ct, refresh: true);
        if (live is null)
        {
            RecordAbsence(post);
            await PublishStaffAsync(post, ct); post.PublicationPending = false; Save(); return;
        }
        if (action.Kind == RecruitmentActionKind.RemoveAdvisory)
        {
            if (post.PublicMessageId is null && post.PublicSendPending)
            {
                // Resolve a possibly delivered provisional advisory before removing it.
                var recovered = await _discord.UpsertAsync(post.Id, null, true, post.PublicSendStartedAt ?? post.CreatedAt,
                    RecruitmentPresentation.PublicV2(State!, post, _settings, Now), Save, ct);
                post.PublicMessageId = recovered.Id; post.PublicSendPending = false; Save();
            }
            if (post.PublicMessageId is not null) await _discord.DeleteAdvisoryAsync(post.Id, post.PublicMessageId, ct, $"recruitment:{post.Id}:public");
            post.PublicMessageId = null; post.PublicMediaAttachmentId = null; post.PublicMediaFileName = null; post.PendingAction = null; Save(); return;
        }
        if (action.CallStarted && action.Kind == RecruitmentActionKind.Close && live.Archived && live.Locked)
        {
            post.Status = ListingStatus.Closed; post.PendingAction = null;
            post.LastOutcome = "Closure confirmed after interrupted action"; post.UpdatedAt = Now; post.Version++;
            post.PublicationPending = true; Save(); await PublishAsync(post, ct); return;
        }
        if (action.Automatic && (!EvidenceCurrent || post.Exempt || post.Pinned || post.Status != ListingStatus.Open ||
            (action.Condition == "timeout" && (post.AcknowledgedAt.HasValue || post.AcceptedAt.HasValue || post.Deadline > Now || _recoverWindows.Contains(post.Id))) ||
            (action.Condition == "ineligible" && (post.AcceptedAt.HasValue || RecruitmentPolicy.Evaluate(State!, post, Now, _settings.CooldownDays).Allowed)) ||
            (action.Condition == "closed-tag" && !live.Tags.Contains(ClosingTag(post)))))
        { post.PendingAction = null; Save(); return; }
        if (action.Kind == RecruitmentActionKind.Reopen && State!.Listings.Values.Any(p => p.OwnerId == post.OwnerId && p.Id != post.Id && p.Status == ListingStatus.Open))
            throw new InvalidOperationException("Another listing occupies this author's slot.");
        // Gateway facts and timely owner interactions already queued must be applied before an automatic action.
        if (action.Automatic && (!EvidenceCurrent || _pendingThreadWork(post.Id)))
        { post.PendingAction = null; Save(); return; }
        // The pending action is already visible to staff. Do not append an automatic-action audit
        // for a request cancelled by newer evidence before it reaches the write boundary.
        if (action.Automatic && !action.CallStarted) Audit(post, action.ActorId, action.Reason);
        action.CallStarted = true; Save();
        switch (action.Kind)
        {
            case RecruitmentActionKind.Delete:
                await _discord.DeleteAsync(live, action.Reason, action.Automatic, ct, () => !action.Automatic || EvidenceCurrent && !_pendingThreadWork(post.Id));
                post.Status = ListingStatus.Deleted;
                if (action.GuidelineTimeout)
                {
                    var author = RecruitmentPolicy.Author(State!, post.OwnerId, Now);
                    author.ConsecutiveTimeouts++;
                    if (author.ConsecutiveTimeouts >= 3 && !author.TimeoutAlerted)
                    { post.TimeoutAlertCount = author.ConsecutiveTimeouts; author.TimeoutAlerted = true; QueueAttention(post, "Three consecutive confirmed Guidelines timeouts"); }
                }
                break;
            case RecruitmentActionKind.Close:
                await _discord.CloseAsync(live, ClosingTag(post), action.Automatic, ct, () => !action.Automatic || EvidenceCurrent && !_pendingThreadWork(post.Id));
                post.Status = ListingStatus.Closed;
                break;
            case RecruitmentActionKind.Reopen:
                await _discord.ReopenAsync(live, ClosingTag(post), ct);
                post.Status = ListingStatus.Open;
                if (!post.AcceptedAt.HasValue) _recoverWindows.Add(post.Id);
                break;
        }
        RecruitmentPolicy.Author(State!, post.OwnerId, Now);
        post.LastOutcome = action.Reason; post.PendingAction = null; post.UpdatedAt = Now; post.Version++; post.PublicationPending = true; Save();
        await PublishAsync(post, ct);
    }

    public async Task<string> StaffAsync(string guild, string actor, string operation, string target, string reason, CancellationToken ct)
    {
        if (guild != _bot.GuildId.ToString() || !await _discord.IsModeratorAsync(guild, actor, ct)) return "Moderator access is required in the configured server.";
        if (operation == "status") return Status;
        if (operation == "preview")
        {
            if (_assets is null) return "Assets are unavailable; inspect the startup error.";
            if (_assets.Templates.ContainsKey(target))
                return _assets.Render(target, "•••••", _settings) + "\n\nManaged tags: " + string.Join(", ", _assets.Tags[target].Select(t => t.Name));
            return string.Join("\n", _assets.Tags.Select(x => $"{x.Key}: {string.Join(", ", x.Value.Select(t => t.Name))}")) +
                "\nSelect a forum on /recruitment preview to read its proposed native Guidelines. Repair publishes these templates and the persisted rotating code.";
        }
        if (State is null || !_settings.Enabled) return "Recruitment is unavailable. Enable valid settings and restart first.";
        if (operation == "lookup")
        {
            if (State.Listings.TryGetValue(target, out var found)) return EvidenceDetails(found);
            if (State.Authors.TryGetValue(target, out var history)) return $"Author `{target}` · next creation: {history.LastAcceptedCreation?.AddDays(_settings.CooldownDays).ToString("u") ?? "none"} · confirmed timeout streak: {history.ConsecutiveTimeouts}";
            return "No retained recruitment record for that thread or author.";
        }
        if (string.IsNullOrWhiteSpace(reason)) return "A reason is required for this change.";
        reason = RecruitmentPresentation.Safe(reason, 300);
        if (operation == "repair")
        {
            if (target.Length > 0 && !State.Forums.ContainsKey(target)) return "Unknown recruitment forum purpose.";
            State.Overview.Fault = null;
            _verifiedReferences.Clear();
            State.SetupAudit.Add(new RecruitmentAudit(Now, actor, "Repair forum setup: " + reason));
            if (State.SetupAudit.Count > 30) State.SetupAudit.RemoveAt(0);
            Save();
            await _discord.ValidateAccessAsync(ct);
            await _setup!.ReconcileAsync(Now, true, ct, target: string.IsNullOrEmpty(target) ? null : target);
            Status = "Setup repaired";
            await SweepAsync(ct); return Status;
        }
        if (operation == "reconcile")
        {
            State.Overview.Fault = null;
            _verifiedReferences.Clear();
            State.SetupAudit.Add(new RecruitmentAudit(Now, actor, "Reconcile: " + reason));
            if (State.SetupAudit.Count > 30) State.SetupAudit.RemoveAt(0);
            Save(); await SweepAsync(ct); return Status;
        }
        if (!_ready) return "Recruitment setup needs recovery before listing changes are available.";
        if (operation == "waive-cooldown")
        {
            if (!State.Authors.TryGetValue(target, out var author)) return "Author history was not found.";
            // Audit on every retained listing for this author; waiver never changes occupied capacity.
            var posts = State.Listings.Values.Where(p => p.OwnerId == target).ToArray();
            if (posts.Length == 0) return "No listing audit record remains; no active cooldown should need a waiver.";
            foreach (var p in posts) { Audit(p, actor, "Waive cooldown: " + reason); Save(); await PublishStaffAsync(p, ct); }
            author.LastAcceptedCreation = null; author.UpdatedAt = Now; Save();
            return "Creation cooldown waived. Existing open listings still occupy the slot.";
        }
        if (!State.Listings.TryGetValue(target, out var post)) return "Managed thread was not found. Reconcile first.";
        Audit(post, actor, operation + ": " + reason); Save(); await PublishStaffAsync(post, ct);
        switch (operation)
        {
            case "exempt": post.Exempt = true; break;
            case "unexempt": post.Exempt = false; _recoverWindows.Add(post.Id); break;
            case "resolve-reports":
                foreach (var report in post.Reports.Where(r => r.ResolvedAt is null)) report.ResolvedAt = Now;
                break;
            case "review-changes": post.ReviewedMaterialChanges = post.MaterialChanges; break;
            case "reset-attempts":
                var attemptHistory = RecruitmentPolicy.Author(State, post.OwnerId, Now);
                attemptHistory.RecentCreations.Clear(); attemptHistory.AttemptPausedUntil = null;
                foreach (var attempt in State.Listings.Values.Where(p => p.OwnerId == post.OwnerId)) attempt.AttemptPausedUntil = null;
                break;
            case "reset-timeouts":
                var timeoutHistory = RecruitmentPolicy.Author(State, post.OwnerId, Now);
                timeoutHistory.ConsecutiveTimeouts = 0; timeoutHistory.TimeoutAlerted = false; break;
            case "reset":
                if (post.AcceptedAt.HasValue || post.Status != ListingStatus.Open) return "Only an open unaccepted attempt can have its acknowledgement reset.";
                post.AcknowledgedAt = null; post.ValidCodes.Clear(); _recoverWindows.Add(post.Id);
                var history = RecruitmentPolicy.Author(State, post.OwnerId, Now);
                history.ConsecutiveTimeouts = 0; history.TimeoutAlerted = false; break;
            case "reference":
                if (post.Status == ListingStatus.Deleted) return "A deleted thread cannot become a reference post.";
                post.Status = ListingStatus.Reference; post.PendingAction = null; post.Deadline = null; post.ValidCodes.Clear();
                post.LastOutcome = "Moderator-designated reference post"; post.Version++; Save();
                await BeginActionAsync(post, RecruitmentActionKind.RemoveAdvisory, actor, reason, "", false, ct, requiresModerator: true);
                return "Reference post designated; bot advisory removed. Any accepted cooldown is preserved.";
            case "close":
                if (post.Status != ListingStatus.Open) return "Only an open listing can be closed.";
                await BeginActionAsync(post, RecruitmentActionKind.Close, actor, reason, "", false, ct, requiresModerator: true); return post.Status == ListingStatus.Closed ? "Listing closed." : "Closure was cancelled or is awaiting reconciliation.";
            case "reopen":
                if (post.Status != ListingStatus.Closed) return "Only a closed listing can be reopened.";
                await DiscoverAsync(ct);
                if (!_inventoryComplete) return "Inventory is incomplete; reopening is held.";
                if (State.Listings.Values.Any(p => p.OwnerId == post.OwnerId && p.Id != post.Id && p.Status == ListingStatus.Open))
                    return "Another listing occupies this author's slot.";
                await BeginActionAsync(post, RecruitmentActionKind.Reopen, actor, reason, "", false, ct, requiresModerator: true); return post.Status == ListingStatus.Open
                    ? "Listing reopened. Update this thread to preserve its conversation history." : "Reopening was cancelled or is awaiting reconciliation.";
            case "review-absent":
                if (!post.AvailabilityUnknown) return "Availability is known; use close or reconcile instead.";
                post.Status = ListingStatus.Deleted; post.AvailabilityUnknown = false; post.PendingAction = null;
                post.PublicSendPending = false; post.Hold = null;
                RecruitmentPolicy.Author(State, post.OwnerId, Now);
                post.LastOutcome = "Staff confirmed absence"; break;
            default: return "Unknown recruitment operation.";
        }
        post.Version++; post.UpdatedAt = Now; Save();
        if (operation == "review-absent") await PublishStaffAsync(post, ct);
        else await PublishAsync(post, ct);
        return "Recruitment record updated.";
    }

    private void Audit(Listing post, string actor, string reason)
    {
        post.LastActor = actor; post.LastReason = reason;
        post.Audit.Add(new RecruitmentAudit(Now, actor, reason));
        if (post.Audit.Count > 30) post.Audit.RemoveRange(0, post.Audit.Count - 30);
    }
    private string Purpose(string forum) => State!.Forums.Single(x => x.Value.ForumId == forum).Key;
    private string? ClosingTag(Listing post)
    {
        var purpose = Purpose(post.ForumId);
        return State!.Forums[purpose].Tags.GetValueOrDefault(_assets!.Tags[purpose].Single(t => t.Closing).Key)?.Id;
    }
    private void Save()
    {
        try { _store!.Save(); Changed?.Invoke(); }
        catch
        {
            _ready = false;
            Status = "Recruitment stopped after a state write failure; restore storage and restart before making changes.";
            throw;
        }
    }
    public void Dispose() => _store?.Dispose();
}
