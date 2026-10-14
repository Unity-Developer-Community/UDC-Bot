namespace DiscordBot.Services.Recruitment;

public sealed partial class RecruitmentEngine
{
    private readonly HashSet<string> _verifiedReferences = new();
    private readonly Dictionary<string, (string Id, bool BotOwned)> _referenceCandidates = new();
    private readonly Dictionary<string, DateTimeOffset> _ignoredThreads = new();
    private readonly HashSet<string> _unreadableReferenceForums = new();
    private string ReferenceMarker(string forum) => $"recruitment:reference:{State!.InstanceId}:{forum}";

    private void ObserveReferenceCandidate(ThreadSnapshot live)
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
            _referenceCandidates[live.ForumId] = (live.Id, owned);
    }

    private async Task ReconcileReferencesAsync(CancellationToken ct, bool retryHeld)
    {
        foreach (var (purpose, forum) in State!.Forums)
        {
            var receipt = forum.Reference;
            if (_verifiedReferences.Contains(forum.ForumId) || !retryHeld && receipt.Fault is { } fault && !fault.IsDue(Now)) continue;
            // Reuse the normal bounded inventory; never create while discovery could still hide a prior send.
            if (!EvidenceCurrent) { _nextAudit = Now.AddSeconds(10); return; }
            try
            {
                var marker = ReferenceMarker(forum.ForumId);
                if (receipt.ThreadId is not null && !await _discord.SecureReferenceAsync(forum.ForumId, receipt.ThreadId, receipt.BotOwned, marker, ct))
                {
                    _referenceCandidates.Remove(forum.ForumId); _ignoredThreads.Remove(receipt.ThreadId);
                    receipt.ThreadId = null; receipt.CreatePending = false; Save();
                }
                if (receipt.ThreadId is null)
                {
                    // Older versions retained designated references as listings. Normal inventory skips
                    // those known IDs; check them before creating another pinned post.
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
                    if (_referenceCandidates.TryGetValue(forum.ForumId, out var candidate) && (!receipt.CreatePending || candidate.BotOwned))
                    {
                        receipt.ThreadId = candidate.Id; receipt.BotOwned = candidate.BotOwned; receipt.CreatePending = false; Save();
                    }
                    else
                    {
                        if (receipt.CreatePending && receipt.CreateStartedAt < State.DiscoveryFloor)
                            throw new InvalidOperationException("Reference creation predates retained discovery; inspect the forum before retrying.");
                        receipt.CreateStartedAt = Now; receipt.CreatePending = true; receipt.BotOwned = true; Save();
                        var closed = forum.Tags[_assets!.Tags[purpose].Single(t => t.Closing).Key].Id;
                        receipt.ThreadId = await _discord.CreateReferenceAsync(forum.ForumId, closed, marker, ct);
                        receipt.CreatePending = false; Save(); // Identity is durable before pin/lock writes.
                    }
                    if (!await _discord.SecureReferenceAsync(forum.ForumId, receipt.ThreadId, receipt.BotOwned, marker, ct))
                    {
                        _ignoredThreads.Remove(receipt.ThreadId); _referenceCandidates.Remove(forum.ForumId);
                        receipt.ThreadId = null; Save();
                        throw new TimeoutException("Reference disappeared before pinning; discovery will retry.");
                    }
                }
                receipt.Fault = null; _verifiedReferences.Add(forum.ForumId); Save();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not System.IO.IOException)
            {
                receipt.Fault = RecruitmentFault.From(ex, Now, receipt.Fault);
                Save(); // Reference availability does not block otherwise valid member listings.
            }
        }
    }
}
