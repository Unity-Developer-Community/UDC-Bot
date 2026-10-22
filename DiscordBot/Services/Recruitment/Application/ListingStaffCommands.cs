using DiscordBot.Settings;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Policies;
using DiscordBot.Services.Recruitment.Presentation;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed class ListingStaffCommands(
    RecruitmentState state,
    TimeProvider time,
    Action save,
    ListingActionExecutor actions,
    ListingPublisher publisher,
    AcknowledgementRecovery windows,
    Func<CancellationToken, Task<bool>> discover)
{
    private readonly RecruitmentState State = state;
    private readonly ListingActionExecutor _actions = actions;
    private readonly ListingPublisher _publisher = publisher;
    private readonly AcknowledgementRecovery _recoverWindows = windows;
    private DateTimeOffset Now => time.GetUtcNow();
    private void Save() => save();

    public async Task<string> ExecuteAsync(string actor, StaffCommand command, string target, string reason, CancellationToken ct)
    {
        var operation = command.Operation;
        if (operation == StaffOperation.WaiveCooldown)
        {
            if (!State.Authors.TryGetValue(target, out var author)) return "Author history was not found.";
            // Audit on every retained listing for this author; waiver never changes occupied capacity.
            var posts = State.Listings.Values.Where(p => p.OwnerId == target).ToArray();
            if (posts.Length == 0) return "No listing audit record remains; no active cooldown should need a waiver.";
            foreach (var p in posts) { _actions!.Audit(p, actor, "Waive cooldown: " + reason); Save(); await _publisher!.PublishStaffAsync(p, ct); }
            author.LastAcceptedCreation = null; author.UpdatedAt = Now; Save();
            return "Creation cooldown waived. Existing open listings still occupy the slot.";
        }
        if (!State.Listings.TryGetValue(target, out var post)) return "Managed thread was not found. Reconcile first.";
        _actions!.Audit(post, actor, command.Name + ": " + reason); Save(); await _publisher!.PublishStaffAsync(post, ct);
        switch (operation)
        {
            case StaffOperation.Exempt: post.Exempt = true; break;
            case StaffOperation.Unexempt: post.Exempt = false; _recoverWindows.Add(post.Id); break;
            case StaffOperation.ResolveReports:
                foreach (var report in post.Reports.Where(r => r.ResolvedAt is null)) report.ResolvedAt = Now;
                break;
            case StaffOperation.ReviewChanges: post.ReviewedMaterialChanges = post.MaterialChanges; break;
            case StaffOperation.ResetAttempts:
                var attemptHistory = ListingTransitions.Author(State, post.OwnerId, Now);
                attemptHistory.RecentCreations.Clear(); attemptHistory.AttemptPausedUntil = null;
                foreach (var attempt in State.Listings.Values.Where(p => p.OwnerId == post.OwnerId)) attempt.AttemptPausedUntil = null;
                break;
            case StaffOperation.ResetTimeouts:
                var timeoutHistory = ListingTransitions.Author(State, post.OwnerId, Now);
                timeoutHistory.ConsecutiveTimeouts = 0; timeoutHistory.TimeoutAlerted = false; break;
            case StaffOperation.Reset:
                if (post.AcceptedAt.HasValue || post.Status != ListingStatus.Open) return "Only an open unaccepted attempt can have its acknowledgement reset.";
                post.AcknowledgedAt = null; post.ValidCodes.Clear(); _recoverWindows.Add(post.Id);
                var history = ListingTransitions.Author(State, post.OwnerId, Now);
                history.ConsecutiveTimeouts = 0; history.TimeoutAlerted = false; break;
            case StaffOperation.Reference:
                if (post.Status == ListingStatus.Deleted) return "A deleted thread cannot become a reference post.";
                post.Status = ListingStatus.Reference; post.PendingAction = null; post.Deadline = null; post.ValidCodes.Clear();
                post.LastOutcome = "Moderator-designated reference post"; post.Version++; Save();
                await _actions!.BeginActionAsync(post, RecruitmentActionKind.RemoveAdvisory, actor, reason, "", false, ct, requiresModerator: true);
                return "Reference post designated; bot advisory removed. Any accepted cooldown is preserved.";
            case StaffOperation.Close:
                if (post.Status != ListingStatus.Open) return "Only an open listing can be closed.";
                await _actions!.BeginActionAsync(post, RecruitmentActionKind.Close, actor, reason, "", false, ct, requiresModerator: true); return post.Status == ListingStatus.Closed ? "Listing closed." : "Closure was cancelled or is awaiting reconciliation.";
            case StaffOperation.Reopen:
                if (post.Status != ListingStatus.Closed) return "Only a closed listing can be reopened.";
                if (!await discover(ct)) return "Inventory is incomplete; reopening is held.";
                if (State.Listings.Values.Any(p => p.OwnerId == post.OwnerId && p.Id != post.Id && p.Status == ListingStatus.Open))
                    return "Another listing occupies this author's slot.";
                await _actions!.BeginActionAsync(post, RecruitmentActionKind.Reopen, actor, reason, "", false, ct, requiresModerator: true); return post.Status == ListingStatus.Open
                    ? "Listing reopened. Update this thread to preserve its conversation history." : "Reopening was cancelled or is awaiting reconciliation.";
            case StaffOperation.ReviewAbsent:
                if (!post.AvailabilityUnknown) return "Availability is known; use close or reconcile instead.";
                post.Status = ListingStatus.Deleted; post.AvailabilityUnknown = false; post.PendingAction = null;
                post.PublicSendPending = false; post.Hold = null;
                ListingTransitions.Author(State, post.OwnerId, Now);
                post.LastOutcome = "Staff confirmed absence"; break;
            default: return "Unknown recruitment operation.";
        }
        post.Version++; post.UpdatedAt = Now; Save();
        if (operation == StaffOperation.ReviewAbsent) await _publisher!.PublishStaffAsync(post, ct);
        else await _publisher!.PublishAsync(post, ct);
        return "Recruitment record updated.";
    }
}
