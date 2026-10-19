using DiscordBot.Services.Recruitment.Infrastructure.Discord;
using System.IO;

namespace DiscordBot.Services.Recruitment;

public sealed partial class RecruitmentEngine
{
    private DateTimeOffset? AcknowledgementNoticeDue(Listing post) =>
        post.Status == ListingStatus.Open && !post.AcceptedAt.HasValue && !post.AcknowledgedAt.HasValue &&
        post.PublicMessageId is not null && post.Deadline > Now &&
        (post.AcknowledgementNotice.MessageId is null || post.AcknowledgementNotice.SendPending)
            ? post.AcknowledgementNotice.Fault is { } fault ? fault.RetryAt : Now : null;

    private async Task PublishAcknowledgementNoticeAsync(Listing post, CancellationToken ct)
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
}
