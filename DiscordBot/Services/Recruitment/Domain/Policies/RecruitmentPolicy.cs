using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DiscordBot.Services.Recruitment;

public sealed record Eligibility(string? BlockingListingId, DateTimeOffset? NextAllowedAt, DateTimeOffset? AttemptPausedUntil = null)
{
    public bool Allowed => BlockingListingId is null && NextAllowedAt is null && AttemptPausedUntil is null;
}

public static class RecruitmentPolicy
{
    public static Eligibility Evaluate(RecruitmentState state, Listing candidate, DateTimeOffset now, int cooldownDays)
    {
        var blocker = state.Listings.Values
            .Where(p => p.OwnerId == candidate.OwnerId && p.Id != candidate.Id && p.Status == ListingStatus.Open)
            .Where(p => p.AcceptedAt.HasValue || p.AvailabilityUnknown ||
                p.CreatedAt < candidate.CreatedAt || (p.CreatedAt == candidate.CreatedAt &&
                ulong.Parse(p.Id) < ulong.Parse(candidate.Id)))
            .OrderBy(p => p.CreatedAt).ThenBy(p => ulong.Parse(p.Id)).FirstOrDefault();
        var next = state.Authors.GetValueOrDefault(candidate.OwnerId)?.LastAcceptedCreation?.AddDays(cooldownDays);
        return new Eligibility(blocker?.Id, next > now ? next : null, candidate.AttemptPausedUntil > now ? candidate.AttemptPausedUntil : null);
    }

    public static bool TryAccept(RecruitmentState state, Listing post, DateTimeOffset now, int cooldownDays)
    {
        if (post.Status != ListingStatus.Open || post.AcceptedAt.HasValue || !post.AcknowledgedAt.HasValue ||
            post.AvailabilityUnknown || !Evaluate(state, post, now, cooldownDays).Allowed) return false;
        post.AcceptedAt = now;
        post.LastOutcome = "Accepted";
        post.Version++;
        post.UpdatedAt = now;
        Author(state, post.OwnerId, now).LastAcceptedCreation = post.CreatedAt;
        return true;
    }

    public static AuthorHistory Author(RecruitmentState state, string owner, DateTimeOffset now)
    {
        if (!state.Authors.TryGetValue(owner, out var history))
            state.Authors[owner] = history = new AuthorHistory();
        history.UpdatedAt = now;
        return history;
    }

    public static void Prune(RecruitmentState state, DateTimeOffset now, int postMonths, int authorMonths, int cooldownDays)
    {
        var floor = now.AddMonths(-postMonths);
        if (floor > state.DiscoveryFloor) state.DiscoveryFloor = floor;
        foreach (var p in state.Listings.Values.Where(p => p.Status is ListingStatus.Closed or ListingStatus.Deleted &&
                     p.UpdatedAt < floor && p.PendingAction is null && p.Hold is null).ToArray())
            state.Listings.Remove(p.Id);
        foreach (var (id, author) in state.Authors.ToArray())
            if (author.UpdatedAt < now.AddMonths(-authorMonths) &&
                !(author.LastAcceptedCreation?.AddDays(cooldownDays) > now) &&
                !state.Listings.Values.Any(p => p.OwnerId == id)) state.Authors.Remove(id);
    }
}
