using DiscordBot.Services.Recruitment.Domain.Policies;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed class InventoryRecovery(
    RecruitmentState state,
    IRecruitmentDiscord discord,
    AcknowledgementRecovery windows,
    ReferenceReconciler references,
    ListingWorkflow workflow,
    Func<long> currentGeneration,
    Action save)
{
    private readonly RecruitmentState State = state;
    private readonly IRecruitmentDiscord _discord = discord;
    private readonly AcknowledgementRecovery _recoverWindows = windows;
    private void Save() => save();
    public bool Complete { get; private set; }
    public long Generation { get; private set; } = -1;
    public void Invalidate() { Complete = false; Generation = -1; }
    public async Task DiscoverAsync(CancellationToken ct)
    {
        var state = State!;
        var generation = currentGeneration();
        var cursors = state.Forums.Values.ToDictionary(f => f.ForumId, f =>
        {
            var cursor = f.ArchiveCursor ?? new ArchiveCursor(state.DiscoveryFloor);
            return f.Reference.CreatePending && f.Reference.CreateStartedAt is { } started && cursor.Since > started.AddMinutes(-5)
                ? new ArchiveCursor(started.AddMinutes(-5)) : cursor;
        });
        var known = state.Listings.Keys.Concat(references.KnownIgnored(state.DiscoveryFloor))
            .Concat(state.Forums.Values.Select(f => f.Reference.ThreadId).OfType<string>()).ToHashSet();
        var inventory = await _discord.InventoryAsync(state.DiscoveryFloor, known, cursors, ct);
        Complete = inventory.Complete;
        Generation = generation;
        references.BeginInventory();
        foreach (var live in inventory.Threads.OrderBy(t => t.CreatedAt).ThenBy(t => ulong.Parse(t.Id)))
        {
            references.ObserveReferenceCandidate(live);
            await workflow.ObserveAsync(live, false, ct);
        }
        if (inventory.Cursors is not null)
            foreach (var forum in state.Forums.Values) forum.ArchiveCursor = inventory.Cursors[forum.ForumId];
        if (!Complete)
            foreach (var p in state.Listings.Values.Where(p => p.Status == ListingStatus.Open && !p.AcceptedAt.HasValue)) _recoverWindows.Add(p.Id);
        Save();
    }

}
