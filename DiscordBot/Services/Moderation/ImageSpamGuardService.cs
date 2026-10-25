using System.IO;
using System.Net.Http;
using Discord.Net;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Moderation;

/// <summary>
/// Catches the common scam wave: the same account posting messages with exactly N images
/// (default 4) in quick succession. The first such message is only remembered; another one in a
/// different channel within the window deletes every remembered message, times the member out and
/// posts a report.
/// </summary>
public class ImageSpamGuardService
{
    private const string ServiceName = "ImageSpamGuard";
    private const string AuditLogReason = "Image spam guard: repeated multi-image posts";
    // Stay under Discord's default 10 MB upload limit when copying images into the report.
    private const long MaxCopyBytes = 8 * 1024 * 1024;

    private readonly DiscordSocketClient _client;
    private readonly ILoggingService _loggingService;
    private readonly BotDeletionRegistry _deletionRegistry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ImageSpamGuardSettings _guardSettings;
    private readonly ulong _moderatorRoleId;
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
        _tracker = new ImageSpamTracker(TimeSpan.FromMinutes(Math.Max(1, _guardSettings.WindowMinutes)));

        if (!_guardSettings.Enabled)
        {
            LoggingService.LogToConsole($"[{ServiceName}] Disabled in settings.", ExtendedLogSeverity.Info);
            return;
        }

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
        // Copy the images before deleting, since their links stop working once the message is gone.
        var (files, copyNote) = await CopyImages(lastAttachments);
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

        try
        {
            await _loggingService.LogComponentsToChannel(AuditLogCards.ImageSpamReportCard(report, DateTimeOffset.UtcNow), files);
        }
        catch (HttpException e) when (files.Count > 0)
        {
            // Most likely the upload was too large: send the report without the images.
            report = report with { CopiedImageUrls = [], CopyNote = $"Couldn't upload the copied images ({e.HttpCode})." };
            await _loggingService.LogComponentsToChannel(AuditLogCards.ImageSpamReportCard(report, DateTimeOffset.UtcNow));
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

    private async Task<(List<FileAttachment> Files, string? Note)> CopyImages(IReadOnlyCollection<IAttachment> attachments)
    {
        var files = new List<FileAttachment>();
        long total = 0;
        var skipped = 0;
        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(15);

        foreach (var attachment in attachments)
        {
            if (total + attachment.Size > MaxCopyBytes)
            {
                skipped++;
                continue;
            }
            try
            {
                var bytes = await http.GetByteArrayAsync(attachment.Url);
                total += bytes.Length;
                var extension = Path.GetExtension(attachment.Filename);
                if (extension.Length is 0 or > 6)
                    extension = ".png";
                files.Add(new FileAttachment(new MemoryStream(bytes), $"spam-{files.Count + 1}{extension.ToLowerInvariant()}"));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                skipped++;
            }
        }

        var note = skipped == 0 ? null
            : files.Count == 0 ? "Couldn't copy the images (too large or no longer available)."
            : $"{skipped} image{(skipped == 1 ? "" : "s")} not copied (too large or no longer available).";
        return (files, note);
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
