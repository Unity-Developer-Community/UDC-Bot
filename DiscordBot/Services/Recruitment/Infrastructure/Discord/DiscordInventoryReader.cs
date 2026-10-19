using Discord.Rest;
using DiscordBot.Settings;
using static DiscordBot.Services.Recruitment.RecruitmentRequests;

namespace DiscordBot.Services.Recruitment.Infrastructure.Discord;

internal sealed class DiscordInventoryReader(BotSettings settings, TimeProvider time,
    Func<string, CancellationToken, Task<RestForumChannel>> forum,
    Func<RestThreadChannel, CancellationToken, Task<ThreadSnapshot>> readSnapshot,
    Func<string, CancellationToken, Task<ThreadSnapshot?>> readThread)
{
    public async Task<ThreadInventory> ReadAsync(DateTimeOffset floor, IReadOnlySet<string> knownIds,
        IReadOnlyDictionary<string, ArchiveCursor> cursors, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        var found = new Dictionary<ulong, RestThreadChannel?>();
        var progress = new Dictionary<string, ArchiveCursor>();
        var complete = true;
        var firstId = settings.Recruitment.Forums.Mapping().Values.First().ToString();
        var firstForum = await forum(firstId, ct);
        using (var active = DiscordCompatibilityTransport.ObserveActiveInventory(settings.GuildId))
        {
            foreach (var thread in await firstForum.GetActiveThreadsAsync(Options(ct))) found[thread.Id] = thread;
            if (active.Identities is null) throw new InvalidOperationException("Active-thread inventory response was not observed; acceptance is held.");
            foreach (var identity in active.Identities.Where(t => settings.Recruitment.Forums.Contains(t.Parent)))
                found.TryAdd(identity.Id, null);
        }
        foreach (var id in settings.Recruitment.Forums.Mapping().Values)
        {
            var currentForum = await forum(id.ToString(), ct);
            var cursor = cursors[id.ToString()];
            var started = cursor.StartedAt ?? time.GetUtcNow();
            var since = cursor.Since > floor ? cursor.Since : floor;
            var before = cursor.Before;
            var finished = false;
            // Resume bounded archive pages across sweeps. A completed pass advances its lower boundary.
            for (var page = 0; page < 2; page++)
            {
                var batch = await currentForum.GetPublicArchivedThreadsAsync(100, before, Options(ct));
                foreach (var thread in batch.Where(t => t.CreatedAt >= floor)) found[thread.Id] = thread;
                if (batch.Count < 100 || batch.Min(t => t.ArchiveTimestamp) < since) { finished = true; break; }
                before = batch.Min(t => t.ArchiveTimestamp);
            }
            complete &= finished;
            // An overlap protects against timestamp precision and threads archiving during the scan.
            progress[id.ToString()] = finished ? new ArchiveCursor(started.AddMinutes(-5)) : new ArchiveCursor(since, before, started);
        }
        var snapshots = new List<ThreadSnapshot>();
        var unseen = found.Where(t => SnowflakeUtils.FromSnowflake(t.Key) >= floor && !knownIds.Contains(t.Key.ToString())).ToArray();
        foreach (var thread in unseen.Take(50))
        {
            var snapshot = thread.Value is not null ? await readSnapshot(thread.Value, ct) : await readThread(thread.Key.ToString(), ct);
            if (snapshot is not null) snapshots.Add(snapshot);
        }
        // Do not advance past an archive page until all its unknown identities have been retained.
        return unseen.Length > 50 ? new ThreadInventory(snapshots, false, cursors) : new ThreadInventory(snapshots, complete, progress);
    }

}
