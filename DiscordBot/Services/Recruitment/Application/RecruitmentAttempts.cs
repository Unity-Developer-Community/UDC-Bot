using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Policies;
namespace DiscordBot.Services.Recruitment;

public sealed partial class RecruitmentEngine
{
    public async Task ThreadCreatedAsync(ThreadCreation creation, CancellationToken ct)
    {
        if (State is null || !_ready || !State.Forums.Values.Any(f => f.ForumId == creation.ForumId)) return;
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

    // Called once per newly retained thread identity, regardless of later deletion or acknowledgement.
    public async Task<string> EligibilityAsync(string guild, string owner, CancellationToken ct)
    {
        if (State is null || guild != State.GuildId || !await _discord.IsMemberAsync(guild, owner, ct))
            return "This control is only available to current members of the configured server.";
        var lines = new List<string>();
        var open = State.Listings.Values.Where(p => p.OwnerId == owner && p.Status == ListingStatus.Open).Take(10).ToArray();
        foreach (var post in open)
            lines.Add($"Your {(post.AcceptedAt.HasValue ? "open" : "pending")} listing occupies your slot: <#{post.Id}>. Update or bump this thread to keep its conversation history. Close it when finished; auto-archive does not free your slot.");
        if (State.Authors.TryGetValue(owner, out var author))
        {
            if (author.LastAcceptedCreation?.AddDays(_settings.CooldownDays) is { } next && next > Now)
                lines.Add($"Your accepted-listing cooldown ends {DiscordDisplayText.Timestamp(next)}. Closing or deleting does not reset this cooldown.");
            if (author.AttemptPausedUntil > Now)
                lines.Add($"Posting attempts are paused until {DiscordDisplayText.Timestamp(author.AttemptPausedUntil.Value)}. A moderator can review a mistake.");
        }
        if (!EvidenceCurrent)
            lines.Add("Eligibility is being checked; the one-listing rule still applies.");
        else if (open.Length == 0)
            lines.Add("No open listing is recorded.");
        lines.Add($"You may have **one open listing across all four recruitment forums**. Pending listings reserve that slot. An accepted listing starts a **{_settings.CooldownDays}-day cooldown from creation**.");
        var used = author is null || author.AttemptPausedUntil.HasValue && author.AttemptPausedUntil <= Now
            ? 0 : author.RecentCreations.Count(d => d > Now.AddMinutes(-_settings.AttemptWindowMinutes));
        lines.Add($"Anti-spam: creation attempt {_settings.AttemptLimit + 1} within {_settings.AttemptWindowMinutes} minutes triggers a {_settings.AttemptPauseMinutes}-minute pause; deleted attempts count. Recent attempts: {used}.");
        return string.Join("\n", lines);
    }
}
