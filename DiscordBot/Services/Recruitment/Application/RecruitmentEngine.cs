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
    private ListingWorkflow? _workflow;
    private ListingActionExecutor? _actions;
    private ListingPublisher? _publisher;
    private ListingReader? _reader;
    private ListingForums? _listingForums;
    private ListingOwnerCommands? _ownerCommands;
    private ListingQueries? _queries;
    private ListingReportCommands? _reportCommands;
    private ListingStaffCommands? _staffCommands;
    private PortfolioCommands? _portfolioCommands;
    public string Status { get; private set; } = "Not started";
    public RecruitmentState? State => _store?.State;
    private DateTimeOffset Now => _time.GetUtcNow();

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
            _ownerCommands = new ListingOwnerCommands(State!, _settings, _discord, _time, Save, _workflow, _actions,
                () => _ready, () => _ready && _feedReady && _feedGeneration == _evidenceGeneration(), () => EvidenceCurrent);
            _queries = new ListingQueries(State!, _settings, _discord, _time, _ownerCommands, () => EvidenceCurrent);
            _reportCommands = new ListingReportCommands(State!, _time, Save, _queries, _publisher, _workflow,
                () => _ready && _feedReady && _gatewayReady() && _feedGeneration == _evidenceGeneration());
            _staffCommands = new ListingStaffCommands(State!, _time, Save, _actions, _publisher, _recoverWindows,
                async token => { await DiscoverAsync(token); return _inventoryComplete; });
            _portfolioCommands = new PortfolioCommands(State!, _time, Save);
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

    public Task<string> AcknowledgeAsync(string guild, string thread, string owner, string code, CancellationToken ct, DateTimeOffset? receivedAt = null) =>
        _ownerCommands?.AcknowledgeAsync(guild, thread, owner, code, ct, receivedAt) ?? Task.FromResult("This control is unavailable or does not belong to you.");
    public Task<string> CloseOwnerAsync(string guild, string thread, string owner, CancellationToken ct) =>
        _ownerCommands?.CloseOwnerAsync(guild, thread, owner, ct) ?? Task.FromResult("This open listing does not belong to you or recruitment is unavailable.");
    public (string Message, string? Token) PrepareRemoval(string guild, string thread, string owner) =>
        _ownerCommands?.PrepareRemoval(guild, thread, owner) ?? ("This listing is unavailable or does not belong to you.", null);
    public Task<string> ConfirmRemovalAsync(string guild, string thread, string owner, string token, CancellationToken ct) =>
        _ownerCommands?.ConfirmRemovalAsync(guild, thread, owner, token, ct) ?? Task.FromResult("This confirmation does not belong to this listing and owner.");
    public Task<string> EligibilityAsync(string guild, string owner, CancellationToken ct) =>
        _queries?.EligibilityAsync(guild, owner, ct) ?? Task.FromResult("This control is only available to current members of the configured server.");
    public Task<string> DetailsAsync(string guild, string thread, string reader, CancellationToken ct) =>
        _queries?.DetailsAsync(guild, thread, reader, ct) ?? Task.FromResult("Listing details are unavailable in this server.");
    public Task<(string Message, bool Open, bool Removable)> OwnerDetailsAsync(string guild, string thread, string owner, CancellationToken ct) =>
        _queries?.OwnerDetailsAsync(guild, thread, owner, ct) ?? Task.FromResult(("This listing does not belong to you or recruitment is unavailable.", false, false));
    public Task<string> ReportAsync(string guild, string thread, string reporter, string concern, CancellationToken ct) =>
        _reportCommands?.ReportAsync(guild, thread, reporter, concern, ct) ?? Task.FromResult("This listing is unavailable or you are not a current server member.");
    public Task<string> StaffAsync(string guild, string actor, string operation, string target, string reason, CancellationToken ct) =>
        StaffAsync(guild, actor, RecruitmentCommands.Staff(operation), target, reason, ct);
    public async Task<string> StaffAsync(string guild, string actor, StaffCommand command, string target, string reason, CancellationToken ct)
    {
        var operation = command.Operation;
        if (guild != _bot.GuildId.ToString() || !await _discord.IsModeratorAsync(guild, actor, ct)) return "Moderator access is required in the configured server.";
        if (operation == StaffOperation.Status) return Status;
        if (operation == StaffOperation.Preview)
        {
            if (_assets is null) return "Assets are unavailable; inspect the startup error.";
            if (_assets.Templates.ContainsKey(target))
                return _assets.Render(target, "•••••", _settings) + "\n\nManaged tags: " + string.Join(", ", _assets.Tags[target].Select(t => t.Name));
            return string.Join("\n", _assets.Tags.Select(x => $"{x.Key}: {string.Join(", ", x.Value.Select(t => t.Name))}")) +
                "\nSelect a forum on /recruitment preview to read its proposed native Guidelines. Repair publishes these templates and the persisted rotating code.";
        }
        if (State is null || !_settings.Enabled) return "Recruitment is unavailable. Enable valid settings and restart first.";
        if (operation == StaffOperation.Lookup)
        {
            if (State.Listings.TryGetValue(target, out var found)) return _queries!.EvidenceDetails(found);
            if (State.Authors.TryGetValue(target, out var history)) return $"Author `{target}` · next creation: {history.LastAcceptedCreation?.AddDays(_settings.CooldownDays).ToString("u") ?? "none"} · confirmed timeout streak: {history.ConsecutiveTimeouts}";
            return "No retained recruitment record for that thread or author.";
        }
        if (string.IsNullOrWhiteSpace(reason)) return "A reason is required for this change.";
        reason = DiscordDisplayText.Literal(reason, 300);
        if (operation == StaffOperation.Repair)
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
        if (operation == StaffOperation.Reconcile)
        {
            State.Overview.Fault = null;
            _verifiedReferences.Clear();
            State.SetupAudit.Add(new RecruitmentAudit(Now, actor, "Reconcile: " + reason));
            if (State.SetupAudit.Count > 30) State.SetupAudit.RemoveAt(0);
            Save(); await SweepAsync(ct); return Status;
        }
        if (!_ready) return "Recruitment setup needs recovery before listing changes are available.";
        return await _staffCommands!.ExecuteAsync(actor, command, target, reason, ct);
    }

    public Task<string> PortfolioSitesAsync(string guild, string actor, string operation, string domain, string reason, CancellationToken ct) =>
        PortfolioSitesAsync(guild, actor, RecruitmentCommands.Portfolio(operation), domain, reason, ct);
    public async Task<string> PortfolioSitesAsync(string guild, string actor, PortfolioCommand command, string domain, string reason, CancellationToken ct)
    {
        if (guild != _bot.GuildId.ToString() || !await _discord.IsAdministratorAsync(guild, actor, ct))
            return "Administrator access is required in the configured server.";
        if (State is null || !_settings.Enabled) return "Recruitment is unavailable.";
        return _portfolioCommands!.Execute(actor, command, domain, reason);
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
