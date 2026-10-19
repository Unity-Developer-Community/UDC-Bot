using System.IO;
using DiscordBot.Settings;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Analysis;
using DiscordBot.Services.Recruitment.Domain.Policies;
using DiscordBot.Services.Recruitment.Infrastructure.Discord;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed class ListingPublisher(
    RecruitmentState state,
    RecruitmentSettings settings,
    IRecruitmentDiscord discord,
    TimeProvider time,
    Action save,
    Action guardFeedWrite)
{
    private readonly RecruitmentState State = state;
    private readonly RecruitmentSettings _settings = settings;
    private readonly IRecruitmentDiscord _discord = discord;
    private DateTimeOffset Now => time.GetUtcNow();
    private void Save() => save();
    private void GuardFeedWrite() => guardFeedWrite();
    private readonly Dictionary<string, string> _publicRenders = new();
    private readonly Dictionary<string, string> _staffRenders = new();
    public void ClearRenders() { _publicRenders.Clear(); _staffRenders.Clear(); }
    public void InvalidatePublic(string id) => _publicRenders.Remove(id);
    public void InvalidateStaff(string id) => _staffRenders.Remove(id);

    public async Task PublishStaffAsync(Listing post, CancellationToken ct)
    {
        await PublishAttentionAsync(post, ct);
        var message = RecruitmentPresentation.Staff(State!, post, _settings);
        if (!post.StaffSendPending && post.StaffMessageId is not null && _staffRenders.GetValueOrDefault(post.Id) == message.Content) return;
        var recovering = post.StaffSendPending;
        var delivery = await _discord.UpsertAsync(_settings.FeedChannelId.ToString(), post.StaffMessageId, recovering,
            post.StaffSendStartedAt ?? post.CreatedAt, message, () =>
            {
                GuardFeedWrite();
                if (!recovering) post.StaffSendStartedAt = Now;
                post.StaffSendPending = true; Save();
            }, ct);
        var changed = post.StaffMessageId != delivery.Id || post.StaffSendPending;
        post.StaffMessageId = delivery.Id; post.StaffSendPending = false;
        _staffRenders[post.Id] = message.Content;
        if (changed) Save();
    }

    public async Task PublishAsync(Listing post, CancellationToken ct)
    {
        await PublishStaffAsync(post, ct);
        if (post.Status is ListingStatus.Deleted or ListingStatus.Reference)
        { post.PublicationPending = false; post.PublicSendPending = false; Save(); return; }
        var message = RecruitmentPresentation.PublicV2(State!, post, _settings, Now);
        var renderKey = message.RenderKey + "\nPortfolio registry " + State!.PortfolioSites.Version;
        if (!post.PublicSendPending && !post.PublicationPending && post.PublicMessageId is not null && _publicRenders.GetValueOrDefault(post.Id) == renderKey) return;
        var recovering = post.PublicSendPending;
        var delivery = await _discord.UpsertAsync(post.Id, post.PublicMessageId, recovering, post.PublicSendStartedAt ?? post.CreatedAt,
            message.WithRestoration(post.Status == ListingStatus.Closed ? ThreadRestoration.LockedArchive :
                new ThreadRestoration(Archived: post.PublicRestoreArchive, Locked: post.PublicRestoreLocked)), () =>
            {
                if (!recovering) post.PublicSendStartedAt = Now;
                post.PublicSendPending = true; Save();
            }, ct);
        var changed = post.PublicMessageId != delivery.Id || post.PublicSendPending || post.PublicationPending ||
            post.PublicMediaAttachmentId != delivery.MediaAttachmentId || post.PublicMediaFileName != delivery.MediaFileName;
        post.PublicMediaAttachmentId = delivery.MediaAttachmentId; post.PublicMediaFileName = delivery.MediaFileName;
        post.PublicMessageId = delivery.Id; post.PublicSendPending = false; post.PublicationPending = false;
        _publicRenders[post.Id] = renderKey;
        if (delivery.NewlySent && post.Deadline.HasValue && !post.AcceptedAt.HasValue)
        { post.PromptDeliveredAt = null; post.Deadline = null; post.Version++; changed = true; }
        if (changed) Save();
    }

    public void QueueAttention(Listing post, string reason)
    {
        // Bound retained receipts. During prolonged feed failure, coalesce rather than grow without limit.
        if (post.Attention.Count >= 8)
        {
            var completed = post.Attention.FirstOrDefault(n => n.MessageId is not null && !n.SendPending);
            if (completed is null) return; // Existing pending notices and the overview still link this listing.
            post.Attention.Remove(completed);
        }
        post.Attention.Add(new AttentionNotice { At = Now, Reason = reason });
    }

    public bool AttentionDue(Listing post) => post.Attention.Any(n => (n.MessageId is null || n.SendPending) && (n.Fault is null || n.Fault.IsDue(Now)));

    public async Task PublishAttentionAsync(Listing post, CancellationToken ct)
    {
        foreach (var notice in post.Attention.Where(n => (n.MessageId is null || n.SendPending) && (n.Fault is null || n.Fault.IsDue(Now))).Take(3))
        {
            try
            {
                var marker = $"recruitment:{post.Id}:attention:{notice.Id}";
                var message = new RecruitmentMessage($"**Recruitment review** · {notice.Reason}\n<#{post.Id}> · author `{post.OwnerId}`\nUse `/recruitment lookup {post.Id}` for evidence and review actions.\n||{marker}||", marker, false);
                var delivery = await _discord.UpsertAsync(_settings.FeedChannelId.ToString(), notice.MessageId, notice.SendPending,
                    notice.At, message, () => { GuardFeedWrite(); notice.SendPending = true; Save(); }, ct);
                notice.MessageId = delivery.Id; notice.SendPending = false; notice.Fault = null; Save();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not System.IO.IOException)
            {
                notice.Fault = RecruitmentFaultClassifier.From(ex, Now, notice.Fault); Save();
            }
        }
    }

    public DateTimeOffset? AcknowledgementNoticeDue(Listing post) => ListingDeliveryPolicy.AcknowledgementNoticeDue(post, Now);

    public async Task PublishAcknowledgementNoticeAsync(Listing post, CancellationToken ct)
    {
        if (AcknowledgementNoticeDue(post) is not { } due || due > Now) return;
        var notice = post.AcknowledgementNotice;
        var marker = $"recruitment:{post.Id}:acknowledgement";
        // The card owns the current deadline and may be restored with a new message ID/window.
        var message = new RecruitmentMessage($"<@{post.OwnerId}>, please read this forum's **Guidelines**, then use **Acknowledge** on the " +
            $"observations card in this thread to enter its code. Check the card for your deadline.\n||{marker}||",
            marker, false, MentionUserId: ulong.Parse(post.OwnerId));
        var recovering = notice.SendPending;
        try
        {
            var delivery = await _discord.UpsertAsync(post.Id, notice.MessageId, recovering, notice.StartedAt ?? Now,
                message, () =>
                {
                    if (!recovering) notice.StartedAt = Now;
                    notice.SendPending = true; Save();
                }, ct);
            notice.MessageId = delivery.Id; notice.SendPending = false; notice.Fault = null;
            Save();
        }
        catch (Exception ex) when (ex is not IOException && ex is not OperationCanceledException)
        {
            // The public controls remain usable; an optional notification cannot reset the deadline
            // or prevent a timely acknowledgement. Retry/recover independently without a second ping.
            notice.Fault = RecruitmentFaultClassifier.From(ex, Now, notice.Fault);
            Save();
        }
    }

    public bool PinDue(Listing post) => ListingDeliveryPolicy.PinDue(post, Now);

    public async Task PinResponseAsync(Listing post, CancellationToken ct)
    {
        if (!PinDue(post)) return;
        if (post.PublicPin.MessageId != post.PublicMessageId)
        { post.PublicPin = new MessagePinReceipt { MessageId = post.PublicMessageId }; Save(); }
        var receipt = post.PublicPin;
        bool found;
        var persistenceFailed = false;
        try
        {
            found = await _discord.PinAdvisoryAsync(post.Id, receipt.MessageId!, receipt.RestoreArchive, receipt.RestoreLocked,
                (archive, locked) =>
                {
                    receipt.RestoreArchive = archive; receipt.RestoreLocked = locked;
                    try { Save(); }
                    catch (Exception ex)
                    { persistenceFailed = true; throw new System.IO.IOException("Could not persist response pin intent.", ex); }
                }, ct);
        }
        catch (Exception ex) when ((ex is not OperationCanceledException || !ct.IsCancellationRequested) && !persistenceFailed)
        {
            receipt.Fault = RecruitmentFaultClassifier.From(ex, Now, receipt.Fault);
            if (receipt.RestoreArchive && receipt.Fault.RetryAt is null)
                receipt.Fault = receipt.Fault with { RetryAt = Now.AddSeconds(RecruitmentBackoff.Seconds(receipt.Fault.Attempts, Random.Shared.Next(1, 6))) };
            Save(); return;
        }
        if (found)
        { receipt.Confirmed = true; receipt.RestoreArchive = receipt.RestoreLocked = false; receipt.Fault = null; }
        else
        { post.PublicMessageId = null; post.PublicationPending = true; _publicRenders.Remove(post.Id); post.PublicPin = new(); }
        Save();
    }

}
