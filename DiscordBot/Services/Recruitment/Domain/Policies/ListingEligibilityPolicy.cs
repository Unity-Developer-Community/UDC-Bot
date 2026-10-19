namespace DiscordBot.Services.Recruitment.Domain.Policies;

public sealed record Eligibility(string? BlockingListingId, DateTimeOffset? NextAllowedAt, DateTimeOffset? AttemptPausedUntil = null)
{
    public bool Allowed => BlockingListingId is null && NextAllowedAt is null && AttemptPausedUntil is null;
}

public static class ListingEligibilityPolicy
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

}
