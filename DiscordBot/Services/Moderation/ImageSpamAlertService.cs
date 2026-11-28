using Discord.Net;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Moderation;

public enum ImageSpamAlertAction
{
    /// <summary>Leave the timeout to run.</summary>
    Ignore,
    /// <summary>Ban the member now.</summary>
    Ban,
    /// <summary>Remove the timeout: the guard was wrong.</summary>
    Unmute,
}

/// <summary>
/// Custom ids of the alert buttons: <c>imgspam:&lt;action&gt;:&lt;userId&gt;</c>. They hold no state, so the
/// buttons keep working after a restart.
/// </summary>
public static class ImageSpamAlertIds
{
    public const string Prefix = "imgspam";

    public static string Build(ImageSpamAlertAction action, ulong userId) => $"{Prefix}:{Name(action)}:{userId}";

    public static bool TryParse(string? customId, out ImageSpamAlertAction action, out ulong userId)
    {
        action = default;
        userId = 0;
        var parts = customId?.Split(':');
        if (parts is not [Prefix, var name, var id] || !ulong.TryParse(id, out userId) || userId == 0)
            return false;
        switch (name)
        {
            case "ignore": action = ImageSpamAlertAction.Ignore; return true;
            case "ban": action = ImageSpamAlertAction.Ban; return true;
            case "unmute": action = ImageSpamAlertAction.Unmute; return true;
            default: return false;
        }
    }

    private static string Name(ImageSpamAlertAction action) => action switch
    {
        ImageSpamAlertAction.Ignore => "ignore",
        ImageSpamAlertAction.Ban => "ban",
        ImageSpamAlertAction.Unmute => "unmute",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}

/// <summary>
/// The AutoMod side of the image spam guard: looks up the member's earlier messages with Discord's message
/// search (live, nothing is stored) and posts the alert card with the moderator buttons.
/// </summary>
public class ImageSpamAlertService
{
    private const string ServiceName = "ImageSpamAlert";
    private static readonly TimeSpan LookupCap = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    private readonly DiscordSocketClient _client;
    private readonly ulong _autoModChannelId;

    public ImageSpamAlertService(DiscordSocketClient client, BotSettings settings)
    {
        _client = client;
        _autoModChannelId = settings.Channels.AutoMod?.Id ?? 0;
    }

    /// <summary>
    /// Searches the member's messages from before the spam: those with an id below the oldest spam message, so the
    /// spam itself stays out even while the search index lags behind the deletes. Never throws.
    /// </summary>
    public async Task<HistorySearch> LookupHistory(SocketGuildUser member, IReadOnlyList<ImageSpamHit> hits)
    {
        using var cap = new CancellationTokenSource(LookupCap);
        try
        {
            var args = new SearchGuildMessages
            {
                AuthorIds = new[] { member.Id },
                Limit = ImageSpamHistory.MaxExcerpts,
                SortDirection = SortDirection.Descending,
                SortAlgorithm = SortAlgorithm.Timestamp,
            };
            if (hits.Count > 0)
                args.MaxMessageId = hits.Min(h => h.MessageId) - 1;

            var data = await Search(member.Guild, args, cap.Token);
            if (data.IndexNotYetAvailable)
            {
                var delay = TimeSpan.FromSeconds(data.RetryAfter ?? DefaultRetryDelay.TotalSeconds);
                await Task.Delay(delay > MaxRetryDelay ? MaxRetryDelay : delay, cap.Token);
                data = await Search(member.Guild, args, cap.Token);
                if (data.IndexNotYetAvailable)
                    return HistorySearch.Unavailable("Discord is still indexing this server's messages");
            }

            var messages = data.Messages
                .Select(m => new HistoryMessage(m.Channel?.Id ?? 0, m.Id, m.Timestamp, m.Content ?? string.Empty,
                    m.Attachments.Count, m.Embeds.Count, m.Stickers.Count))
                .ToList();
            return new HistorySearch(data.TotalResults, messages);
        }
        catch (OperationCanceledException)
        {
            return HistorySearch.Unavailable("the search timed out");
        }
        catch (HttpException e)
        {
            LoggingService.LogToConsole($"[{ServiceName}] History search failed: {(int)e.HttpCode} {e.Reason}".TrimEnd(), ExtendedLogSeverity.Warning);
            return HistorySearch.Unavailable($"Discord returned {(int)e.HttpCode}");
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[{ServiceName}] History search failed: {e}", ExtendedLogSeverity.Warning);
            return HistorySearch.Unavailable("the search failed");
        }
    }

    private static Task<GuildMessageSearchData> Search(IGuild guild, SearchGuildMessages args, CancellationToken cancel) =>
        guild.SearchMessagesAsync(args, CacheMode.CacheOnly, new RequestOptions { CancelToken = cancel });

    /// <summary>Posts the alert card in the AutoMod channel. A failure to post is logged, not thrown.</summary>
    public async Task PostAlert(SocketGuildUser member, string rule, int messageCount, int windowMinutes, string timeoutResult,
        IUserMessage? reportMessage, Task<HistorySearch> history)
    {
        if (_autoModChannelId == 0)
            return;
        // Looked up when needed: the channel cache may not be ready when the service is created.
        if (_client.GetChannel(_autoModChannelId) is not IMessageChannel autoMod)
        {
            LoggingService.LogToConsole($"[{ServiceName}] AutoMod channel {_autoModChannelId} not found.", ExtendedLogSeverity.Warning);
            return;
        }

        var search = await history;
        var alert = new AuditLogCards.ImageSpamAlert(
            CardUser.From(member),
            member.JoinedAt,
            member.CreatedAt,
            rule,
            messageCount,
            windowMinutes,
            timeoutResult,
            reportMessage?.GetJumpUrl(),
            ImageSpamHistory.Lines(search, DateTimeOffset.UtcNow, member.Guild.Id));

        try
        {
            await autoMod.SendMessageAsync(components: AuditLogCards.ImageSpamAlertCard(alert), flags: MessageFlags.ComponentsV2,
                allowedMentions: AllowedMentions.None);
        }
        catch (HttpException e)
        {
            LoggingService.LogToConsole($"[{ServiceName}] Couldn't post to the AutoMod channel: {(int)e.HttpCode} {e.Reason}".TrimEnd(),
                ExtendedLogSeverity.Warning);
        }
    }
}
