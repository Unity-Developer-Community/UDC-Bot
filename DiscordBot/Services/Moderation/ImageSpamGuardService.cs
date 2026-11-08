using System.IO;
using System.Net.Http;
using Discord.Net;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Moderation;

/// <summary>
/// Catches the common scam wave: the same account posting messages with exactly N images
/// (default 4) in quick succession. The first such message is only remembered; another one in a
/// different channel within the window deletes every remembered message, times the member out,
/// posts a report to the log channel and a short pointer to it in the AutoMod channel.
/// </summary>
public class ImageSpamGuardService
{
    private const string ServiceName = "ImageSpamGuard";
    private const string AuditLogReason = "Image spam guard: repeated multi-image posts";
    private const string SeenReaction = "👀";
    private const string BannedReaction = "🔨";
    private const string ReactionLegend = $"-# React {SeenReaction} if you're on it, {BannedReaction} once banned";

    private readonly DiscordSocketClient _client;
    private readonly ILoggingService _loggingService;
    private readonly BotDeletionRegistry _deletionRegistry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ImageSpamGuardSettings _guardSettings;
    private readonly ulong _moderatorRoleId;
    private readonly ulong _autoModChannelId;
    private readonly ImageSpamTracker _tracker;

    public ImageSpamGuardService(DiscordSocketClient client, BotSettings settings, ILoggingService loggingService,
        BotDeletionRegistry deletionRegistry, IHttpClientFactory httpClientFactory)
    {
        _client = client;
        _loggingService = loggingService;
        _deletionRegistry = deletionRegistry;
        _httpClientFactory = httpClientFactory;
        _guardSettings = settings.ImageSpamGuard;
        _moderatorRoleId = settings.Roles.Moderator;
        _autoModChannelId = settings.Channels.AutoMod?.Id ?? 0;
        _tracker = new ImageSpamTracker(TimeSpan.FromMinutes(Math.Max(1, _guardSettings.WindowMinutes)));

        if (!_guardSettings.Enabled)
        {
            LoggingService.LogToConsole($"[{ServiceName}] Disabled in settings.", ExtendedLogSeverity.Info);
            return;
        }

        if (_autoModChannelId == 0)
            LoggingService.LogToConsole($"[{ServiceName}] Channels.AutoMod is not configured: reports won't be pointed to from AutoMod.",
                ExtendedLogSeverity.Warning);

        client.MessageReceived += EventGuard.Guarded<SocketMessage>(MessageReceived, nameof(MessageReceived));
    }

    private Task MessageReceived(SocketMessage message)
    {
        if (message is not SocketUserMessage userMessage || message.Author.IsUserBotOrWebhook())
            return Task.CompletedTask;
        if (message.Channel is not SocketGuildChannel || message.Author is not SocketGuildUser member || IsExempt(member))
            return Task.CompletedTask;

        var forwarded = userMessage.ForwardedMessages?.Select(f => f.Message).FirstOrDefault(m => m != null);
        var attachments = userMessage.Attachments.Count > 0 || forwarded == null ? userMessage.Attachments : forwarded.Attachments;
        var shapes = attachments.Select(a => new AttachmentShape(a.Filename, a.ContentType)).ToList();
        if (!ImageSpamShape.Matches(shapes, null, _guardSettings.ImageCount))
            return Task.CompletedTask;

        var text = string.IsNullOrWhiteSpace(message.Content) ? forwarded?.Content ?? string.Empty : message.Content;
        var hit = new ImageSpamHit(message.Channel.Id, message.Id, DateTimeOffset.UtcNow, text, shapes.Select(s => s.Filename).ToList());
        var result = _tracker.Record(member.Id, hit);

        switch (result.Outcome)
        {
            case ImageSpamOutcome.Triggered:
                ActOnSpam(member, attachments, result.Hits).SafeFireAndForget(ServiceName);
                break;
            case ImageSpamOutcome.AlreadyActioned:
                DeleteHits(result.Hits).SafeFireAndForget(ServiceName);
                break;
        }
        return Task.CompletedTask;
    }

    private bool IsExempt(SocketGuildUser member) =>
        member.GuildPermissions.Administrator
        || member.GuildPermissions.ManageMessages
        || (_moderatorRoleId != 0 && member.Roles.Any(r => r.Id == _moderatorRoleId));

    private async Task ActOnSpam(SocketGuildUser member, IReadOnlyCollection<IAttachment> lastAttachments, IReadOnlyList<ImageSpamHit> hits)
    {
        var timeoutResult = await TryTimeout(member);
        // Copy the image before deleting, since its link stops working once the message is gone.
        var (copy, copyNote) = await CopyOneImage(lastAttachments);
        List<FileAttachment> files = copy is { } copied ? [copied] : [];
        // The copy holds the image bytes in memory: release it however the rest goes.
        try
        {
            var (deleted, failures) = await DeleteHits(hits);

            LoggingService.LogToConsole(
                $"[{ServiceName}] Triggered for {member.GetUserLoggingString()}: deleted {deleted}/{hits.Count}, timeout: {timeoutResult}",
                ExtendedLogSeverity.Warning);

            var report = new AuditLogCards.ImageSpamReport(
                CardUser.From(member),
                _guardSettings.ImageCount,
                _guardSettings.WindowMinutes,
                hits.Select(h => new AuditLogCards.SpamMessage(h.ChannelId, h.MessageId, h.Text, h.FileNames)).ToList(),
                deleted,
                failures,
                timeoutResult,
                files.Select(f => $"attachment://{f.FileName}").ToList(),
                copyNote);

            IUserMessage? reportMessage;
            try
            {
                reportMessage = await _loggingService.LogComponentsToChannel(AuditLogCards.ImageSpamReportCard(report, DateTimeOffset.UtcNow), files);
            }
            catch (HttpException e) when (files.Count > 0)
            {
                // Most likely the upload was too large: send the report without the image.
                report = report with { CopiedImageUrls = [], CopyNote = $"Couldn't upload the copied image ({e.HttpCode})." };
                reportMessage = await _loggingService.LogComponentsToChannel(AuditLogCards.ImageSpamReportCard(report, DateTimeOffset.UtcNow));
            }

            await PostAutoModPointer(member, reportMessage);
        }
        finally
        {
            foreach (var file in files)
                file.Dispose();
        }
    }

    private async Task<string> TryTimeout(SocketGuildUser member)
    {
        var self = member.Guild.CurrentUser;
        if (!self.GuildPermissions.ModerateMembers)
            return "❌ not timed out: the bot is missing the Moderate Members permission";
        if (member.Hierarchy >= self.Hierarchy)
            return "❌ not timed out: the member's top role is not below the bot's";

        var duration = TimeSpan.FromHours(Math.Clamp(_guardSettings.TimeoutHours, 1, ImageSpamGuardSettings.MaxTimeoutHours));
        try
        {
            await member.SetTimeOutAsync(duration, new RequestOptions { AuditLogReason = AuditLogReason });
            return $"✅ {duration.TotalHours:0} hours, until {AuditLogCards.Timestamp(DateTimeOffset.UtcNow + duration)}";
        }
        catch (HttpException e)
        {
            return $"❌ not timed out: Discord returned {(int)e.HttpCode} {e.Reason}".TrimEnd();
        }
    }

    private async Task PostAutoModPointer(SocketGuildUser member, IUserMessage? reportMessage)
    {
        if (_autoModChannelId == 0)
            return;
        if (reportMessage == null)
        {
            LoggingService.LogToConsole($"[{ServiceName}] No report was posted, so there is nothing to point to from AutoMod.",
                ExtendedLogSeverity.Warning);
            return;
        }
        // Looked up when needed: the channel cache may not be ready when the service is created.
        if (_client.GetChannel(_autoModChannelId) is not IMessageChannel autoMod)
        {
            LoggingService.LogToConsole($"[{ServiceName}] AutoMod channel {_autoModChannelId} not found.", ExtendedLogSeverity.Warning);
            return;
        }

        IUserMessage pointer;
        try
        {
            pointer = await autoMod.SendMessageAsync(
                $"🚨 Image spam guard triggered for {member.Mention} · [See Details]({reportMessage.GetJumpUrl()})\n{ReactionLegend}",
                allowedMentions: AllowedMentions.None);
        }
        catch (HttpException e)
        {
            LoggingService.LogToConsole($"[{ServiceName}] Couldn't post to the AutoMod channel: {(int)e.HttpCode} {e.Reason}".TrimEnd(),
                ExtendedLogSeverity.Warning);
            return;
        }

        // The reactions are only markers for moderators; a failure here must not affect anything else.
        try
        {
            await pointer.AddReactionAsync(new Emoji(SeenReaction));
            await pointer.AddReactionAsync(new Emoji(BannedReaction));
        }
        catch (HttpException e)
        {
            LoggingService.LogToConsole($"[{ServiceName}] Couldn't add reactions to the AutoMod alert: {(int)e.HttpCode} {e.Reason}".TrimEnd(),
                ExtendedLogSeverity.Warning);
        }
    }

    private async Task<(FileAttachment? File, string? Note)> CopyOneImage(IReadOnlyCollection<IAttachment> attachments)
    {
        const string failed = "Couldn't copy an image (too large, unsupported or no longer available).";
        var list = attachments.ToList();
        if (ImageSpamThumbnail.PickIndex(list.Select(a => (long)a.Size).ToList(), Random.Shared) is not { } index)
            return (null, failed);

        byte[]? bytes;
        using (var http = _httpClientFactory.CreateClient())
        {
            http.Timeout = TimeSpan.FromSeconds(15);
            // Attachment.Size is what Discord reported: cap what is actually read too.
            http.MaxResponseContentBufferSize = ImageSpamThumbnail.MaxSourceBytes;
            bytes = await TryDownload(http, list[index].Url);
        }
        if (bytes == null)
            return (null, failed);

        // Production has half a CPU: keep the decode off the gateway thread.
        var thumbnail = await Task.Run(() => ImageSpamThumbnail.TryCreate(bytes));
        if (thumbnail == null)
            return (null, failed);

        var note = list.Count > 1 ? $"Showing 1 of {list.Count} images, picked at random and resized." : null;
        // FileAttachment.Dispose disposes the stream; ActOnSpam disposes it.
        return (new FileAttachment(new MemoryStream(thumbnail), "spam.jpg"), note);
    }

    private static async Task<byte[]?> TryDownload(HttpClient http, string url)
    {
        try
        {
            return await http.GetByteArrayAsync(url);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private async Task<(int Deleted, List<string> Failures)> DeleteHits(IReadOnlyList<ImageSpamHit> hits)
    {
        // Register first: the gateway delete event can arrive before the REST call returns.
        _deletionRegistry.Register(hits.Select(h => h.MessageId), DateTimeOffset.UtcNow);

        var deleted = 0;
        var failures = new List<string>();
        foreach (var hit in hits)
        {
            if (_client.GetChannel(hit.ChannelId) is not IMessageChannel channel)
            {
                failures.Add($"<#{hit.ChannelId}> not found");
                continue;
            }
            try
            {
                await channel.DeleteMessageAsync(hit.MessageId, new RequestOptions { AuditLogReason = AuditLogReason });
                deleted++;
            }
            catch (HttpException e) when (e.DiscordCode == DiscordErrorCode.UnknownMessage)
            {
                // Already deleted by the author or a moderator.
                deleted++;
            }
            catch (HttpException e)
            {
                failures.Add($"<#{hit.ChannelId}>: {(int)e.HttpCode}");
            }
        }
        return (deleted, failures);
    }
}
