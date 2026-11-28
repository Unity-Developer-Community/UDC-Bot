using System.Collections.Concurrent;
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
    private readonly ulong _moderatorRoleId;
    // Alert messages whose button is being handled or was handled. A handled alert stays in the set, so a click that
    // lands before the card loses its buttons (or after the card edit failed) does nothing.
    private readonly AlertClaims _claimed = new();

    public ImageSpamAlertService(DiscordSocketClient client, BotSettings settings)
    {
        _client = client;
        _autoModChannelId = settings.Channels.AutoMod?.Id ?? 0;
        _moderatorRoleId = settings.Roles.Moderator;
    }

    /// <summary>Whether an alert is posted for a trigger (the AutoMod channel is set).</summary>
    public bool PostsAlerts => _autoModChannelId != 0;

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
                // A 0 means "retry after a short delay", not "now".
                var delay = data.RetryAfter is > 0 ? TimeSpan.FromSeconds(data.RetryAfter.Value) : DefaultRetryDelay;
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

    /// <summary>Moderators (the Moderator role) and administrators may use the buttons.</summary>
    public static bool MayAct(bool isAdministrator, IEnumerable<ulong> roleIds, ulong moderatorRoleId) =>
        isAdministrator || (moderatorRoleId != 0 && roleIds.Contains(moderatorRoleId));

    /// <summary>Whether the message still has alert buttons, that is, no one has acted on it yet.</summary>
    public static bool HasAlertButtons(IEnumerable<IMessageComponent> components) =>
        components.Any(c => c switch
        {
            ButtonComponent b => b.CustomId?.StartsWith(ImageSpamAlertIds.Prefix + ":", StringComparison.Ordinal) == true,
            ActionRowComponent row => HasAlertButtons(row.Components),
            ContainerComponent container => HasAlertButtons(container.Components),
            _ => false,
        });

    /// <summary>What the closed card says happened.</summary>
    public static string ResultText(ImageSpamAlertAction action, bool memberLeft, DateTimeOffset? timedOutUntil, DateTimeOffset now) =>
        action switch
        {
            ImageSpamAlertAction.Ban => "banned",
            _ when memberLeft => "member has left the server",
            ImageSpamAlertAction.Unmute => "timeout removed",
            _ => timedOutUntil is { } until && until > now ? $"left timed out until {AuditLogCards.Timestamp(until)}" : "member is not timed out",
        };

    /// <summary>
    /// Runs a button on an alert. On success the result holds the closed card to put on the alert message; on
    /// failure it holds the error for the moderator, and the buttons stay.
    /// </summary>
    public async Task<ImageSpamAlertOutcome> HandleAsync(ImageSpamAlertAction action, ulong userId, IGuildUser clicker, IUserMessage message)
    {
        if (!MayAct(clicker.GuildPermissions.Administrator, clicker.RoleIds, _moderatorRoleId))
            return ImageSpamAlertOutcome.Failed("Only moderators can use these buttons.");
        if (!HasAlertButtons(message.Components))
            return ImageSpamAlertOutcome.Failed("This alert has already been handled.");
        switch (_claimed.TryClaim(message.Id))
        {
            case AlertClaim.Handled:
                return ImageSpamAlertOutcome.Failed("This alert has already been handled.");
            case AlertClaim.InProgress:
                return ImageSpamAlertOutcome.Failed("This alert is already being handled.");
        }

        var handled = false;
        try
        {
            var guild = _client.GetGuild(clicker.GuildId);
            if (guild == null)
                return ImageSpamAlertOutcome.Failed("I couldn't find the server.");

            var result = await Execute(action, userId, guild, clicker);
            if (result.Error != null)
                return ImageSpamAlertOutcome.Failed(result.Error);

            LoggingService.LogToConsole($"[{ServiceName}] {action} on {userId} by {clicker.Id}: {result.Text}", ExtendedLogSeverity.Info);
            var now = DateTimeOffset.UtcNow;
            var line = AuditLogCards.ImageSpamClosedLine(action, clicker.Id, result.Text!, now);
            handled = true;
            _claimed.MarkHandled(message.Id);
            return ImageSpamAlertOutcome.Closed(AuditLogCards.ImageSpamClosedCard(message.Components, line), result.Text!);
        }
        catch (HttpException e)
        {
            LoggingService.LogToConsole($"[{ServiceName}] {action} on {userId} failed: {(int)e.HttpCode} {e.Reason}".TrimEnd(), ExtendedLogSeverity.Warning);
            return ImageSpamAlertOutcome.Failed($"Discord refused: {(int)e.HttpCode} {e.Reason}".TrimEnd());
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[{ServiceName}] {action} on {userId} failed: {e}", ExtendedLogSeverity.Warning);
            return ImageSpamAlertOutcome.Failed("Something went wrong, please try again.");
        }
        finally
        {
            // A failed action frees the alert for another try; a handled one stays claimed.
            if (!handled)
                _claimed.Release(message.Id);
        }
    }

    private async Task<(string? Text, string? Error)> Execute(ImageSpamAlertAction action, ulong userId, SocketGuild guild, IGuildUser clicker)
    {
        var self = guild.CurrentUser;
        var member = await FindMember(guild, userId);
        var now = DateTimeOffset.UtcNow;

        switch (action)
        {
            case ImageSpamAlertAction.Ignore:
                return (ResultText(action, member == null, member?.TimedOutUntil, now), null);

            case ImageSpamAlertAction.Ban:
                if (!self.GuildPermissions.BanMembers)
                    return (null, "I'm missing the Ban Members permission.");
                if (member != null && member.Hierarchy >= self.Hierarchy)
                    return (null, "The member's top role is not below mine.");
                // Deletes none of their messages: the guard already removed the spam.
                await guild.AddBanAsync(userId, 0, $"Image spam guard: banned by {clicker.Username} from the AutoMod alert");
                return (ResultText(action, member == null, null, now), null);

            case ImageSpamAlertAction.Unmute:
                if (member == null)
                    return (ResultText(action, true, null, now), null);
                if (!self.GuildPermissions.ModerateMembers)
                    return (null, "I'm missing the Moderate Members permission.");
                if (member.Hierarchy >= self.Hierarchy)
                    return (null, "The member's top role is not below mine.");
                await member.RemoveTimeOutAsync(new RequestOptions { AuditLogReason = $"Image spam guard: false positive, marked by {clicker.Username}" });
                return (ResultText(action, false, null, now), null);

            default:
                return (null, "Unknown action.");
        }
    }

    /// <summary>The member, or null when they have left (or were never in) the server.</summary>
    private async Task<IGuildUser?> FindMember(SocketGuild guild, ulong userId) =>
        guild.GetUser(userId) ?? (IGuildUser?)await _client.Rest.GetGuildUserAsync(guild.Id, userId);
}

/// <summary>
/// The result of a button: the closed card to show and what happened (<see cref="ResultText"/>), or an error for the
/// moderator.
/// </summary>
public sealed record ImageSpamAlertOutcome(MessageComponent? ClosedCard, string? Error, string? ResultText = null)
{
    public static ImageSpamAlertOutcome Closed(MessageComponent card, string resultText) => new(card, null, resultText);
    public static ImageSpamAlertOutcome Failed(string error) => new(null, error);
}

/// <summary>What <see cref="AlertClaims.TryClaim"/> found.</summary>
public enum AlertClaim
{
    /// <summary>The caller now holds the alert.</summary>
    Claimed,
    /// <summary>Another click is running its action.</summary>
    InProgress,
    /// <summary>An action went through already.</summary>
    Handled,
}

/// <summary>
/// The alert messages whose button is running or has run. A handled alert stays in the set. One entry per alert,
/// and alerts are rare. Thread-safe.
/// </summary>
public sealed class AlertClaims
{
    private readonly ConcurrentDictionary<ulong, bool> _handled = new();

    public AlertClaim TryClaim(ulong messageId)
    {
        if (_handled.TryAdd(messageId, false))
            return AlertClaim.Claimed;
        return _handled.TryGetValue(messageId, out var handled) && handled ? AlertClaim.Handled : AlertClaim.InProgress;
    }

    /// <summary>The action went through: the alert stays claimed for good.</summary>
    public void MarkHandled(ulong messageId) => _handled[messageId] = true;

    /// <summary>The action failed: free the alert for another try.</summary>
    public void Release(ulong messageId) => _handled.TryRemove(messageId, out _);
}
