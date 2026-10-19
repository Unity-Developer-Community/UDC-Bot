using System.IO;
using DiscordBot.Settings;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Analysis;
using DiscordBot.Services.Recruitment.Domain.Policies;
using DiscordBot.Services.Recruitment.Infrastructure.Discord;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed class ListingWorkflow(RecruitmentState state, RecruitmentSettings settings, IRecruitmentDiscord discord, TimeProvider time, Action save, AcknowledgementRecovery windows, ListingReader reader, ListingPublisher publisher, ListingActionExecutor actions, ListingEvidenceCapture facts, ListingForums forums, Func<bool> evidenceCurrent, Action<ThreadSnapshot> observeReferenceCandidate)
{
    private readonly RecruitmentState State = state;
    private readonly RecruitmentSettings _settings = settings;
    private readonly IRecruitmentDiscord _discord = discord;
    private DateTimeOffset Now => time.GetUtcNow();
    private void Save() => save();
    private readonly AcknowledgementRecovery _recoverWindows = windows;
    private readonly ListingReader _reader = reader;
    private readonly ListingPublisher _publisher = publisher;
    private readonly ListingActionExecutor _actions = actions;
    private readonly ListingEvidenceCapture _facts = facts;
    private readonly ListingForums _forums = forums;
    private bool EvidenceCurrent => evidenceCurrent();

    public Task ObserveAsync(ThreadSnapshot live, bool observedFromCreation, CancellationToken ct)
    {
        if (live.OwnerIsBot || live.OwnerIsStaff || !State.Forums.Values.Any(f => f.ForumId == live.ForumId)) return Task.CompletedTask;
        if (State.Listings.ContainsKey(live.Id) || live.CreatedAt < State.DiscoveryFloor) return Task.CompletedTask;
        var post = new Listing
        {
            Id = live.Id, ForumId = live.ForumId, OwnerId = live.OwnerId, Title = DiscordDisplayText.Literal(live.Title),
            CreatedAt = live.CreatedAt.ToUniversalTime(), UpdatedAt = Now, Pinned = live.Pinned,
            ResponseCoverageComplete = observedFromCreation,
            Facts = new InitialFacts
            {
                AccountCreatedAt = live.AccountCreatedAt, JoinedAt = live.JoinedAt,
                MemberRoleCount = live.MemberRoleCount,
                TagAdvice = _forums.TagAdvice(live),
                PriorAcceptedListing = State.Authors.GetValueOrDefault(live.OwnerId)?.LastAcceptedCreation is not null
            }
        };
        State.Listings[post.Id] = post;
        _facts.RefreshFacts(post, live);
        RecordCreation(post);
        Save();
        return Task.CompletedTask;
    }

    public async Task ProcessAsync(Listing post, CancellationToken ct)
    {
        if (post.Status == ListingStatus.Deleted)
        {
            if (post.PublicationPending || post.StaffSendPending || _publisher.AttentionDue(post)) await _publisher.PublishAsync(post, ct);
            return;
        }
        if (post.Status == ListingStatus.Reference)
        {
            if (post.PendingAction is not null) await _actions.ExecuteActionAsync(post, ct);
            return;
        }
        var live = await _reader.ReadAsync(post, ct);
        if (live is null)
        {
            // Unknown Channel proves absence, not who deleted it or when. Never invent a timeout or cooldown.
            _reader.RecordAbsence(post);
            await _publisher.PublishStaffAsync(post, ct); post.PublicationPending = false; Save();
            return;
        }
        if (live.OwnerIsBot || live.OwnerIsStaff)
        {
            observeReferenceCandidate(live);
            // Retire records made by older versions without editing staff content or erasing accepted history.
            post.Status = ListingStatus.Reference; post.PendingAction = null; post.Deadline = null;
            post.Hold = null; post.Fault = null; post.ValidCodes.Clear(); _recoverWindows.Remove(post.Id);
            post.LastOutcome = "Staff or bot post; excluded from member recruitment"; post.Version++; Save();
            await _actions.BeginActionAsync(post, RecruitmentActionKind.RemoveAdvisory, "system", post.LastOutcome, "", false, ct);
            return;
        }
        RefreshSnapshot(post, live);
        if (live.Starter is null && !post.PromptDeliveredAt.HasValue && !post.AcceptedAt.HasValue)
        {
            post.Hold = "Starter message is not available yet";
            post.Fault = new RecruitmentFault(post.Hold, Now.AddSeconds(15));
            _discord.InvalidateThread(post.Id); Save(); return;
        }
        if (post.PendingAction is not null) { await _actions.ExecuteActionAsync(post, ct); return; }
        if (post.Status != ListingStatus.Open)
        {
            if (post.StaffSendPending || post.PublicSendPending || post.PublicationPending || _publisher.AttentionDue(post)) await _publisher.PublishAsync(post, ct);
            return;
        }
        if (await CloseIneligibleAsync(post, ct)) return;
        await EnsureAcknowledgementWindowAsync(post, ct);
        await AcceptAcknowledgedAsync(post, ct);
        await EnforceAutomaticActionAsync(post, live, ct);
    }

    private async Task EnsureAcknowledgementWindowAsync(Listing post, CancellationToken ct)
    {
        if (_recoverWindows.Remove(post.Id))
        {
            post.PromptDeliveredAt = post.Deadline = null;
            // A timeout that was never sent must not run immediately after an outage.
            post.Version++;
        }
        var receipt = State!.Forums[_forums.Purpose(post.ForumId)];
        if (!post.AcceptedAt.HasValue && !post.ValidCodes.Contains(receipt.VisibleCode)) post.ValidCodes.Add(receipt.VisibleCode);
        Save();
        await _publisher.PublishAsync(post, ct);
        if (!post.PromptDeliveredAt.HasValue && !post.AcceptedAt.HasValue)
        {
            post.PromptDeliveredAt = Now;
            post.Deadline = Now.AddMinutes(_settings.AcknowledgementMinutes);
            post.Version++; Save();
            await _publisher.PublishAsync(post, ct);
        }
        await _publisher.PublishAcknowledgementNoticeAsync(post, ct);
    }

    private async Task AcceptAcknowledgedAsync(Listing post, CancellationToken ct)
    {
        if (EvidenceCurrent && post.AcknowledgedAt.HasValue && !post.AcceptedAt.HasValue)
            await _reader.RefreshBlockersAsync(post, ct);
        if (EvidenceCurrent && ListingTransitions.TryAccept(State, post, Now, _settings.CooldownDays))
        { Save(); await _publisher.PublishAsync(post, ct); }
    }

    private async Task EnforceAutomaticActionAsync(Listing post, ThreadSnapshot live, CancellationToken ct)
    {
        if (!EvidenceCurrent || post.Exempt || post.Pinned) return;
        if (live.Tags.Contains(_forums.ClosingTag(post)))
            await _actions.BeginActionAsync(post, RecruitmentActionKind.Close, "system", "Closed tag applied", RecruitmentConditions.ClosedTag, true, ct);
        else if (!post.AcceptedAt.HasValue && post.Deadline <= Now)
        {
            if (!post.AcknowledgedAt.HasValue)
                await _actions.BeginActionAsync(post, RecruitmentActionKind.Delete, "system", "Guidelines acknowledgement timed out", RecruitmentConditions.Timeout, true, ct);
            else
                await _actions.BeginActionAsync(post, RecruitmentActionKind.Close, "system", "Acknowledged but still ineligible at deadline", RecruitmentConditions.Ineligible, true, ct);
        }
    }

    private void RefreshSnapshot(Listing post, ThreadSnapshot live)
    {
        post.Title = DiscordDisplayText.Literal(live.Title);
        _facts.RefreshFacts(post, live);
        post.Facts.TagAdvice = _forums.TagAdvice(live);
        if (!post.PublicSendPending)
        {
            post.PublicRestoreArchive = post.PublicPin.RestoreArchive || live.Archived && post.AcceptedAt.HasValue;
            post.PublicRestoreLocked = post.PublicRestoreArchive && (post.PublicPin.RestoreArchive ? post.PublicPin.RestoreLocked : live.Locked);
        }
        post.Hold = null;
    }

    private async Task<bool> CloseIneligibleAsync(Listing post, CancellationToken ct)
    {
        if (EvidenceCurrent && !post.AcceptedAt.HasValue && !post.Exempt && !post.Pinned)
        {
            await _reader.RefreshBlockersAsync(post, ct);
            var eligibility = ListingEligibilityPolicy.Evaluate(State!, post, Now, _settings.CooldownDays);
            if (!eligibility.Allowed)
            {
                await _actions.BeginActionAsync(post, RecruitmentActionKind.Close, "system", ListingEligibilityText.EligibilityReason(eligibility), RecruitmentConditions.Ineligible, true, ct);
                return true;
            }
        }
        return false;
    }

    public async Task ProcessSafelyAsync(Listing post, CancellationToken ct, bool verifyHeld = false)
    {
        if (post.Fault is { } fault && !fault.IsDue(Now))
        {
            if (!verifyHeld || fault.RetryAt is not null) return;
            // A permanent write hold must not hide a deletion during downtime. Probe once per bounded audit.
            try
            {
                var live = await _reader.ReadAsync(post, ct, refresh: true);
                if (live is null) _reader.RecordAbsence(post);
                else if (!live.OwnerIsBot && !live.OwnerIsStaff) return;
            }
            catch (Exception ex) when (ex is not System.IO.IOException && ex is not OperationCanceledException) { return; }
        }
        var previousFault = post.Fault;
        post.Fault = null;
        try
        {
            await ProcessAsync(post, ct);
            await _publisher.PinResponseAsync(post, ct);
            if (previousFault is not null) Save();
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not System.IO.IOException)
        {
            _discord.InvalidateThread(post.Id);
            post.Fault = RecruitmentFaultClassifier.From(ex, Now, previousFault);
            post.Hold = post.Fault.Message;
            if (!post.AcceptedAt.HasValue && post.Status == ListingStatus.Open) _recoverWindows.Add(post.Id);
            Save();
        }
    }

    public void RecordCreation(Listing post)
    {
        var history = ListingTransitions.Author(State!, post.OwnerId, Now);
        if (post.Pinned || post.CreatedAt <= Now.AddMinutes(-_settings.AttemptWindowMinutes)) return;
        var decision = AttemptPolicy.Record(history.RecentCreations, history.AttemptPausedUntil, post.CreatedAt, Now,
            _settings.AttemptWindowMinutes, _settings.AttemptLimit, _settings.AttemptPauseMinutes);
        history.RecentCreations = decision.Creations.ToList();
        post.AttemptPausedUntil = history.AttemptPausedUntil = decision.PausedUntil;
        if (decision.PauseStarted) _publisher.QueueAttention(post, "Repeated creations triggered a posting pause");
    }

}
