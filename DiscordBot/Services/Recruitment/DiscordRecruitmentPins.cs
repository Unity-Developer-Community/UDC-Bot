using Discord.Rest;

namespace DiscordBot.Services.Recruitment;

public sealed partial class DiscordRecruitmentAdapter
{
    public async Task<bool> PinAdvisoryAsync(string threadId, string messageId, bool restoreArchive, bool restoreLocked,
        Action<bool, bool> beforeWrite, CancellationToken ct)
    {
        // A fresh channel is essential after a lost unarchive/pin response.
        var channel = await _client.Rest.GetChannelAsync(ulong.Parse(threadId), Options(ct)) as RestThreadChannel;
        if (channel is null || channel.GuildId != _settings.GuildId ||
            !_settings.Recruitment.Forums.Mapping().Values.Contains(channel.ParentChannelId))
            throw new InvalidOperationException("Response pin thread unavailable.");
        var guild = _client.GetGuild(_settings.GuildId);
        var parent = guild?.GetChannel(channel.ParentChannelId);
        bool? allowed = parent is null ? null : guild!.CurrentUser.GetPermissions(parent).PinMessages;
        return await PinOwnedAsync(channel, BotUserId, ulong.Parse(messageId), allowed, restoreArchive, restoreLocked, beforeWrite, ct);
    }

    internal static async Task<bool> PinOwnedAsync(IThreadChannel channel, ulong botId, ulong messageId, bool? allowed,
        bool restoreArchive, bool restoreLocked, Action<bool, bool> beforeWrite, CancellationToken ct)
    {
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
        if (!OwnsMessage(message, botId, channel.Id, $"recruitment:{channel.Id}:public", RecruitmentMessageFormat.ComponentsV2))
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
