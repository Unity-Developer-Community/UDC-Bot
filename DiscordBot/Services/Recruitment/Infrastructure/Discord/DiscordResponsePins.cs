using static DiscordBot.Services.Recruitment.RecruitmentRequests;

namespace DiscordBot.Services.Recruitment.Infrastructure.Discord;

internal static class DiscordResponsePins
{
    internal static async Task<bool> PinOwnedAsync(IThreadChannel channel, ulong botId, ulong messageId, bool? allowed,
        bool restoreArchive, bool restoreLocked, Action<bool, bool> beforeWrite, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        using var observation = RecruitmentMessageTransport.Observe(channel.Id);
        var message = await channel.GetMessageAsync(messageId, CacheMode.AllowDownload, Options(ct)) as IUserMessage;
        var archive = restoreArchive || channel.IsArchived;
        var locked = restoreArchive ? restoreLocked : channel.IsLocked;
        async Task RestoreAsync(bool force)
        {
            if (archive && (force || !channel.IsArchived || channel.IsLocked != locked))
            {
                beforeWrite(true, locked);
                await channel.ModifyAsync(p => { p.Archived = true; p.Locked = locked; }, Options(ct));
            }
            if (archive) beforeWrite(false, false); // Restoration is now durable, even if the pin failed.
        }
        // Restoration from an interrupted attempt is independent of pin capability.
        if (restoreArchive) await RestoreAsync(false);
        if (message is null) return false;
        if (!OwnedMessageLookup.OwnsMessage(message, botId, channel.Id, $"recruitment:{channel.Id}:public", RecruitmentMessageFormat.ComponentsV2))
            throw new InvalidOperationException("Response pin message identity does not match.");
        if (message.IsPinned) return true;
        if (allowed != true) throw new InvalidOperationException(allowed == false ? "Pin Messages permission missing" : "Pin Messages permission evidence unavailable");
        beforeWrite(archive, archive && locked);
        try
        {
            if (channel.IsArchived) await channel.ModifyAsync(p => p.Archived = false, Options(ct));
            using var pin = RecruitmentMessageTransport.Pin(channel.Id, messageId);
            await message.PinAsync(Options(ct));
        }
        finally
        {
            // A definite rejection must not leave the thread open indefinitely. Force the
            // restore because a lost unarchive response may leave the local SDK object stale.
            if (archive) await RestoreAsync(true);
        }
        return true;
    }
}
