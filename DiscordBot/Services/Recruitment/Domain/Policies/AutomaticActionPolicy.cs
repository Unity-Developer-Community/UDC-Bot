namespace DiscordBot.Services.Recruitment.Domain.Policies;

internal static class AutomaticActionPolicy
{
    public static bool ShouldCancel(RecruitmentState state, Listing post, RecruitmentAction action,
        DateTimeOffset now, int cooldownDays, bool evidenceCurrent, bool recoveringWindow, bool closingTagApplied)
    {
        if (!action.Automatic) return false;
        if (!evidenceCurrent || post.Exempt || post.Pinned || post.Status != ListingStatus.Open) return true;
        return action.Condition switch
        {
            RecruitmentConditions.Timeout => post.AcknowledgedAt.HasValue || post.AcceptedAt.HasValue || post.Deadline > now || recoveringWindow,
            RecruitmentConditions.Ineligible => post.AcceptedAt.HasValue || ListingEligibilityPolicy.Evaluate(state, post, now, cooldownDays).Allowed,
            RecruitmentConditions.ClosedTag => !closingTagApplied,
            _ => false // Preserve the behavior of older/unknown persisted conditions.
        };
    }
}
