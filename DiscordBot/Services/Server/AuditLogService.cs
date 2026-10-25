using System.Globalization;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Server;

public class AuditLogService
{
    private readonly ILoggingService _loggingService;
    private readonly DiscordSocketClient _client;

    private const int MaxMessageLength = 800;
    private static readonly Color EditedMessageColor = new(255, 255, 128);

    private readonly IMessageChannel? _botAnnouncementChannel = null!;

    public AuditLogService(DiscordSocketClient client, BotSettings settings, ILoggingService loggingService)
    {
        _loggingService = loggingService;
        _client = client;

        client.MessageDeleted += EventGuard.Guarded<Cacheable<IMessage, ulong>, Cacheable<IMessageChannel, ulong>>(MessageDeleted, nameof(MessageDeleted));
        client.MessagesBulkDeleted += EventGuard.Guarded<IReadOnlyCollection<Cacheable<IMessage, ulong>>, Cacheable<IMessageChannel, ulong>>(MessagesBulkDeleted, nameof(MessagesBulkDeleted));
        client.MessageUpdated += EventGuard.Guarded<Cacheable<IMessage, ulong>, SocketMessage, ISocketMessageChannel>(MessageUpdated, nameof(MessageUpdated));
        client.UserLeft += EventGuard.Guarded<SocketGuild, SocketUser>(UserLeft, nameof(UserLeft));
        client.GuildMemberUpdated += EventGuard.Guarded<Cacheable<SocketGuildUser, ulong>, SocketGuildUser>(GuildMemberUpdated, nameof(GuildMemberUpdated));

        if (settings.Channels.BotAnnouncement != null)
            _botAnnouncementChannel = client.GetChannel(settings.Channels.BotAnnouncement.Id) as IMessageChannel;
    }

    private async Task MessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        if (channel.Id == _botAnnouncementChannel?.Id)
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
        var inServer = guild is SocketGuild socketGuild ? socketGuild.GetUser(author.Id) != null : true;
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
        if (after.Author.IsBot || channel.Id == _botAnnouncementChannel?.Id)
            return;

        bool isCached = true;
        string content = "";
        var beforeMessage = await before.GetOrDownloadAsync();
        if (beforeMessage == null || beforeMessage.Content == after.Content)
            isCached = false;
        else
            content = beforeMessage.Content;

        if (content == after.Content)
            return;
        if (content.Length == 0 && (beforeMessage?.Attachments.Count ?? 0) == 0)
            return;

        bool isTruncated = false;
        if (content.Length > MaxMessageLength)
        {
            content = content[..MaxMessageLength];
            isTruncated = true;
        }

        var user = after.Author;
        var builder = new EmbedBuilder()
            .WithColor(EditedMessageColor)
            .WithTimestamp(after.Timestamp)
            .FooterInChannel(after.Channel)
            .AddAuthorWithAction(user, "Updated a message", true);
        if (isCached)
        {
            builder.AddField($"Previous message content {(isTruncated ? "(truncated)" : "")}", content);
            if (beforeMessage?.Attachments.Count > 0)
            {
                var attachments = beforeMessage.Attachments.Where(x => after.Attachments.All(y => y.Url != x.Url));
                var removedAttachments = attachments.ToList();
                if (removedAttachments.Any())
                {
                    var attachmentString = string.Join("\n", removedAttachments.Select(x => $"[{x.Filename}]({x.Url})"));
                    builder.AddField($"Previous attachments ({removedAttachments.Count()})", attachmentString);
                }
            }
        }

        builder.WithDescription($"Message: [{after.Id}]({after.GetJumpUrl()})");
        var embed = builder.Build();

        await _loggingService.Log(LogBehaviour.Channel, string.Empty, ExtendedLogSeverity.Info, embed);
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
