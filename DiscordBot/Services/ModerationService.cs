using Discord.WebSocket;
using DiscordBot.Services.Moderation;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class ModerationService
{
    private readonly ILoggingService _loggingService;
    private readonly DiscordSocketClient _client;
    private readonly CommandHandlingService _commandHandlingService;

    private const int MaxMessageLength = 800;
    private const int MaxBulkMessageLength = ForumPostTracker.MaxMessageLength;
    private const int MaxBulkMessageDetails = 4;
    private static readonly Color DeletedMessageColor = new(200, 128, 128);
    private static readonly Color EditedMessageColor = new(255, 255, 128);

    private readonly IMessageChannel? _botAnnouncementChannel;
    private readonly IMessageChannel? _memeChannel;
    private readonly bool _moderatorNoInviteLinks;
    private readonly ForumPostTracker _forumPostTracker;

    public ModerationService(DiscordSocketClient client, BotSettings settings, ILoggingService loggingService,
        CommandHandlingService commandHandlingService)
    {
        _client = client;
        _loggingService = loggingService;
        _commandHandlingService = commandHandlingService;
        _forumPostTracker = new ForumPostTracker(settings.ModeratorRoleId);

        client.MessageDeleted += MessageDeleted;
        client.MessagesBulkDeleted += MessagesBulkDeleted;
        client.MessageUpdated += MessageUpdated;
        client.MessageReceived += MessageReceived;
        client.ThreadCreated += ThreadCreated;
        client.ThreadUpdated += ThreadUpdated;
        client.ThreadDeleted += ThreadDeleted;
        client.ChannelDestroyed += ChannelDestroyed;

        if (settings.BotAnnouncementChannel != null)
            _botAnnouncementChannel = _client.GetChannel(settings.BotAnnouncementChannel.Id) as IMessageChannel;
        if (settings.MemeChannel != null)
            _memeChannel = _client.GetChannel(settings.MemeChannel.Id) as IMessageChannel;
        _moderatorNoInviteLinks = settings.ModeratorNoInviteLinks;

        foreach (var thread in _client.Guilds.SelectMany(guild => guild.ThreadChannels))
            _forumPostTracker.TrackThread(thread);
    }

    private async Task MessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        var sourceChannel = ResolveMessageChannel(channel);
        var threadSnapshot = _forumPostTracker.GetOrTrack(channel.Id, sourceChannel as SocketThreadChannel);

        if (IsIgnoredChannel(channel.Id) || sourceChannel is not null and not SocketGuildChannel)
            return;

        // A thread deletion is already represented by one bounded summary. Discord may subsequently emit
        // child-message deletion events, which must not produce one audit record per removed message.
        if (_forumPostTracker.IsRecentlyDeleted(channel.Id))
            return;

        if (!message.HasValue)
        {
            var location = threadSnapshot == null
                ? $"channel <#{channel.Id}>"
                : $"thread **{threadSnapshot.Title}** (`{channel.Id}`) in <#{threadSnapshot.ParentChannel.Id}>";
            await _loggingService.LogChannelAndFile(
                $"An uncached message `{message.Id}` was deleted from {location}; its author and content are unavailable.");
            return;
        }

        if (threadSnapshot != null)
            _forumPostTracker.ObserveMessage(threadSnapshot, message.Value);

        await LogDeletedMessage(message.Value);
    }

    private async Task MessagesBulkDeleted(
        IReadOnlyCollection<Cacheable<IMessage, ulong>> messages,
        Cacheable<IMessageChannel, ulong> channel)
    {
        var sourceChannel = ResolveMessageChannel(channel);
        var threadSnapshot = _forumPostTracker.GetOrTrack(channel.Id, sourceChannel as SocketThreadChannel);

        if (IsIgnoredChannel(channel.Id) || sourceChannel is not null and not SocketGuildChannel)
            return;

        if (_forumPostTracker.IsRecentlyDeleted(channel.Id))
            return;

        var cachedUserMessages = messages
            .Where(message => message.HasValue && !message.Value.Author.IsUserBotOrWebhook())
            .Select(message => message.Value)
            .Where(message => message.Channel is SocketGuildChannel)
            .ToList();
        foreach (var cachedMessage in cachedUserMessages)
        {
            if (threadSnapshot != null)
                _forumPostTracker.ObserveMessage(threadSnapshot, cachedMessage);
        }

        var reportedMessages = cachedUserMessages
            .OrderBy(message => message.Timestamp)
            .Take(MaxBulkMessageDetails)
            .ToList();
        var uncachedMessageCount = messages.Count(message => !message.HasValue);
        var omittedBotMessageCount = messages.Count - cachedUserMessages.Count - uncachedMessageCount;
        var omittedCachedUserMessageCount = cachedUserMessages.Count - reportedMessages.Count;

        var detailFields = reportedMessages
            .Select(message => (
                Name: $"Message by {message.Author.GetUserPreferredName()} (`{message.Id}`)"
                    .TruncateWithEllipsis(EmbedFieldBuilder.MaxFieldNameLength),
                Value: message.FormatForAuditLog(MaxBulkMessageLength).Value))
            .ToList();

        var threadKind = threadSnapshot?.ParentChannel is SocketForumChannel
            ? "Forum post"
            : threadSnapshot == null
                ? "Bulk"
                : "Thread";
        var builder = new EmbedBuilder()
            .WithColor(DeletedMessageColor)
            .WithTitle($"{threadKind} message deletion ({messages.Count} messages)")
            .WithDescription(
                $"Recovered {cachedUserMessages.Count} cached human-authored messages and showing " +
                $"{reportedMessages.Count} (maximum {MaxBulkMessageDetails}). " +
                $"{omittedCachedUserMessageCount} additional cached human messages, " +
                $"{uncachedMessageCount} uncached messages, and " +
                $"{omittedBotMessageCount} cached bot/webhook messages were omitted.")
            .WithTimestamp(DateTimeOffset.UtcNow);

        if (sourceChannel != null)
            builder.FooterInChannel(sourceChannel);
        else if (threadSnapshot != null)
            builder.FooterInChannel(threadSnapshot.ParentChannel);
        else
            builder.WithFooter($"In channel {channel.Id}");

        if (threadSnapshot != null)
        {
            builder.AddField(
                $"Users involved (observed) ({threadSnapshot.Participants.Count})",
                FormatThreadParticipants(threadSnapshot));
        }

        foreach (var field in detailFields)
            builder.AddField(field.Name, field.Value);

        await _loggingService.Log(
            LogBehaviour.Channel,
            string.Empty,
            ExtendedLogSeverity.Info,
            builder.Build());
    }

    private async Task LogDeletedMessage(IMessage message)
    {
        if (message.Author.IsUserBotOrWebhook() || message.Channel is not SocketGuildChannel)
            return;

        var deletedMessage = message.FormatForAuditLog(MaxMessageLength);

        var builder = new EmbedBuilder()
            .WithColor(DeletedMessageColor)
            .WithTimestamp(message.Timestamp)
            .FooterInChannel(message.Channel)
            .AddAuthorWithAction(message.Author, "Message deleted", true)
            .AddField(
                $"Deleted message {(deletedMessage.IsTruncated ? "(truncated)" : string.Empty)}",
                deletedMessage.Value);
        var embed = builder.Build();

        await _loggingService.Log(LogBehaviour.Channel, string.Empty, ExtendedLogSeverity.Info, embed);
    }

    private async Task MessageUpdated(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        _forumPostTracker.ObserveMessage(after);

        if (after.Author.IsUserBotOrWebhook() || IsIgnoredChannel(channel.Id) ||
            channel is not SocketGuildChannel || !before.HasValue)
            return;

        var beforeMessage = before.Value;

        // Discord also raises MessageUpdated when it adds or changes a URL embed. Those updates do not
        // change EditedTimestamp, so only continue when Discord reports a real user edit.
        if (beforeMessage.EditedTimestamp == after.EditedTimestamp)
            return;

        var contentChanged = !string.Equals(beforeMessage.Content, after.Content, StringComparison.Ordinal);
        var removedAttachments = beforeMessage.Attachments
            .Where(oldAttachment => after.Attachments.All(newAttachment => newAttachment.Id != oldAttachment.Id))
            .ToList();

        if (!contentChanged && removedAttachments.Count == 0)
            return;

        var user = after.Author;
        var builder = new EmbedBuilder()
            .WithColor(EditedMessageColor)
            .WithTimestamp(after.EditedTimestamp ?? DateTimeOffset.UtcNow)
            .FooterInChannel(after.Channel)
            .AddAuthorWithAction(user, "Updated a message", true);

        if (contentChanged)
        {
            var content = string.IsNullOrEmpty(beforeMessage.Content)
                ? "*No text content*"
                : beforeMessage.Content;
            var isTruncated = content.Length > MaxMessageLength;
            builder.AddField(
                $"Previous message content {(isTruncated ? "(truncated)" : string.Empty)}",
                content.TruncateWithEllipsis(MaxMessageLength));
        }

        if (removedAttachments.Count > 0)
        {
            var attachmentString = string.Join(
                "\n",
                removedAttachments.Select(attachment => $"[{attachment.Filename}]({attachment.Url})"));
            builder.AddField(
                $"Previous attachments ({removedAttachments.Count})",
                attachmentString.TruncateWithEllipsis(EmbedFieldBuilder.MaxFieldValueLength));
        }

        builder.WithDescription($"Message: [{after.Id}]({after.GetJumpUrl()})");
        var embed = builder.Build();

        await _loggingService.Log(LogBehaviour.Channel, string.Empty, ExtendedLogSeverity.Info, embed);
    }

    private Task ThreadCreated(SocketThreadChannel thread)
    {
        _forumPostTracker.TrackThread(thread);
        return Task.CompletedTask;
    }

    private Task ThreadUpdated(Cacheable<SocketThreadChannel, ulong> before, SocketThreadChannel after)
    {
        _forumPostTracker.TrackThread(after);
        return Task.CompletedTask;
    }

    private async Task ThreadDeleted(Cacheable<SocketThreadChannel, ulong> thread)
    {
        if (_forumPostTracker.IsRecentlyDeleted(thread.Id))
            return;

        var snapshot = _forumPostTracker.MarkDeleted(
            thread.Id,
            thread.HasValue ? thread.Value : null);
        if (snapshot == null)
        {
            await _loggingService.LogChannelAndFile(
                $"An uncached thread snowflake `{thread.Id}` was deleted; its title and message count are unavailable.");
            return;
        }

        var messageCount = snapshot.MessageCount >= 50
            ? "50+ (Discord's approximate count is capped)"
            : snapshot.MessageCount.ToString();
        var isLargeThread = snapshot.MessageCount >= ForumPostTracker.LargeThreadMessageCount ||
                            snapshot.Participants.Count >= ForumPostTracker.LargeThreadParticipantCount ||
                            snapshot.Messages.Count >= ForumPostTracker.MaxMessagesPerPost;
        snapshot.Messages.TryGetValue(snapshot.ThreadId, out var starterMessage);
        IReadOnlyCollection<ForumPostMessage> messagesToReport = isLargeThread
            ? starterMessage == null
                ? []
                : [starterMessage]
            : snapshot.Messages.Values
                .OrderBy(message => message.Timestamp)
                .ToList();

        var threadKind = snapshot.ParentChannel is SocketForumChannel ? "Forum post" : "Thread";
        var otherParticipantCount = snapshot.Participants.Values.Count(
            participant => participant.Id != snapshot.AuthorId);
        var builder = new EmbedBuilder()
            .WithColor(DeletedMessageColor)
            .WithTitle($"{threadKind} deleted")
            .WithDescription(isLargeThread
                ? $"This was a large {threadKind.ToLowerInvariant()} ({messageCount} messages, " +
                  $"{snapshot.Participants.Count} observed users). Message history was omitted" +
                  (starterMessage == null ? ". The starter was not observed while tracking." : "; showing only the starter.")
                : snapshot.Messages.Count == 0
                    ? "No eligible message bodies were observed while this thread was being tracked."
                    : "Showing the observed starter and moderator/admin messages.")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .FooterInChannel(snapshot.ParentChannel)
            .AddField($"{threadKind} title", $"{snapshot.Title} (`{thread.Id}`)")
            .AddField("Thread author", $"{snapshot.AuthorName} (`{snapshot.AuthorId}`)")
            .AddField("Messages deleted with thread (approximate)", messageCount);

        if (otherParticipantCount > 0)
        {
            builder.AddField(
                $"Other users involved (observed) ({otherParticipantCount})",
                FormatThreadParticipants(snapshot, includeAuthor: false));
        }

        foreach (var message in messagesToReport)
        {
            var messageRole = message.Id == snapshot.ThreadId ? "starter" : "moderator/admin";
            builder.AddField(
                ($"Message by {message.AuthorName} — {messageRole} (`{message.Id}`)")
                    .TruncateWithEllipsis(EmbedFieldBuilder.MaxFieldNameLength),
                message.Value);
        }

        await _loggingService.Log(
            LogBehaviour.Channel,
            string.Empty,
            ExtendedLogSeverity.Info,
            builder.Build());
    }

    private async Task ChannelDestroyed(SocketChannel channel)
    {
        if (channel is not SocketForumChannel forumChannel)
            return;

        var trackedPosts = _forumPostTracker.MarkParentChannelDeleted(forumChannel.Id);
        var observedAuthorIds = trackedPosts
            .Select(post => post.AuthorId)
            .Distinct()
            .Order()
            .Select(authorId => $"`{authorId}`");
        var authorList = string.Join(", ", observedAuthorIds)
            .TruncateWithEllipsis(EmbedFieldBuilder.MaxFieldValueLength);

        var builder = new EmbedBuilder()
            .WithColor(DeletedMessageColor)
            .WithTitle("Forum channel deleted")
            .WithDescription(
                $"Tracked {trackedPosts.Count} active posts in this forum. " +
                "Per-post message history was omitted to avoid deletion-log spam.")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .FooterInChannel(forumChannel)
            .AddField("Forum", $"{forumChannel.Name} (`{forumChannel.Id}`)");

        if (!string.IsNullOrEmpty(authorList))
            builder.AddField("Observed post author IDs", authorList);

        await _loggingService.Log(
            LogBehaviour.Channel,
            string.Empty,
            ExtendedLogSeverity.Info,
            builder.Build());
    }

    // MessageReceived
    private async Task MessageReceived(SocketMessage message)
    {
        _forumPostTracker.ObserveMessage(message);

        if (message.Author.IsBot)
            return;

        if (_moderatorNoInviteLinks == true)
        {
            if (_memeChannel?.Id == message.Channel.Id)
            {
                if (message.ContainsInviteLink())
                {
                    await message.DeleteAsync();
                    // Send a message in _botAnnouncementChannel about the deleted message, nothing fancy, name, userid, channel and message content
                    if (_botAnnouncementChannel != null)
                    {
                        await _botAnnouncementChannel.SendMessageAsync(
                            $"{message.Author.Mention} tried to post an invite link in <#{message.Channel.Id}>: {message.Content}");
                    }
                    return;
                }
            }
        }
    }

    public async Task<string> GetBotCommandHistory(int count)
    {
        return await _commandHandlingService.GetCommandHistory(count);
    }

    private IMessageChannel? ResolveMessageChannel(Cacheable<IMessageChannel, ulong> channel)
    {
        return channel.HasValue
            ? channel.Value
            : _client.GetChannel(channel.Id) as IMessageChannel;
    }

    private bool IsIgnoredChannel(ulong channelId)
    {
        return _botAnnouncementChannel?.Id == channelId;
    }

    private static string FormatThreadParticipants(
        ForumPostSnapshot snapshot,
        bool includeAuthor = true)
    {
        var participants = snapshot.Participants.Values
            .Where(participant => includeAuthor || participant.Id != snapshot.AuthorId)
            .OrderByDescending(participant => participant.Id == snapshot.AuthorId)
            .ThenByDescending(participant => participant.IsAdministrator)
            .ThenByDescending(participant => participant.IsModerator)
            .ThenBy(participant => participant.Name)
            .Select(participant =>
            {
                var role = participant.Id == snapshot.AuthorId
                    ? "thread author"
                    : participant.IsAdministrator
                        ? "admin"
                        : participant.IsModerator
                            ? "moderator"
                            : "participant";
                return $"{participant.Name} (`{participant.Id}`) — {role}";
            });

        var value = string.Join("\n", participants);
        return string.IsNullOrEmpty(value)
            ? "*No other participants observed*"
            : value.TruncateWithEllipsis(EmbedFieldBuilder.MaxFieldValueLength);
    }

}
