using System.Collections.Concurrent;
using System.Globalization;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Services.Moderation;
using DiscordBot.Settings;

namespace DiscordBot.Services.Server;

public class AuditLogService
{
    private readonly ILoggingService _loggingService;
    private readonly DiscordSocketClient _client;
    private readonly BotSettings _settings;
    private readonly BotDeletionRegistry _botDeletions;

    // Uncached edits already logged (message ID -> edited time), so the unfurl update after an edit isn't logged twice.
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _uncachedEditsLogged = new();
    private int _auditLogWarned;

    private readonly IMessageChannel? _botAnnouncementChannel = null!;

    public AuditLogService(DiscordSocketClient client, BotSettings settings, ILoggingService loggingService, BotDeletionRegistry botDeletions)
    {
        _botDeletions = botDeletions;
        _loggingService = loggingService;
        _client = client;
        _settings = settings;

        client.MessageDeleted += EventGuard.Guarded<Cacheable<IMessage, ulong>, Cacheable<IMessageChannel, ulong>>(MessageDeleted, nameof(MessageDeleted));
        client.MessagesBulkDeleted += EventGuard.Guarded<IReadOnlyCollection<Cacheable<IMessage, ulong>>, Cacheable<IMessageChannel, ulong>>(MessagesBulkDeleted, nameof(MessagesBulkDeleted));
        client.MessageUpdated += EventGuard.Guarded<Cacheable<IMessage, ulong>, SocketMessage, ISocketMessageChannel>(MessageUpdated, nameof(MessageUpdated));
        client.ThreadDeleted += EventGuard.Guarded<Cacheable<SocketThreadChannel, ulong>>(ThreadDeleted, nameof(ThreadDeleted));
        client.UserLeft += EventGuard.Guarded<SocketGuild, SocketUser>(UserLeft, nameof(UserLeft));
        client.GuildMemberUpdated += EventGuard.Guarded<Cacheable<SocketGuildUser, ulong>, SocketGuildUser>(GuildMemberUpdated, nameof(GuildMemberUpdated));

        if (settings.Channels.BotAnnouncement != null)
            _botAnnouncementChannel = client.GetChannel(settings.Channels.BotAnnouncement.Id) as IMessageChannel;
    }

    private async Task MessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        // Messages removed by the spam guard are already in its report.
        if (channel.Id == _botAnnouncementChannel?.Id || _botDeletions.Contains(message.Id, DateTimeOffset.UtcNow))
            return;

        if (message.HasValue == false)
        {
            // Don't download the channel: it may be a thread that was just deleted.
            IChannel? cachedChannel = channel.HasValue ? channel.Value : _client.GetChannel(channel.Id);
            if (cachedChannel is IPrivateChannel)
                return;
            var cardChannel = cachedChannel != null ? CardChannel.From(cachedChannel) : new CardChannel(channel.Id, null);
            await _loggingService.LogComponentsToChannel(
                AuditLogCards.UncachedDeletedMessageCard(message.Id, cardChannel, DateTimeOffset.UtcNow));
            return;
        }

        var deleted = message.Value;
        if (deleted.Author.IsBot || deleted.Channel is not IGuildChannel guildChannel)
            return;

        var card = AuditLogCards.DeletedMessageCard(ToDeletedMessage(deleted, guildChannel.Guild), DateTimeOffset.UtcNow);
        await _loggingService.LogComponentsToChannel(card);
    }

    private async Task MessagesBulkDeleted(IReadOnlyCollection<Cacheable<IMessage, ulong>> messages, Cacheable<IMessageChannel, ulong> channel)
    {
        if (channel.Id == _botAnnouncementChannel?.Id)
            return;

        IChannel? cachedChannel = channel.HasValue ? channel.Value : _client.GetChannel(channel.Id);
        if (cachedChannel is IPrivateChannel)
            return;
        var guild = (cachedChannel as IGuildChannel)?.Guild;

        var now = DateTimeOffset.UtcNow;
        messages = messages.Where(m => !_botDeletions.Contains(m.Id, now)).ToList();
        var cached = messages.Where(m => m.HasValue).Select(m => m.Value).OrderBy(m => m.Timestamp).ToList();
        var uncachedCount = messages.Count - cached.Count;
        var listed = cached
            .Where(m => !m.Author.IsBot)
            .Select(m => new AuditLogCards.BulkDeletedMessage(AuthorCard(m.Author, guild), m.Content, m.Attachments.Count))
            .ToList();
        // Bot messages count towards the total but aren't listed.
        uncachedCount += cached.Count - listed.Count;
        if (listed.Count == 0 && uncachedCount == 0)
            return;

        var cardChannel = cachedChannel != null ? CardChannel.From(cachedChannel) : new CardChannel(channel.Id, null);
        await _loggingService.LogComponentsToChannel(
            AuditLogCards.BulkDeletedCard(cardChannel, listed, uncachedCount, DateTimeOffset.UtcNow));
    }

    private static CardUser AuthorCard(IUser author, IGuild? guild)
    {
        // Deletions from people who left or were banned are what scam waves look like, so call it out.
        var inServer = guild is not SocketGuild socketGuild || socketGuild.GetUser(author.Id) != null;
        return CardUser.From(author, inServer ? null : "no longer in the server");
    }

    private static AuditLogCards.DeletedMessage ToDeletedMessage(IMessage message, IGuild guild)
    {
        var forwarded = (message as IUserMessage)?.ForwardedMessages?.Select(f => f.Message).FirstOrDefault(m => m != null);
        var attachments = message.Attachments.Count > 0 || forwarded == null ? message.Attachments : forwarded.Attachments;

        string? replyTo = null;
        if (message.Type == MessageType.Reply && message.Reference?.MessageId.IsSpecified == true)
            replyTo = AuditLogCards.JumpUrl(guild.Id, message.Reference.ChannelId, message.Reference.MessageId.Value);

        return new AuditLogCards.DeletedMessage(
            AuthorCard(message.Author, guild),
            CardChannel.From(message.Channel),
            message.Id,
            message.Timestamp,
            message.Content,
            attachments.Select(a => new CardAttachment(a.Filename, a.Size)).ToList(),
            message.Stickers.Select(st => st.Name).ToList(),
            replyTo,
            forwarded?.Content);
    }

    private async Task MessageUpdated(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        if (after.Author.IsBot || channel.Id == _botAnnouncementChannel?.Id || channel is not SocketGuildChannel guildChannel)
            return;

        // Never download "before": that returns the edited message and made unfurls look like edits.
        var beforeVersion = before.HasValue ? MessageVersion.From(before.Value) : null;
        var afterVersion = MessageVersion.From(after);
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? lastLogged = _uncachedEditsLogged.TryGetValue(after.Id, out var logged) ? logged : null;

        var decision = MessageEditClassifier.Classify(beforeVersion, afterVersion, now, lastLogged);
        if (decision.Kind == EditKind.Ignore)
            return;
        if (decision.Kind == EditKind.EditedUncached)
            RememberUncachedEdit(after.Id, afterVersion.EditedAt!.Value, now);

        var card = AuditLogCards.EditedMessageCard(new AuditLogCards.EditedMessage(
            AuthorCard(after.Author, guildChannel.Guild),
            CardChannel.From(channel),
            after.Id,
            after.GetJumpUrl(),
            after.Timestamp,
            afterVersion.EditedAt!.Value,
            beforeVersion?.Content,
            after.Content,
            decision.Removed,
            decision.Added));
        await _loggingService.LogComponentsToChannel(card);
    }

    private void RememberUncachedEdit(ulong messageId, DateTimeOffset editedAt, DateTimeOffset now)
    {
        _uncachedEditsLogged[messageId] = editedAt;
        if (_uncachedEditsLogged.Count <= 200)
            return;
        foreach (var entry in _uncachedEditsLogged.Where(e => now - e.Value > MessageEditClassifier.UncachedEditMaxAge).ToList())
            _uncachedEditsLogged.TryRemove(entry.Key, out _);
    }

    private Task ThreadDeleted(Cacheable<SocketThreadChannel, ulong> thread)
    {
        // The audit-log lookup waits a moment, so don't hold up the gateway.
        LogThreadDeletion(thread.Id, thread.HasValue ? thread.Value : null, DateTimeOffset.UtcNow)
            .SafeFireAndForget(nameof(ThreadDeleted));
        return Task.CompletedTask;
    }

    private async Task LogThreadDeletion(ulong threadId, SocketThreadChannel? thread, DateTimeOffset deletedAt)
    {
        var guild = thread?.Guild ?? _client.GetGuild(_settings.GuildId);
        if (guild == null)
            return;

        var (deletedBy, auditData) = await FindThreadDeleteAudit(guild, threadId);

        // Tag IDs are unique, so they also tell us the forum when the thread wasn't cached.
        var tagIds = thread?.AppliedTags ?? auditData?.AppliedTags ?? [];
        var parent = thread?.ParentChannel as SocketGuildChannel
                     ?? guild.ForumChannels.FirstOrDefault(f => f.Tags.Any(t => tagIds.Contains(t.Id)));
        var forum = parent as SocketForumChannel;
        var tags = forum?.Tags.Where(t => tagIds.Contains(t.Id)).Select(t => t.Name).ToList() ?? [];

        CardUser? owner = null;
        ulong? ownerId = (thread as IThreadChannel)?.OwnerId;
        if (ownerId != null)
        {
            IUser? ownerUser = guild.GetUser(ownerId.Value) ?? (IUser?)_client.GetUser(ownerId.Value);
            if (ownerUser != null)
                owner = AuthorCard(ownerUser, guild);
        }

        var starter = thread?.GetCachedMessage(threadId);
        var info = new AuditLogCards.DeletedThread(
            threadId,
            thread?.Name ?? auditData?.ThreadName,
            forum != null,
            parent != null ? CardChannel.From(parent) : null,
            owner,
            ownerId,
            tags,
            thread?.MessageCount,
            starter?.Content,
            starter?.Attachments.Select(a => new CardAttachment(a.Filename, a.Size)).ToList() ?? [],
            deletedBy != null ? CardUser.From(deletedBy) : null,
            thread != null);

        await _loggingService.LogComponentsToChannel(AuditLogCards.DeletedThreadCard(info, deletedAt));
    }

    private async Task<(IUser? DeletedBy, ThreadDeleteAuditLogData? Data)> FindThreadDeleteAudit(SocketGuild guild, ulong threadId)
    {
        if (!guild.CurrentUser.GuildPermissions.ViewAuditLog)
        {
            WarnAuditLogOnce("missing the View Audit Log permission");
            return default;
        }

        // The audit-log entry can show up a little after the gateway event.
        await Task.Delay(TimeSpan.FromSeconds(2));
        try
        {
            var entries = await guild.GetAuditLogsAsync(10, actionType: ActionType.ThreadDelete).FlattenAsync();
            var entry = entries.FirstOrDefault(e =>
                e.Data is ThreadDeleteAuditLogData data && data.ThreadId == threadId &&
                DateTimeOffset.UtcNow - e.CreatedAt < TimeSpan.FromSeconds(30));
            return (entry?.User, entry?.Data as ThreadDeleteAuditLogData);
        }
        catch (HttpException e)
        {
            WarnAuditLogOnce($"audit log request failed ({e.HttpCode})");
            return default;
        }
    }

    private void WarnAuditLogOnce(string reason)
    {
        if (Interlocked.Exchange(ref _auditLogWarned, 1) == 0)
            LoggingService.LogToConsole($"[{nameof(AuditLogService)}] Can't show who deleted threads: {reason}", LogSeverity.Warning);
    }

    private async Task UserLeft(SocketGuild guild, SocketUser user)
    {
        if (user.IsBot) return;

        var guildUser = guild.GetUser(user.Id);
        if (guildUser?.JoinedAt != null)
        {
            var joinDate = guildUser.JoinedAt.Value.Date;
            var timeStayed = DateTime.Now - joinDate;
            await _loggingService.LogChannelAndFile(
                $"User Left - After {(timeStayed.Days > 1 ? Math.Floor((double)timeStayed.Days) + " days" : " ")}" +
                $" {Math.Floor((double)timeStayed.Hours).ToString(CultureInfo.InvariantCulture)} hours {user.Mention} - `{guildUser.GetPreferredAndUsername()}` - ID : `{user.Id}`");
        }
        else
        {
            await _loggingService.LogChannelAndFile(
                $"User Left - `{user.GetPreferredAndUsername()}` - ID : `{user.Id}` - Left at {DateTime.Now}");
        }
    }

    private async Task GuildMemberUpdated(Cacheable<SocketGuildUser, ulong> oldUserCached, SocketGuildUser user)
    {
        var oldUser = await oldUserCached.GetOrDownloadAsync();
        if (oldUser.Nickname != user.Nickname)
        {
            await _loggingService.LogChannelAndFile(
                $"User {oldUser.GetUserPreferredName()} changed his " +
                $"username to {user.GetUserPreferredName()}");
        }
    }
}
