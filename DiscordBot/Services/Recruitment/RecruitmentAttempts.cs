namespace DiscordBot.Services.Recruitment;

public sealed partial class RecruitmentEngine
{
    // Called once per newly retained thread identity, regardless of later deletion or acknowledgement.
    private void RecordCreation(Listing post)
    {
        var history = RecruitmentPolicy.Author(State!, post.OwnerId, Now);
        if (post.Pinned || post.CreatedAt <= Now.AddMinutes(-_settings.AttemptWindowMinutes)) return;
        if (history.AttemptPausedUntil > Now)
        {
            post.AttemptPausedUntil = history.AttemptPausedUntil;
            return; // Extra attempts cannot prolong an existing pause.
        }
        if (history.AttemptPausedUntil.HasValue)
        {
            history.RecentCreations.Clear(); history.AttemptPausedUntil = null;
        }
        history.RecentCreations.RemoveAll(d => d <= Now.AddMinutes(-_settings.AttemptWindowMinutes));
        history.RecentCreations.Add(post.CreatedAt);
        if (history.RecentCreations.Count > _settings.AttemptLimit)
            post.AttemptPausedUntil = history.AttemptPausedUntil = Now.AddMinutes(_settings.AttemptPauseMinutes);
    }

    private static string EligibilityReason(Eligibility eligibility)
    {
        if (eligibility.AttemptPausedUntil is { } pause) return $"Posting attempts paused until {pause:yyyy-MM-dd HH:mm} UTC. Check /recruitment eligibility.";
        if (eligibility.BlockingListingId is { } id) return $"Existing listing {id} occupies your slot. Update or bump that thread; check /recruitment eligibility.";
        return $"Accepted-listing cooldown ends {eligibility.NextAllowedAt:yyyy-MM-dd HH:mm} UTC. Check /recruitment eligibility.";
    }

    public async Task<string> EligibilityAsync(string guild, string owner, CancellationToken ct)
    {
        if (State is null || guild != State.GuildId || !await _discord.IsMemberAsync(guild, owner, ct))
            return "This control is only available to current members of the configured server.";
        var lines = new List<string>();
        foreach (var post in State.Listings.Values.Where(p => p.OwnerId == owner && p.Status == ListingStatus.Open).Take(10))
            lines.Add($"Your listing: <#{post.Id}>. Update or bump this thread to keep its conversation history. Close it when finished; auto-archive does not free your slot.");
        if (State.Authors.TryGetValue(owner, out var author))
        {
            if (author.LastAcceptedCreation?.AddDays(_settings.CooldownDays) is { } next && next > Now)
                lines.Add($"A replacement can be accepted after {RecruitmentPresentation.Timestamp(next)}. Closing or deleting does not reset this cooldown.");
            if (author.AttemptPausedUntil > Now)
                lines.Add($"Posting attempts are paused until {RecruitmentPresentation.Timestamp(author.AttemptPausedUntil.Value)}. A moderator can review a mistake.");
            else
            {
                var used = author.AttemptPausedUntil.HasValue ? 0 : author.RecentCreations.Count(d => d > Now.AddMinutes(-_settings.AttemptWindowMinutes));
                lines.Add($"Recent creations: {used}/{_settings.AttemptLimit} in {_settings.AttemptWindowMinutes} minutes. Another attempt over this limit starts a {_settings.AttemptPauseMinutes}-minute pause, including deleted attempts.");
            }
        }
        else lines.Add($"You have no recorded listing restrictions. Up to {_settings.AttemptLimit} creations in {_settings.AttemptWindowMinutes} minutes; further attempts start a {_settings.AttemptPauseMinutes}-minute pause.");
        if (!EvidenceCurrent) lines.Add("Inventory recovery is in progress; eligibility will be checked again when you post.");
        return string.Join("\n", lines);
    }
}
