using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment;

/// <summary>Metadata only: terminal details compact into history before removal; Discord messages and XP are untouched.</summary>
public sealed class RecruitmentRetention(RecruitmentStateStore store, IOptions<RecruitmentOptions> options, TimeProvider time)
{
    public async Task TickAsync(CancellationToken token)
    {
        DateTimeOffset now = time.GetUtcNow();
        var state = (await store.LoadAsync(token))!;
        if (state.LastRetentionAtUtc > now.AddDays(-1)) return;
        await store.UpdateAsync(current =>
        {
            DateTimeOffset postCutoff = now.AddMonths(-options.Value.PostRetentionMonths);
            foreach (var post in current.Posts.Values.Where(post => CanCompact(post, postCutoff, now)).ToArray())
            {
                var author = RecruitmentHistory.Author(current, post);
                var group = RecruitmentHistory.Group(author, post.Forum);
                if (post.AcceptedAtUtc is not null)
                {
                    group.LastAcceptedCreatedAtUtc = RecruitmentHistory.Later(group.LastAcceptedCreatedAtUtc, post.CreatedAtUtc);
                    group.LastAcceptedDeletedAtUtc = RecruitmentHistory.Later(group.LastAcceptedDeletedAtUtc, post.DeletedObservedAtUtc);
                }
                author.LastActivityAtUtc = RecruitmentHistory.Later(author.LastActivityAtUtc,
                    RecruitmentHistory.Later(post.ClosedAtUtc, post.DeletedObservedAtUtc));
                current.RetiredThreadIds.Add(post.ThreadId);
                current.Posts.Remove(post.ThreadId);
            }
            DateTimeOffset authorCutoff = now.AddMonths(-options.Value.AuthorRetentionMonths);
            foreach (var author in current.Authors.Values.ToArray())
            {
                if (current.Posts.Values.Any(post => post.AuthorId == author.UserId)) continue;
                DateTimeOffset? activity = RecruitmentHistory.Later(author.LastActivityAtUtc, author.LastAttemptAtUtc);
                if (activity is null || activity > authorCutoff) continue;
                if (author.Groups.Values.Any(group => group.RequiresReview ||
                    group.LastAcceptedCreatedAtUtc?.AddDays(options.Value.CooldownDays) > now ||
                    group.LastAcceptedDeletedAtUtc?.AddDays(options.Value.CooldownDays) > now ||
                    group.Waiver?.GrantedAtUtc > authorCutoff)) continue;
                current.Authors.Remove(author.UserId);
            }
            current.LastRetentionAtUtc = now;
            return true;
        }, token);
    }

    private static bool CanCompact(RecruitmentPostRecord post, DateTimeOffset cutoff, DateTimeOffset now)
    {
        if (post.Lifecycle is not (RecruitmentLifecycle.Closed or RecruitmentLifecycle.Deleted) ||
            post.RequiresReview || post.DeletionTimeUncertain || post.PendingAction is { IsPending: true } ||
            post.Advisory.Confirmation?.ExpiresAtUtc > now || post.Advisory.SendRequestedAtUtc is not null ||
            post.Observation.FeedSendRequestedAtUtc is not null || post.Observation.FeedError is not null) return false;
        DateTimeOffset? terminalAt = RecruitmentHistory.Later(post.ClosedAtUtc, post.DeletedObservedAtUtc);
        return terminalAt is not null && terminalAt <= cutoff && post.Audit.All(entry => entry.AtUtc <= cutoff);
    }
}
