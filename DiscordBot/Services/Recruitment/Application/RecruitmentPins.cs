namespace DiscordBot.Services.Recruitment;

public sealed partial class RecruitmentEngine
{
    private bool PinDue(Listing post) => post.Status is ListingStatus.Open or ListingStatus.Closed &&
        post.PublicMessageId is not null &&
        (post.PublicPin.MessageId != post.PublicMessageId ||
         (!post.PublicPin.Confirmed || post.PublicPin.RestoreArchive) && (post.PublicPin.Fault is null || post.PublicPin.Fault.IsDue(Now)));

    private async Task PinResponseAsync(Listing post, CancellationToken ct)
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
            receipt.Fault = RecruitmentFault.From(ex, Now, receipt.Fault);
            if (receipt.RestoreArchive && receipt.Fault.RetryAt is null)
                receipt.Fault = receipt.Fault with { RetryAt = Now.AddSeconds(Math.Min(900, 15 * Math.Pow(2, receipt.Fault.Attempts - 1)) + Random.Shared.Next(1, 6)) };
            Save(); return;
        }
        if (found)
        { receipt.Confirmed = true; receipt.RestoreArchive = receipt.RestoreLocked = false; receipt.Fault = null; }
        else
        { post.PublicMessageId = null; post.PublicationPending = true; _publicRenders.Remove(post.Id); post.PublicPin = new(); }
        Save();
    }
}
