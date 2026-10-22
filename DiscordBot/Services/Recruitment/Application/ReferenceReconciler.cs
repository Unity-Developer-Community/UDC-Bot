using DiscordBot.Services.Recruitment.Infrastructure.Discord;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed record ReferenceCandidate(string Id, bool BotOwned);
internal sealed class ReferenceReconciler(
    RecruitmentState state,
    RecruitmentAssets assets,
    IRecruitmentDiscord discord,
    TimeProvider time,
    Action save,
    Func<bool> evidenceCurrent,
    Action requestRetry)
{
    private readonly RecruitmentState State = state;
    private readonly RecruitmentAssets _assets = assets;
    private readonly IRecruitmentDiscord _discord = discord;
    private DateTimeOffset Now => time.GetUtcNow();
    private bool EvidenceCurrent => evidenceCurrent();
    private void Save() => save();
    public void Invalidate() { _verifiedReferences.Clear(); _referenceCandidates.Clear(); _ignoredThreads.Clear(); }
    public void ClearVerified() => _verifiedReferences.Clear();
    public void BeginInventory() => _unreadableReferenceForums.Clear();
    public IEnumerable<string> KnownIgnored(DateTimeOffset floor)
    {
        foreach (var id in _ignoredThreads.Where(p => p.Value < floor).Select(p => p.Key).ToArray()) _ignoredThreads.Remove(id);
        return _ignoredThreads.Keys;
    }
    public void Forget(string forum, string id)
    { _verifiedReferences.Remove(forum); _referenceCandidates.Remove(forum); _ignoredThreads.Remove(id); }
    private readonly HashSet<string> _verifiedReferences = new();
    private readonly Dictionary<string, ReferenceCandidate> _referenceCandidates = new();
    private readonly Dictionary<string, DateTimeOffset> _ignoredThreads = new();
    private readonly HashSet<string> _unreadableReferenceForums = new();
    private string ReferenceMarker(string forum) => $"recruitment:reference:{State!.InstanceId}:{forum}";

    public void ObserveReferenceCandidate(ThreadSnapshot live)
    {
        if (!live.OwnerIsBot && !live.OwnerIsStaff) return;
        if (live.OwnerIsBot && live.Starter is null)
        {
            _unreadableReferenceForums.Add(live.ForumId);
            return; // The missing starter could contain an interrupted create's marker; keep it discoverable.
        }
        // Retain ignored identities for this recovery pass so 50 staff/bot posts cannot starve member discovery.
        _ignoredThreads[live.Id] = live.CreatedAt;
        var owned = live.OwnerIsBot && live.Starter?.Contains(ReferenceMarker(live.ForumId), StringComparison.Ordinal) == true;
        if (owned || live.OwnerIsStaff && live.Pinned && !_referenceCandidates.ContainsKey(live.ForumId))
            _referenceCandidates[live.ForumId] = new ReferenceCandidate(live.Id, owned);
    }

    public async Task ReconcileAsync(CancellationToken ct, bool retryHeld)
    {
        foreach (var (purpose, forum) in State!.Forums)
        {
            var receipt = forum.Reference;
            if (_verifiedReferences.Contains(forum.ForumId) || !retryHeld && receipt.Fault is { } fault && !fault.IsDue(Now)) continue;
            // Reuse the normal bounded inventory; never create while discovery could still hide a prior send.
            if (!EvidenceCurrent) { requestRetry(); return; }
            try
            {
                await ReconcileForumAsync(purpose, forum, ct);
                receipt.Fault = null; _verifiedReferences.Add(forum.ForumId); Save();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not System.IO.IOException)
            {
                receipt.Fault = RecruitmentFaultClassifier.From(ex, Now, receipt.Fault);
                Save(); // Reference availability does not block otherwise valid member listings.
            }
        }
    }
    private async Task ReconcileForumAsync(string purpose, ForumReceipt forum, CancellationToken ct)
    {
        var receipt = forum.Reference;
        var marker = ReferenceMarker(forum.ForumId);
        if (receipt.ThreadId is not null && !await _discord.SecureReferenceAsync(forum.ForumId, receipt.ThreadId, receipt.BotOwned, marker, ct))
        {
            _referenceCandidates.Remove(forum.ForumId); _ignoredThreads.Remove(receipt.ThreadId);
            receipt.ThreadId = null; receipt.CreatePending = false; Save();
        }
        if (receipt.ThreadId is not null) return;
        var candidate = await DiscoverCandidateAsync(forum, ct);
        await AdoptOrCreateAsync(purpose, forum, marker, candidate, ct);
        if (!await _discord.SecureReferenceAsync(forum.ForumId, receipt.ThreadId!, receipt.BotOwned, marker, ct))
        {
            _ignoredThreads.Remove(receipt.ThreadId!); _referenceCandidates.Remove(forum.ForumId);
            receipt.ThreadId = null; Save();
            throw new TimeoutException("Reference disappeared before pinning; discovery will retry.");
        }
    }

    private async Task<ReferenceCandidate?> DiscoverCandidateAsync(ForumReceipt forum, CancellationToken ct)
    {
        var receipt = forum.Reference;
        // Normal inventory skips known listing IDs. Probe retained reference designations first.
        if (!_referenceCandidates.ContainsKey(forum.ForumId) && !receipt.CreatePending)
        {
            var retained = State.Listings.Values.Where(p => p.ForumId == forum.ForumId &&
                p.Status != ListingStatus.Deleted && (p.Status == ListingStatus.Reference || p.Pinned)).ToArray();
            foreach (var post in retained.Take(10))
            {
                if (await _discord.GetThreadAsync(post.Id, ct, refresh: true) is { } live) ObserveReferenceCandidate(live);
                if (_referenceCandidates.ContainsKey(forum.ForumId)) break;
            }
            if (retained.Length > 10 && !_referenceCandidates.ContainsKey(forum.ForumId))
                throw new InvalidOperationException("Reference discovery exceeded 10 retained candidates; review earlier reference designations.");
        }
        if (_unreadableReferenceForums.Contains(forum.ForumId))
            throw new TimeoutException("Reference discovery is waiting for a bot-owned starter message.");
        return _referenceCandidates.TryGetValue(forum.ForumId, out var candidate) && (!receipt.CreatePending || candidate.BotOwned)
            ? candidate : null;
    }

    private async Task AdoptOrCreateAsync(string purpose, ForumReceipt forum, string marker, ReferenceCandidate? candidate, CancellationToken ct)
    {
        var receipt = forum.Reference;
        if (candidate is not null)
        {
            receipt.ThreadId = candidate.Id; receipt.BotOwned = candidate.BotOwned; receipt.CreatePending = false; Save();
            return;
        }
        if (receipt.CreatePending && receipt.CreateStartedAt < State.DiscoveryFloor)
            throw new InvalidOperationException("Reference creation predates retained discovery; inspect the forum before retrying.");
        receipt.CreateStartedAt = Now; receipt.CreatePending = true; receipt.BotOwned = true; Save();
        var closed = forum.Tags[_assets.Tags[purpose].Single(t => t.Closing).Key].Id;
        receipt.ThreadId = await _discord.CreateReferenceAsync(forum.ForumId, closed, marker, ct);
        receipt.CreatePending = false; Save(); // Identity is durable before pin/lock writes.
    }
}
