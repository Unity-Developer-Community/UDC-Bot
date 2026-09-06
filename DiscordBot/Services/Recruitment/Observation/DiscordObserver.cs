using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Services.Recruitment.Policy;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment.Observation;

public sealed class DiscordObserver(
    DiscordSocketClient client, DatabaseService database, ForumClassifier classifier,
    IOptions<RecruitmentOptions> options, IOptions<DiscordGuildOptions> guildOptions,
    IOptions<AuthorizationOptions> roleOptions) : IForumObserver
{
    private ulong GuildId => guildOptions.Value.GuildId;
    private ulong FeedId => options.Value.FeedChannelId;
    private static RequestOptions Request(CancellationToken token) => new() { CancelToken = token, Timeout = 15000 };

    public IDisposable Subscribe(Action<ObservationEvent> receive)
    {
        Task ThreadCreated(SocketThreadChannel thread)
        {
            if (thread.Guild.Id == GuildId && classifier.Classify(thread.Id, thread.ParentChannel.Id) is not null)
                receive(new(EventKind.Changed, thread.Id, thread.ParentChannel.Id));
            return Task.CompletedTask;
        }
        Task ThreadUpdated(Cacheable<SocketThreadChannel, ulong> _, SocketThreadChannel thread) => ThreadCreated(thread);
        Task ThreadDeleted(Cacheable<SocketThreadChannel, ulong> thread)
        {
            // Uncached IDs are filtered against persisted recruitment state by the coordinator.
            if (!thread.HasValue || thread.Value.Guild.Id == GuildId && classifier.Classify(thread.Id, thread.Value.ParentChannel.Id) is not null)
                receive(new(EventKind.Deleted, thread.Id, thread.HasValue ? thread.Value.ParentChannel.Id : 0));
            return Task.CompletedTask;
        }
        Task Message(SocketMessage message)
        {
            if (message.Channel is SocketThreadChannel thread && thread.Guild.Id == GuildId &&
                classifier.Classify(thread.Id, thread.ParentChannel.Id) is not null)
                receive(new(EventKind.Message, thread.Id, thread.ParentChannel.Id, Snapshot(message, live: true)));
            return Task.CompletedTask;
        }
        Task MessageUpdated(Cacheable<IMessage, ulong> _, SocketMessage message, ISocketMessageChannel channel)
        {
            if (channel is SocketThreadChannel thread) return ThreadCreated(thread);
            return Task.CompletedTask;
        }
        Task MessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
        {
            if (message.Id == channel.Id) receive(new(EventKind.Changed, channel.Id));
            return Task.CompletedTask; // Never discard previously observed qualifying reply evidence.
        }
        Task ChannelUpdated(SocketChannel _, SocketChannel channel)
        {
            if (classifier.Classify(channel.Id) is not null || channel.Id == FeedId) receive(new(EventKind.Gap));
            return Task.CompletedTask;
        }
        Task ChannelDestroyed(SocketChannel channel) => ChannelUpdated(channel, channel);
        Task BulkDeleted(IReadOnlyCollection<Cacheable<IMessage, ulong>> messages, Cacheable<IMessageChannel, ulong> channel)
        {
            if (messages.Any(m => m.Id == channel.Id)) receive(new(EventKind.Changed, channel.Id));
            return Task.CompletedTask;
        }
        Task Ready() { receive(new(EventKind.Gap)); return Task.CompletedTask; }
        Task Disconnected(Exception _) => Ready();
        client.ThreadCreated += ThreadCreated;
        client.ThreadUpdated += ThreadUpdated;
        client.ThreadDeleted += ThreadDeleted;
        client.MessageReceived += Message;
        client.MessageUpdated += MessageUpdated;
        client.MessageDeleted += MessageDeleted;
        client.MessagesBulkDeleted += BulkDeleted;
        client.ChannelUpdated += ChannelUpdated;
        client.ChannelDestroyed += ChannelDestroyed;
        client.Ready += Ready;
        client.Disconnected += Disconnected;
        return new Subscription(() =>
        {
            client.ThreadCreated -= ThreadCreated;
            client.ThreadUpdated -= ThreadUpdated;
            client.ThreadDeleted -= ThreadDeleted;
            client.MessageReceived -= Message;
            client.MessageUpdated -= MessageUpdated;
            client.MessageDeleted -= MessageDeleted;
            client.MessagesBulkDeleted -= BulkDeleted;
            client.ChannelUpdated -= ChannelUpdated;
            client.ChannelDestroyed -= ChannelDestroyed;
            client.Ready -= Ready;
            client.Disconnected -= Disconnected;
        });
    }

    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        var guild = client.GetGuild(GuildId) ?? throw new InvalidOperationException("Recruitment guild is unavailable.");
        foreach (var configured in ForumClassifier.GetForums(options.Value.Forums))
        {
            var forum = await ForumAsync(configured.ChannelId, cancellationToken);
            var permissions = guild.CurrentUser.GetPermissions(forum);
            if (!permissions.ViewChannel || !permissions.ReadMessageHistory)
                throw new InvalidOperationException("Observe requires View Channel and Read Message History in every recruitment forum.");
        }
        var feed = await FeedAsync(cancellationToken);
        var feedPermissions = guild.CurrentUser.GetPermissions(feed);
        if (!feedPermissions.ViewChannel || !feedPermissions.ReadMessageHistory || !feedPermissions.SendMessages)
            throw new InvalidOperationException("Observe requires View Channel, Read Message History and Send Messages in the staff feed.");
    }

    private async Task<IForumChannel> ForumAsync(ulong id, CancellationToken token)
    {
        if (classifier.Classify(id) is null) throw new InvalidOperationException("Unconfigured recruitment forum.");
        return await ((IDiscordClient)client.Rest).GetChannelAsync(id, CacheMode.AllowDownload, Request(token)) is IForumChannel forum && forum.GuildId == GuildId
            ? forum : throw new InvalidOperationException("Recruitment forum is missing, has the wrong type, or belongs to another guild.");
    }

    private async Task<ITextChannel> FeedAsync(CancellationToken token) =>
        await ((IDiscordClient)client.Rest).GetChannelAsync(FeedId, CacheMode.AllowDownload, Request(token)) is ITextChannel feed &&
        feed is not IThreadChannel && feed.GuildId == GuildId && classifier.Classify(feed.Id) is null
            ? feed : throw new InvalidOperationException("Recruitment staff feed must be a text channel in the configured guild.");

    public async Task<IReadOnlyList<ThreadSnapshot>> GetActiveAsync(ulong forumId, CancellationToken cancellationToken)
    {
        var forum = await ForumAsync(forumId, cancellationToken);
        return (await forum.GetActiveThreadsAsync(Request(cancellationToken)))
            .Where(t => t is RestThreadChannel rest && rest.ParentChannelId == forumId).Select(t => Snapshot(t, forum)).ToArray();
    }

    public async Task<ArchivePage> GetArchivedAsync(ulong forumId, DateTimeOffset? before, CancellationToken cancellationToken)
    {
        var forum = await ForumAsync(forumId, cancellationToken);
        var threads = (await forum.GetPublicArchivedThreadsAsync(100, before, Request(cancellationToken))).Select(t => Snapshot(t, forum)).ToArray();
        // Discord returns has_more, but this SDK surface discards it. A short page is not
        // sufficient evidence of completion: keep paging until an empty response.
        return new(threads, threads.Length == 0 ? null : threads.Min(t => t.ArchiveTimestampUtc), threads.Length == 0);
    }

    public async Task<ThreadSnapshot?> GetThreadAsync(ulong threadId, CancellationToken cancellationToken)
    {
        var thread = await ThreadAsync(threadId, cancellationToken);
        if (thread is null) return null;
        var forum = await ForumAsync(thread.ParentChannelId, cancellationToken);
        return Snapshot(thread, forum);
    }

    private async Task<RestThreadChannel?> ThreadAsync(ulong id, CancellationToken token)
    {
        try
        {
            var channel = await ((IDiscordClient)client.Rest).GetChannelAsync(id, CacheMode.AllowDownload, Request(token));
            if (channel is null) return null;
            if (channel is not RestThreadChannel thread || thread.GuildId != GuildId || classifier.Classify(id, thread.ParentChannelId) is null)
                throw new InvalidOperationException("Thread does not belong to a configured recruitment forum.");
            return thread;
        }
        catch (HttpException e) when (e.DiscordCode == DiscordErrorCode.UnknownChannel) { return null; }
    }

    public async Task<MessageSnapshot?> GetStarterAsync(ulong threadId, CancellationToken cancellationToken)
    {
        var channel = await ThreadAsync(threadId, cancellationToken) ?? throw new InvalidOperationException("Thread disappeared while reading its starter.");
        try
        {
            var message = await ((IMessageChannel)channel).GetMessageAsync(threadId, CacheMode.AllowDownload, Request(cancellationToken));
            return message is null ? null : Snapshot(message, live: false);
        }
        catch (HttpException e) when (e.DiscordCode == DiscordErrorCode.UnknownMessage) { return null; }
    }

    public async Task<MessagePage> GetRepliesAsync(ulong threadId, ulong afterId, CancellationToken cancellationToken)
    {
        var channel = await ThreadAsync(threadId, cancellationToken) ?? throw new InvalidOperationException("Thread disappeared while reading replies.");
        var messages = (await ((IMessageChannel)channel).GetMessagesAsync(afterId, Direction.After, 100, CacheMode.AllowDownload, Request(cancellationToken)).FlattenAsync()).ToArray();
        return new(messages.OrderBy(m => m.Id).Select(m => Snapshot(m, live: false)).ToArray(), messages.Length < 100);
    }

    public async Task<AuthorFacts> GetAuthorAsync(ulong authorId, CancellationToken cancellationToken)
    {
        var activity = ActivityStatus.Unknown;
        if (database.IsRunning)
        {
            try
            {
                // The existing repository uses its configured command timeout. Await it so Stop drains the read.
                var user = await database.Query.GetUser(authorId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                activity = PolicyEvaluator.Activity(user?.Exp, user?.Karma, user?.KarmaGiven);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(activity, client.GetGuild(GuildId)?.GetUser(authorId)?.JoinedAt?.ToUniversalTime());
    }

    public async Task<FeedPage> FindFeedAsync(string marker, DateTimeOffset since, ulong? beforeId, CancellationToken cancellationToken)
    {
        var feed = await FeedAsync(cancellationToken);
        var messages = (await (beforeId is { } before
            ? feed.GetMessagesAsync(before, Direction.Before, 100, CacheMode.AllowDownload, Request(cancellationToken))
            : feed.GetMessagesAsync(100, CacheMode.AllowDownload, Request(cancellationToken))).FlattenAsync()).ToArray();
        var found = messages.FirstOrDefault(m => Owned(m, marker));
        return new(found?.Id, messages.Length == 0 ? null : messages.Min(m => m.Id),
            messages.Length < 100 || messages.Min(m => m.Timestamp) < since.AddMinutes(-1));
    }

    public async Task<ulong> SendFeedAsync(string content, CancellationToken cancellationToken)
    {
        var feed = await FeedAsync(cancellationToken);
        return (await feed.SendMessageAsync(content, allowedMentions: AllowedMentions.None, options: Request(cancellationToken))).Id;
    }

    public async Task<bool> EditFeedAsync(ulong messageId, string marker, string content, CancellationToken cancellationToken)
    {
        var feed = await FeedAsync(cancellationToken);
        IMessage? message;
        try { message = await feed.GetMessageAsync(messageId, CacheMode.AllowDownload, Request(cancellationToken)); }
        catch (HttpException e) when (e.DiscordCode == DiscordErrorCode.UnknownMessage) { return false; }
        if (message is null) return false;
        if (message is not IUserMessage owned || !Owned(owned, marker))
            throw new InvalidOperationException("Saved staff-feed message is not owned by this recruitment observation.");
        if (!string.Equals(message.Content, content, StringComparison.Ordinal))
            await owned.ModifyAsync(p => { p.Content = content; p.AllowedMentions = AllowedMentions.None; }, Request(cancellationToken));
        return true;
    }

    private bool Owned(IMessage message, string marker) => message.Author.Id == client.CurrentUser.Id &&
        message.Content.EndsWith($"\n`{marker}`", StringComparison.Ordinal);

    private static ThreadSnapshot Snapshot(IThreadChannel thread, IForumChannel forum) => new(
        thread.Id, forum.Id, thread.OwnerId, thread.CreatedAt.ToUniversalTime(), thread.Name, thread.AppliedTags.ToArray(),
        thread.IsArchived, thread.IsLocked, thread.Flags.HasFlag(ChannelFlags.Pinned),
        forum.Tags.Any(tag => string.Equals(tag.Name.Trim(), "Closed", StringComparison.OrdinalIgnoreCase) && thread.AppliedTags.Contains(tag.Id)),
        thread.ArchiveTimestamp.ToUniversalTime());

    private MessageSnapshot Snapshot(IMessage message, bool live)
    {
        var user = live ? message.Author as IGuildUser : null;
        // REST history cannot establish what roles someone held at message time.
        return new(message.Id, new(message.Author.Id, message.Timestamp.ToUniversalTime(), message.Author.IsBot,
            message.Author.IsWebhook, user is null ? null : user.RoleIds.Contains(roleOptions.Value.ModeratorRoleId),
            user?.GuildPermissions.Administrator, message.Type is MessageType.Default or MessageType.Reply),
            message.Content is { Length: > 0 } content ? content[..Math.Min(content.Length, 8000)] : null);
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;
        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
