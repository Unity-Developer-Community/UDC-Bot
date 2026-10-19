using System.IO;
using DiscordBot.Settings;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Analysis;
using DiscordBot.Services.Recruitment.Domain.Policies;
using DiscordBot.Services.Recruitment.Infrastructure.Discord;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed class ListingActionExecutor(RecruitmentState state, RecruitmentSettings settings, IRecruitmentDiscord discord, TimeProvider time, Action save, AcknowledgementRecovery windows, ListingReader reader, ListingPublisher publisher, ListingForums forums, Func<bool> evidenceCurrent, Func<string, bool> pendingThreadWork)
{
    private readonly RecruitmentState State = state;
    private readonly RecruitmentSettings _settings = settings;
    private readonly IRecruitmentDiscord _discord = discord;
    private DateTimeOffset Now => time.GetUtcNow();
    private void Save() => save();
    private readonly AcknowledgementRecovery _recoverWindows = windows;
    private readonly ListingReader _reader = reader;
    private readonly ListingPublisher _publisher = publisher;
    private readonly ListingForums _forums = forums;
    private bool EvidenceCurrent => evidenceCurrent();
    private readonly Func<string, bool> _pendingThreadWork = pendingThreadWork;

    public async Task BeginActionAsync(Listing post, RecruitmentActionKind kind, string actor, string reason, string condition, bool automatic, CancellationToken ct, bool requiresModerator = false)
    {
        if (post.PendingAction is not null) throw new InvalidOperationException("An earlier action still needs reconciliation.");
        post.PendingAction = new RecruitmentAction { Kind = kind, ActorId = actor, Reason = reason, Condition = condition,
            Automatic = automatic, RequiresModerator = requiresModerator, GuidelineTimeout = condition == RecruitmentConditions.Timeout, CreatedAt = Now };
        if (!automatic) Audit(post, actor, reason);
        Save();
        await ExecuteActionAsync(post, ct);
    }

    public async Task ExecuteActionAsync(Listing post, CancellationToken ct)
    {
        var action = post.PendingAction!;
        if (action.Automatic && action.Condition == RecruitmentConditions.LegacyUnanswered)
        {
            await ReconcileLegacyExpiryAsync(post, action, ct);
            return;
        }
        if (action.RequiresModerator && !await _discord.IsModeratorAsync(State!.GuildId, action.ActorId, ct))
        {
            post.PendingAction = null; post.LastOutcome = "Staff action cancelled: moderator access no longer present";
            post.PublicationPending = true; Save(); await _publisher.PublishStaffAsync(post, ct); return;
        }
        // Intent and staff evidence must exist before removing or changing member content.
        await _publisher.PublishStaffAsync(post, ct);
        var live = await _reader.ReadAsync(post, ct, refresh: true);
        if (live is null)
        {
            _reader.RecordAbsence(post);
            await _publisher.PublishStaffAsync(post, ct); post.PublicationPending = false; Save(); return;
        }
        if (action.Kind == RecruitmentActionKind.RemoveAdvisory)
        {
            await RemoveAdvisoryAsync(post, ct);
            return;
        }

        if (action.CallStarted && action.Kind == RecruitmentActionKind.Close && live.Archived && live.Locked)
        {
            post.Status = ListingStatus.Closed; post.PendingAction = null;
            post.LastOutcome = "Closure confirmed after interrupted action"; post.UpdatedAt = Now; post.Version++;
            post.PublicationPending = true; Save(); await _publisher.PublishAsync(post, ct); return;
        }
        if (action.Automatic && AutomaticActionPolicy.ShouldCancel(State, post, action, Now, _settings.CooldownDays,
            EvidenceCurrent, _recoverWindows.Contains(post.Id),
            action.Condition == RecruitmentConditions.ClosedTag && live.Tags.Contains(_forums.ClosingTag(post))))
        { post.PendingAction = null; Save(); return; }
        if (action.Kind == RecruitmentActionKind.Reopen && State!.Listings.Values.Any(p => p.OwnerId == post.OwnerId && p.Id != post.Id && p.Status == ListingStatus.Open))
            throw new InvalidOperationException("Another listing occupies this author's slot.");
        // Gateway facts and timely owner interactions already queued must be applied before an automatic action.
        if (action.Automatic && (!EvidenceCurrent || _pendingThreadWork(post.Id)))
        { post.PendingAction = null; Save(); return; }
        // The pending action is already visible to staff. Do not append an automatic-action audit
        // for a request cancelled by newer evidence before it reaches the write boundary.
        if (action.Automatic && !action.CallStarted) Audit(post, action.ActorId, action.Reason);
        action.CallStarted = true; Save();
        switch (action.Kind)
        {
            case RecruitmentActionKind.Delete:
                await _discord.DeleteAsync(live, action.Reason, action.Automatic, ct, () => !action.Automatic || EvidenceCurrent && !_pendingThreadWork(post.Id));
                post.Status = ListingStatus.Deleted;
                if (action.GuidelineTimeout)
                {
                    var author = ListingTransitions.Author(State!, post.OwnerId, Now);
                    author.ConsecutiveTimeouts++;
                    if (author.ConsecutiveTimeouts >= 3 && !author.TimeoutAlerted)
                    { post.TimeoutAlertCount = author.ConsecutiveTimeouts; author.TimeoutAlerted = true; _publisher.QueueAttention(post, "Three consecutive confirmed Guidelines timeouts"); }
                }
                break;
            case RecruitmentActionKind.Close:
                await _discord.CloseAsync(live, _forums.ClosingTag(post), action.Automatic, ct, () => !action.Automatic || EvidenceCurrent && !_pendingThreadWork(post.Id));
                post.Status = ListingStatus.Closed;
                break;
            case RecruitmentActionKind.Reopen:
                await _discord.ReopenAsync(live, _forums.ClosingTag(post), ct);
                post.Status = ListingStatus.Open;
                if (!post.AcceptedAt.HasValue) _recoverWindows.Add(post.Id);
                break;
        }
        ListingTransitions.Author(State!, post.OwnerId, Now);
        post.LastOutcome = action.Reason; post.PendingAction = null; post.UpdatedAt = Now; post.Version++; post.PublicationPending = true; Save();
        await _publisher.PublishAsync(post, ct);
    }

    private async Task ReconcileLegacyExpiryAsync(Listing post, RecruitmentAction action, CancellationToken ct)
    {
        // Cancel unsent legacy expiry; preserve a closure already applied before the upgrade.
        var previous = action.CallStarted ? await _reader.ReadAsync(post, ct, refresh: true) : null;
        if (action.CallStarted && previous is null) { _reader.RecordAbsence(post); return; }
        var alreadyClosed = action.CallStarted && previous is { Locked: true, Archived: true };
        if (alreadyClosed) post.Status = ListingStatus.Closed;
        post.PendingAction = null; post.PublicationPending = true;
        post.LastOutcome = alreadyClosed ? "Closure applied before upgrade confirmed" : "Automatic expiry cancelled; listing remains open";
        Save();
    }

    private async Task RemoveAdvisoryAsync(Listing post, CancellationToken ct)
    {
        if (post.PublicMessageId is null && post.PublicSendPending)
        {
            // Resolve a possibly delivered provisional advisory before removing it.
            var recovered = await _discord.UpsertAsync(post.Id, null, true, post.PublicSendStartedAt ?? post.CreatedAt,
                RecruitmentPresentation.PublicV2(State!, post, _settings, Now), Save, ct);
            post.PublicMessageId = recovered.Id; post.PublicSendPending = false; Save();
        }
        if (post.PublicMessageId is not null) await _discord.DeleteAdvisoryAsync(post.Id, post.PublicMessageId, ct, $"recruitment:{post.Id}:public");
        post.PublicMessageId = null; post.PublicMediaAttachmentId = null; post.PublicMediaFileName = null; post.PendingAction = null; Save();
    }

    public void Audit(Listing post, string actor, string reason)
    {
        post.LastActor = actor; post.LastReason = reason;
        post.Audit.Add(new RecruitmentAudit(Now, actor, reason));
        if (post.Audit.Count > 30) post.Audit.RemoveRange(0, post.Audit.Count - 30);
    }

}
