using DiscordBot.Services.Recruitment.Application;
using DiscordBot.Services.Recruitment.Infrastructure.Discord;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Policies;
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
    private readonly AcknowledgementRecovery _recoverWindows = new();
    private readonly Dictionary<string, RemovalConfirmation> _confirmations = new();
    private ListingWorkflow? _workflow;
    private ListingActionExecutor? _actions;
    private ListingPublisher? _publisher;
    private ListingReader? _reader;
    private ListingForums? _listingForums;
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
            _listingForums = new ListingForums(State!, _assets);
            _publisher = new ListingPublisher(State!, _settings, _discord, _time, Save, GuardFeedWrite);
            _reader = new ListingReader(State!, _settings, _discord, _time, Save, _recoverWindows);
            _actions = new ListingActionExecutor(State!, _settings, _discord, _time, Save, _recoverWindows, _reader, _publisher, _listingForums,
                () => EvidenceCurrent, _pendingThreadWork);
            var facts = new ListingEvidenceCapture(State!, _time, _publisher.QueueAttention);
            _workflow = new ListingWorkflow(State!, _settings, _discord, _time, Save, _recoverWindows, _reader, _publisher, _actions,
                facts, _listingForums, () => EvidenceCurrent, ObserveReferenceCandidate);
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
        _publisher?.ClearRenders();
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
                .Where(p => p.Status == ListingStatus.Open || p.PendingAction is not null || p.StaffSendPending || p.PublicSendPending || p.PublicationPending || _publisher!.AttentionDue(p) || _publisher!.PinDue(p))
                .OrderBy(p => ulong.Parse(p.Id) <= cursor).ThenBy(p => ulong.Parse(p.Id)).Take(10).ToArray();
            foreach (var post in batch)
            {
                ct.ThrowIfCancellationRequested();
                if (retryHeld) { post.Fault = null; post.PublicPin.Fault = null; }
                _discord.InvalidateFacts(post.Id);
                await _workflow!.ProcessSafelyAsync(post, ct, verifyHeld: true);
            }
            if (batch.Length > 0) State.SweepAfterId = batch[^1].Id;
            await ReconcileReferencesAsync(ct, retryHeld);
            RecruitmentRetention.Prune(State, Now, _settings.PostRetentionMonths, _settings.AuthorRetentionMonths, _settings.CooldownDays);
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
                State.RecoveryFault = RecruitmentFaultClassifier.From(ex, Now, previousFault);
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
        await _workflow!.ProcessSafelyAsync(post, ct);
        foreach (var waiting in State.Listings.Values.Where(p => p.Id != post.Id && p.OwnerId == post.OwnerId &&
                     p.Status == ListingStatus.Open && !p.AcceptedAt.HasValue).OrderBy(p => p.CreatedAt).ToArray())
            await _workflow!.ProcessSafelyAsync(waiting, ct);
    }

    public Task ObserveAsync(ThreadSnapshot live, bool observedFromCreation, CancellationToken ct) =>
        !_ready || _workflow is null ? Task.CompletedTask : _workflow.ObserveAsync(live, observedFromCreation, ct);

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
        var author = ListingTransitions.Author(State!, owner, Now);
        author.ConsecutiveTimeouts = 0; author.TimeoutAlerted = false;
        Save();
        _discord.InvalidateThread(post.Id);
        post.Fault = null;
        await _workflow!.ProcessAsync(post, ct);
        if (post.PendingAction is null && post.Status == ListingStatus.Open && !post.AcceptedAt.HasValue)
            await _workflow!.ProcessAsync(post, ct); // A recovered timeout intent may have just been cancelled.
        if (post.Status != ListingStatus.Open) return "This listing is no longer open. Use your listing controls to check its status.";
        return post.AcceptedAt.HasValue ? "Guidelines acknowledged. Your listing is accepted." : !EvidenceCurrent
            ? "Guidelines acknowledged. Acceptance is waiting for inventory recovery."
            : "Guidelines acknowledged. The existing listing or creation cooldown still blocks acceptance; see the advisory.";
    }

    public async Task<string> CloseOwnerAsync(string guild, string thread, string owner, CancellationToken ct)
    {
        var post = Owner(guild, thread, owner);
        if (post is null || post.Status != ListingStatus.Open) return "This open listing does not belong to you or recruitment is unavailable.";
        await _actions!.BeginActionAsync(post, RecruitmentActionKind.Close, owner, "Owner closed listing", "", false, ct);
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
            ? "Its creation cooldown remains " + DiscordDisplayText.Timestamp(next.Value) + "." : "The accepted creation cooldown has been waived.";
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
        await _actions!.BeginActionAsync(post, RecruitmentActionKind.Delete, owner, "Owner confirmed removal", "", false, ct);
        return "Post removed. Any accepted creation cooldown is unchanged.";
    }

    private Listing? Owner(string guild, string thread, string owner) =>
        _ready && State?.GuildId == guild && State.Listings.TryGetValue(thread, out var post) && post.OwnerId == owner ? post : null;

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
        reason = DiscordDisplayText.Literal(reason, 300);
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
            foreach (var p in posts) { _actions!.Audit(p, actor, "Waive cooldown: " + reason); Save(); await _publisher!.PublishStaffAsync(p, ct); }
            author.LastAcceptedCreation = null; author.UpdatedAt = Now; Save();
            return "Creation cooldown waived. Existing open listings still occupy the slot.";
        }
        if (!State.Listings.TryGetValue(target, out var post)) return "Managed thread was not found. Reconcile first.";
        _actions!.Audit(post, actor, operation + ": " + reason); Save(); await _publisher!.PublishStaffAsync(post, ct);
        switch (operation)
        {
            case "exempt": post.Exempt = true; break;
            case "unexempt": post.Exempt = false; _recoverWindows.Add(post.Id); break;
            case "resolve-reports":
                foreach (var report in post.Reports.Where(r => r.ResolvedAt is null)) report.ResolvedAt = Now;
                break;
            case "review-changes": post.ReviewedMaterialChanges = post.MaterialChanges; break;
            case "reset-attempts":
                var attemptHistory = ListingTransitions.Author(State, post.OwnerId, Now);
                attemptHistory.RecentCreations.Clear(); attemptHistory.AttemptPausedUntil = null;
                foreach (var attempt in State.Listings.Values.Where(p => p.OwnerId == post.OwnerId)) attempt.AttemptPausedUntil = null;
                break;
            case "reset-timeouts":
                var timeoutHistory = ListingTransitions.Author(State, post.OwnerId, Now);
                timeoutHistory.ConsecutiveTimeouts = 0; timeoutHistory.TimeoutAlerted = false; break;
            case "reset":
                if (post.AcceptedAt.HasValue || post.Status != ListingStatus.Open) return "Only an open unaccepted attempt can have its acknowledgement reset.";
                post.AcknowledgedAt = null; post.ValidCodes.Clear(); _recoverWindows.Add(post.Id);
                var history = ListingTransitions.Author(State, post.OwnerId, Now);
                history.ConsecutiveTimeouts = 0; history.TimeoutAlerted = false; break;
            case "reference":
                if (post.Status == ListingStatus.Deleted) return "A deleted thread cannot become a reference post.";
                post.Status = ListingStatus.Reference; post.PendingAction = null; post.Deadline = null; post.ValidCodes.Clear();
                post.LastOutcome = "Moderator-designated reference post"; post.Version++; Save();
                await _actions!.BeginActionAsync(post, RecruitmentActionKind.RemoveAdvisory, actor, reason, "", false, ct, requiresModerator: true);
                return "Reference post designated; bot advisory removed. Any accepted cooldown is preserved.";
            case "close":
                if (post.Status != ListingStatus.Open) return "Only an open listing can be closed.";
                await _actions!.BeginActionAsync(post, RecruitmentActionKind.Close, actor, reason, "", false, ct, requiresModerator: true); return post.Status == ListingStatus.Closed ? "Listing closed." : "Closure was cancelled or is awaiting reconciliation.";
            case "reopen":
                if (post.Status != ListingStatus.Closed) return "Only a closed listing can be reopened.";
                await DiscoverAsync(ct);
                if (!_inventoryComplete) return "Inventory is incomplete; reopening is held.";
                if (State.Listings.Values.Any(p => p.OwnerId == post.OwnerId && p.Id != post.Id && p.Status == ListingStatus.Open))
                    return "Another listing occupies this author's slot.";
                await _actions!.BeginActionAsync(post, RecruitmentActionKind.Reopen, actor, reason, "", false, ct, requiresModerator: true); return post.Status == ListingStatus.Open
                    ? "Listing reopened. Update this thread to preserve its conversation history." : "Reopening was cancelled or is awaiting reconciliation.";
            case "review-absent":
                if (!post.AvailabilityUnknown) return "Availability is known; use close or reconcile instead.";
                post.Status = ListingStatus.Deleted; post.AvailabilityUnknown = false; post.PendingAction = null;
                post.PublicSendPending = false; post.Hold = null;
                ListingTransitions.Author(State, post.OwnerId, Now);
                post.LastOutcome = "Staff confirmed absence"; break;
            default: return "Unknown recruitment operation.";
        }
        post.Version++; post.UpdatedAt = Now; Save();
        if (operation == "review-absent") await _publisher!.PublishStaffAsync(post, ct);
        else await _publisher!.PublishAsync(post, ct);
        return "Recruitment record updated.";
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
