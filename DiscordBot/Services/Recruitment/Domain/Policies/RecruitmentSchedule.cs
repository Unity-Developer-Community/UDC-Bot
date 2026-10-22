namespace DiscordBot.Services.Recruitment.Domain.Policies;

internal enum RecruitmentDueReason
{
    Audit, RecoveryRetry, ForumRetry, ReferenceRetry, Rotation, ListingRetry, Publication,
    Attention, Pin, RegistryRefresh, WindowRecovery, Prompt, AcknowledgementNotice, Deadline, Eligibility
}
internal sealed record RecruitmentDueWork(DateTimeOffset At, RecruitmentDueReason Reason);

internal static class ListingDeliveryPolicy
{
    public static bool PinDue(Listing post, DateTimeOffset now) => post.Status is ListingStatus.Open or ListingStatus.Closed &&
        post.PublicMessageId is not null &&
        (post.PublicPin.MessageId != post.PublicMessageId ||
         (!post.PublicPin.Confirmed || post.PublicPin.RestoreArchive) && (post.PublicPin.Fault is null || post.PublicPin.Fault.IsDue(now)));

    public static DateTimeOffset? AcknowledgementNoticeDue(Listing post, DateTimeOffset now)
    {
        if (post.Status != ListingStatus.Open || post.AcceptedAt.HasValue || post.AcknowledgedAt.HasValue ||
            post.PublicMessageId is null || !(post.Deadline > now) ||
            post.AcknowledgementNotice.MessageId is not null && !post.AcknowledgementNotice.SendPending) return null;
        return post.AcknowledgementNotice.Fault is { } fault ? fault.RetryAt : now;
    }
}

/// <summary>Pure due-work selection. Parent holds suppress work that cannot make progress.</summary>
internal static class RecruitmentSchedule
{
    public static RecruitmentDueWork? Next(RecruitmentState state, DateTimeOffset now, DateTimeOffset audit,
        bool ready, bool evidenceCurrent, int cooldownDays, AcknowledgementRecovery windows)
    {
        var due = new List<RecruitmentDueWork>();
        void Add(DateTimeOffset? at, RecruitmentDueReason reason) { if (at.HasValue) due.Add(new(at.Value, reason)); }
        if (audit != DateTimeOffset.MaxValue) Add(audit, RecruitmentDueReason.Audit);
        Add(state.RecoveryFault?.RetryAt, RecruitmentDueReason.RecoveryRetry);
        if (state.RecoveryFault is null)
            foreach (var forum in state.Forums.Values) Add(forum.Fault?.RetryAt, RecruitmentDueReason.ForumRetry);
        if (ready)
            foreach (var forum in state.Forums.Values) Add(forum.Reference.Fault?.RetryAt, RecruitmentDueReason.ReferenceRetry);
        if (ready && evidenceCurrent)
        {
            Add(state.RotateAt, RecruitmentDueReason.Rotation);
            foreach (var post in state.Listings.Values)
                if (Listing(state, post, now, cooldownDays, windows.Contains(post.Id)) is { } work) due.Add(work);
        }
        return due.MinBy(d => d.At);
    }

    public static bool RecoveryDue(RecruitmentState state, DateTimeOffset now, DateTimeOffset audit, bool ready) =>
        audit <= now || state.RecoveryFault?.IsDue(now) == true ||
        state.RecoveryFault is null && state.Forums.Values.Any(f => f.Fault?.IsDue(now) == true) ||
        ready && (state.Forums.Values.Any(f => f.Reference.Fault?.IsDue(now) == true) || state.RotateAt <= now);

    public static RecruitmentDueWork? Listing(RecruitmentState state, Listing post, DateTimeOffset now, int cooldownDays, bool recoverWindow)
    {
        static RecruitmentDueWork? At(DateTimeOffset? at, RecruitmentDueReason reason) => at.HasValue ? new(at.Value, reason) : null;
        if (post.Fault is { } fault) return At(fault.RetryAt, RecruitmentDueReason.ListingRetry);
        var pinDue = ListingDeliveryPolicy.PinDue(post, now) ? (DateTimeOffset?)now : null;
        if (!pinDue.HasValue && post.Status is ListingStatus.Open or ListingStatus.Closed && post.PublicMessageId is not null)
            pinDue = post.PublicPin.Fault?.RetryAt;
        if (post.PendingAction is not null || post.PublicSendPending || post.StaffSendPending || post.PublicationPending)
            return new(now, RecruitmentDueReason.Publication);
        var attention = post.Attention.Where(n => n.MessageId is null || n.SendPending)
            .Select(n => n.Fault is null ? (DateTimeOffset?)now : n.Fault.RetryAt).Where(t => t.HasValue).Min();
        var due = new List<RecruitmentDueWork>();
        void Add(DateTimeOffset? at, RecruitmentDueReason reason) { if (At(at, reason) is { } work) due.Add(work); }
        Add(attention, RecruitmentDueReason.Attention);
        Add(pinDue, RecruitmentDueReason.Pin);
        if (post.Status != ListingStatus.Open) return due.MinBy(d => d.At);
        if (post.PortfolioRegistryVersion != state.PortfolioSites.Version) return new(now, RecruitmentDueReason.RegistryRefresh);
        if (recoverWindow) return new(now, RecruitmentDueReason.WindowRecovery);
        if (!post.PromptDeliveredAt.HasValue && !post.AcceptedAt.HasValue) return new(now, RecruitmentDueReason.Prompt);
        Add(ListingDeliveryPolicy.AcknowledgementNoticeDue(post, now), RecruitmentDueReason.AcknowledgementNotice);
        if (!post.Exempt && !post.Pinned && !post.AcceptedAt.HasValue) Add(post.Deadline, RecruitmentDueReason.Deadline);
        if (post.AcknowledgedAt.HasValue && !post.AcceptedAt.HasValue)
        {
            var next = ListingEligibilityPolicy.Evaluate(state, post, now, cooldownDays);
            if (next.BlockingListingId is null) Add(next.NextAllowedAt ?? now, RecruitmentDueReason.Eligibility);
        }
        return due.MinBy(d => d.At);
    }
}
