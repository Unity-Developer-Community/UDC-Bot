namespace DiscordBot.Services.Recruitment.Domain.Policies;

public static class ListingTransitions
{
    public static bool TryAccept(RecruitmentState state, Listing post, DateTimeOffset now, int cooldownDays)
    {
        if (post.Status != ListingStatus.Open || post.AcceptedAt.HasValue || !post.AcknowledgedAt.HasValue ||
            post.AvailabilityUnknown || !ListingEligibilityPolicy.Evaluate(state, post, now, cooldownDays).Allowed) return false;
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

}
