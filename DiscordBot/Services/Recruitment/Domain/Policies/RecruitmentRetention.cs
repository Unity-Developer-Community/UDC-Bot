namespace DiscordBot.Services.Recruitment.Domain.Policies;

public static class RecruitmentRetention
{
    public static void Prune(RecruitmentState state, DateTimeOffset now, int postMonths, int authorMonths, int cooldownDays)
    {
        var floor = now.AddMonths(-postMonths);
        if (floor > state.DiscoveryFloor) state.DiscoveryFloor = floor;
        foreach (var p in state.Listings.Values.Where(p => CanExpire(p, floor)).ToArray())
            state.Listings.Remove(p.Id);
        foreach (var (id, author) in state.Authors.ToArray())
            if (author.UpdatedAt < now.AddMonths(-authorMonths) &&
                !(author.LastAcceptedCreation?.AddDays(cooldownDays) > now) &&
                !state.Listings.Values.Any(p => p.OwnerId == id)) state.Authors.Remove(id);
    }

    // Compatibility: unresolved reports and optional delivery receipts do not extend retention.
    // Changing this requires a retention-policy decision, not a storage refactor.
    public static bool CanExpire(Listing post, DateTimeOffset floor) =>
        post.Status is ListingStatus.Closed or ListingStatus.Deleted && post.UpdatedAt < floor &&
        post.PendingAction is null && post.Hold is null;
}
