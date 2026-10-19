using DiscordBot.Services.Recruitment.Infrastructure.Discord;
using DiscordBot.Utils;
using System.IO;
using System.Collections.Concurrent;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Recruitment;

/// <summary>Discord I/O and authoritative pre-action checks for the serialized recruitment engine.</summary>
public sealed class DiscordRecruitmentAdapter : IRecruitmentDiscord
{
    private readonly DiscordSocketClient _client;
    private readonly BotSettings _settings;
    private readonly TimeProvider _time;
    private readonly DiscordReferencePublisher _referencePublisher;
    private readonly Dictionary<string, (ForumSnapshot Snapshot, RestForumChannel Channel)> _forums = new();
    private readonly ConcurrentDictionary<string, ForumSnapshot> _expectedForums = new();
    private readonly Dictionary<string, ThreadSnapshot> _threads = new();
    private readonly Dictionary<string, ThreadSnapshot> _observedFacts = new();
    private readonly Dictionary<string, IMessageChannel> _messageChannels = new();
    public void InvalidateThread(string threadId) { _threads.Remove(threadId); _messageChannels.Remove(threadId); }
    public void InvalidateFacts(string threadId) { _observedFacts.Remove(threadId); InvalidateThread(threadId); }
    public void InvalidateCache() { _threads.Clear(); _observedFacts.Clear(); _messageChannels.Clear(); _forums.Clear(); }
    public DiscordRecruitmentAdapter(DiscordSocketClient client, BotSettings settings, TimeProvider? time = null)
    {
        (_client, _settings, _time) = (client, settings, time ?? TimeProvider.System);
        _referencePublisher = new DiscordReferencePublisher(client, settings, CachedForumAsync, () => BotUserId, IsModeratorAsync, InvalidateFacts);
    }
    private static RequestOptions Options(CancellationToken ct) => RecruitmentRequests.Options(ct);
    private ulong BotUserId => _client.CurrentUser?.Id ?? _client.Rest.CurrentUser?.Id
        ?? throw new InvalidOperationException("Authenticated bot identity is unavailable.");
    private SocketGuild Guild => _client.GetGuild(_settings.GuildId) ?? throw new InvalidOperationException("Configured guild is unavailable.");

    public Task ValidateAccessAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_settings.Roles.Moderator == 0 || Guild.GetRole(_settings.Roles.Moderator) is null)
            throw new InvalidOperationException("The configured moderator role is unavailable.");
        foreach (var id in _settings.Recruitment.Forums.Mapping().Values)
        {
            var cached = Guild.GetChannel(id) as SocketForumChannel ?? throw new InvalidOperationException("Forum permission cache unavailable.");
            var perms = Guild.CurrentUser.GetPermissions(cached);
            if (!perms.ViewChannel || !perms.ReadMessageHistory || !perms.SendMessagesInThreads || !perms.ManageThreads || !perms.ManageChannel)
                throw new InvalidOperationException("Recruitment needs View Channel, Read Message History, Send Messages in Threads, Manage Threads and Manage Channels on each forum.");
        }
        return Task.CompletedTask;
    }

    public async Task<bool> ValidateFeedAsync(CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        var feed = await _client.Rest.GetChannelAsync(_settings.Recruitment.FeedChannelId, Options(ct)) as RestTextChannel;
        if (feed is null || feed.GuildId != _settings.GuildId) throw new InvalidOperationException("Recruitment feed must be a text channel in the configured guild.");
        var cachedFeed = Guild.GetTextChannel(feed.Id) ?? throw new InvalidOperationException("Feed permission cache unavailable.");
        var access = Guild.CurrentUser.GetPermissions(cachedFeed);
        if (!access.ViewChannel || !access.SendMessages || !access.ReadMessageHistory)
            throw new InvalidOperationException("Recruitment needs View Channel, Send Messages and Read Message History in the staff feed.");
        var everyone = feed.PermissionOverwrites.FirstOrDefault(p => p.TargetId == Guild.EveryoneRole.Id && p.TargetType == PermissionTarget.Role);
        if (everyone.Permissions.ViewChannel != PermValue.Deny)
            throw new InvalidOperationException("The staff feed must explicitly deny View Channel to @everyone.");
        foreach (var overwrite in feed.PermissionOverwrites.Where(p => p.Permissions.ViewChannel == PermValue.Allow))
        {
            if (overwrite.TargetType == PermissionTarget.Role)
            {
                var role = Guild.GetRole(overwrite.TargetId);
                if (role?.Permissions.Administrator == true || overwrite.TargetId == _settings.Roles.Moderator ||
                    (role?.IsManaged == true && Guild.CurrentUser.Roles.Any(r => r.Id == role.Id))) continue;
            }
            else if (overwrite.TargetId == BotUserId || await IsModeratorAsync(_settings.GuildId.ToString(), overwrite.TargetId.ToString(), ct)) continue;
            throw new InvalidOperationException("The recruitment feed grants access outside the configured moderator/admin roles.");
        }
        _messageChannels[feed.Id.ToString()] = feed;
        return access.EmbedLinks;
    }

    public async Task<bool> IsMemberAsync(string guildId, string userId, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        if (guildId != _settings.GuildId.ToString() || !ulong.TryParse(userId, out var id)) return false;
        return await MemberAsync(id, ct) is not null;
    }

    private async Task<RestGuildUser?> MemberAsync(ulong userId, CancellationToken ct)
    {
        try { return await _client.Rest.GetGuildUserAsync(_settings.GuildId, userId, Options(ct)); }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMember) { return null; }
    }

    public async Task<RecruitmentAuthor?> GetAuthorAsync(string userId, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        var user = await MemberAsync(ulong.Parse(userId), ct);
        return user is null ? null : new RecruitmentAuthor(user.IsBot,
            user.GuildPermissions.Administrator || user.RoleIds.Contains(_settings.Roles.Moderator));
    }

    public async Task<bool> IsAdministratorAsync(string guildId, string userId, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        if (guildId != _settings.GuildId.ToString() || !ulong.TryParse(userId, out var id)) return false;
        var user = await MemberAsync(id, ct);
        return user?.GuildPermissions.Administrator == true;
    }

    public async Task<bool> IsModeratorAsync(string guildId, string userId, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        if (guildId != _settings.GuildId.ToString() || !ulong.TryParse(userId, out var id)) return false;
        var user = await MemberAsync(id, ct);
        return user is not null && (user.GuildPermissions.Administrator || user.RoleIds.Contains(_settings.Roles.Moderator));
    }

    private async Task<RestForumChannel> ForumAsync(string id, CancellationToken ct)
    {
        var channel = await _client.Rest.GetChannelAsync(ulong.Parse(id), Options(ct));
        return channel is RestForumChannel forum && forum.GuildId == _settings.GuildId && _settings.Recruitment.Forums.Contains(forum.Id)
            ? forum : throw new InvalidOperationException("Configured recruitment forum is unavailable or has the wrong type/guild.");
    }

    public async Task<ForumSnapshot> GetForumAsync(string forumId, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        var forum = await ForumAsync(forumId, ct);
        var snapshot = Snapshot(forum);
        _forums[forumId] = (snapshot, forum);
        _expectedForums[forumId] = snapshot;
        return snapshot;
    }

    private static ForumSnapshot Snapshot(RestForumChannel forum) => new(forum.Id.ToString(), forum.Topic ?? "",
        (forum.Flags & ChannelFlags.RequireTag) != 0,
        forum.Tags.Select(t => new LiveTag(t.Id.ToString(), t.Name, t.IsModerated, t.Emoji)).ToArray(), forum.Flags);

    public async Task<ForumSnapshot> PublishForumAsync(ForumSnapshot expected, string? topic, IReadOnlyList<LiveTag>? tags, bool requireTag, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        if (!_forums.TryGetValue(expected.Id, out var observed) || !ReferenceEquals(observed.Snapshot, expected))
            throw new InvalidOperationException("Forum publication needs a current pre-write observation.");
        _expectedForums[expected.Id] = expected with { Topic = topic ?? expected.Topic, Tags = tags ?? expected.Tags,
            RequireTag = requireTag, Flags = requireTag ? expected.Flags | ChannelFlags.RequireTag : expected.Flags & ~ChannelFlags.RequireTag };
        var snapshot = await ModifyForumAsync(observed.Channel, topic, tags, requireTag, Options(ct));
        _forums[expected.Id] = (snapshot, observed.Channel);
        return snapshot;
    }

    // Read by gateway callbacks; compare the prepared publication as well as completed REST observations
    // so our own setup/rotation events cannot invalidate reply coverage or reset challenge windows.
    public bool IsExpectedForumUpdate(ForumSnapshot actual) => _expectedForums.TryGetValue(actual.Id, out var expected) &&
        expected.Topic == actual.Topic && expected.Flags == actual.Flags && expected.Tags.Count == actual.Tags.Count &&
        expected.Tags.All(t => actual.Tags.Any(a => (t.Id.Length == 0 || t.Id == a.Id) && t.Name == a.Name &&
            t.Moderated == a.Moderated && t.Emoji?.ToString() == a.Emoji?.ToString()));

    internal static async Task<ForumSnapshot> ModifyForumAsync(RestForumChannel forum, string? topic,
        IReadOnlyList<LiveTag>? tags, bool requireTag, RequestOptions options)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        using var scope = tags is null ? null : DiscordCompatibilityTransport.PreserveTags(forum.Id, tags);
        if (tags is not null) options.AuditLogReason = DiscordCompatibilityTransport.TagWriteReason;
        await forum.ModifyAsync(p =>
        {
            if (topic is not null) p.Topic = topic;
            // Discord.Net 3.20.1 sends flags:0 when Flags is omitted, even for a topic-only edit.
            p.Flags = requireTag ? forum.Flags | ChannelFlags.RequireTag : forum.Flags & ~ChannelFlags.RequireTag;
            if (tags is not null) p.Tags = tags.Select(t =>
            {
                var builder = new ForumTagBuilder(t.Name, string.IsNullOrEmpty(t.Id) ? null : ulong.Parse(t.Id), t.Moderated);
                if (t.Emoji is not null) builder.WithEmoji(t.Emoji);
                return builder.Build();
            }).ToArray();
        }, options);
        return Snapshot(forum);
    }

    public Task<ThreadInventory> InventoryAsync(DateTimeOffset floor, IReadOnlySet<string> knownIds,
        IReadOnlyDictionary<string, ArchiveCursor> cursors, CancellationToken ct) =>
        new DiscordInventoryReader(_settings, _time, CachedForumAsync, SnapshotAsync,
            (id, token) => GetThreadAsync(id, token)).ReadAsync(floor, knownIds, cursors, ct);

    private Task<RestForumChannel> CachedForumAsync(string id, CancellationToken ct) =>
        _forums.TryGetValue(id, out var observed) ? Task.FromResult(observed.Channel) : ForumAsync(id, ct);

    public async Task<ThreadSnapshot?> GetThreadAsync(string threadId, CancellationToken ct, bool refresh = false)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        if (!refresh && _threads.TryGetValue(threadId, out var cached)) return cached;
        try
        {
            var channel = await _client.Rest.GetChannelAsync(ulong.Parse(threadId), Options(ct));
            if (channel is not RestThreadChannel thread || thread.GuildId != _settings.GuildId || !_settings.Recruitment.Forums.Contains(thread.ParentChannelId))
                throw new InvalidOperationException("Thread is outside configured recruitment forums.");
            return await SnapshotAsync(thread, ct);
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownChannel) { _threads.Remove(threadId); return null; }
    }

    private async Task<ThreadSnapshot> SnapshotAsync(RestThreadChannel thread, CancellationToken ct)
    {
        var ownerId = ((IThreadChannel)thread).OwnerId;
        var id = thread.Id.ToString();
        if (!_observedFacts.TryGetValue(id, out var facts))
        {
            var user = await MemberAsync(ownerId, ct);
            var starter = await thread.GetMessageAsync(thread.Id, Options(ct));
            facts = new ThreadSnapshot(id, thread.ParentChannelId.ToString(), ownerId.ToString(), thread.Name,
                thread.CreatedAt.ToUniversalTime(), user?.IsBot ?? starter?.Author.IsBot ?? false, false, false, false,
                Array.Empty<string>(), starter?.Content, SnowflakeUtils.FromSnowflake(ownerId), user?.JoinedAt,
                user is not null && (user.GuildPermissions.Administrator || user.RoleIds.Contains(_settings.Roles.Moderator)),
                user?.RoleIds.Count(r => r != _settings.GuildId), user is null ? null : !string.IsNullOrEmpty(user.DisplayAvatarId),
                starter?.Attachments.Take(10).Select(a => new StarterAttachment(a.Id.ToString(), DiscordDisplayText.Literal(a.Filename, 120), a.ContentType?[..Math.Min(a.ContentType.Length, 80)])).ToArray(),
                RecruitmentSignals.SafeIdentity(user?.Nickname), RecruitmentSignals.SafeIdentity(user?.GlobalName),
                RecruitmentSignals.SafeIdentity(user?.Username ?? starter?.Author.Username),
                RecruitmentSignals.SafeAvatarUrl(user?.GetDisplayAvatarUrl(ImageFormat.Png, 128)),
                RecruitmentSignals.SafeIdentity(user?.DisplayAvatarId, 200));
            // Missing starters remain retryable. Bound cached initial facts independently of retained history.
            if (starter is not null)
            {
                if (_observedFacts.Count >= 4096) _observedFacts.Remove(_observedFacts.Keys.First());
                _observedFacts[id] = facts;
            }
        }
        var snapshot = facts with { ForumId = thread.ParentChannelId.ToString(), OwnerId = ownerId.ToString(), Title = thread.Name,
            Pinned = (thread.Flags & ChannelFlags.Pinned) != 0, Archived = thread.IsArchived, Locked = thread.IsLocked,
            Tags = thread.AppliedTags.Select(t => t.ToString()).ToArray() };
        _threads[id] = snapshot;
        _messageChannels[id] = thread;
        return snapshot;
    }

    private async Task<IMessageChannel> MessageChannelAsync(string id, CancellationToken ct)
    {
        if (_messageChannels.TryGetValue(id, out var cached)) return cached;
        var channel = await _client.Rest.GetChannelAsync(ulong.Parse(id), Options(ct));
        if (channel is not IGuildChannel guildChannel || guildChannel.GuildId != _settings.GuildId || channel is not IMessageChannel messages)
            throw new InvalidOperationException("Recruitment message channel unavailable.");
        _messageChannels[id] = messages;
        return messages;
    }

    public async Task<Delivery> UpsertAsync(string channelId, string? messageId, bool recovering, DateTimeOffset since, RecruitmentMessage message, Action beforeWrite, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        var channel = await MessageChannelAsync(channelId, ct);
        var botUserId = BotUserId;
        if (message.Format == RecruitmentMessageFormat.ComponentsV2)
        {
            var permissions = channel is RestThreadChannel listingThread
                ? _client.GetGuild(_settings.GuildId)?.CurrentUser?.GetPermissions(_client.GetGuild(_settings.GuildId)?.GetChannel(listingThread.ParentChannelId))
                : null;
            return await V2MessageWriter.UpsertAsync(channel, botUserId, messageId, recovering, since, message,
                channel is not IThreadChannel || permissions is { AttachFiles: true, EmbedLinks: true }, beforeWrite, ct);
        }
        if (message.Embed is not null && message.TextFallback is not null && channel is RestThreadChannel listing &&
            !Guild.CurrentUser.GetPermissions(Guild.GetChannel(listing.ParentChannelId)).EmbedLinks)
            message = message with { Content = message.TextFallback, Embed = null };
        return await StandardMessageWriter.UpsertAsync(channel, botUserId, messageId, recovering, since, message, beforeWrite, ct);
    }

    public async Task CloseAsync(ThreadSnapshot thread, string? closedTagId, bool protectPinned, CancellationToken ct, Func<bool>? allowWrite = null)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        var live = await ThreadAsync(thread.Id, ct);
        GuardWrite(live, thread, protectPinned);
        if (allowWrite?.Invoke() == false) throw new InvalidOperationException("New recruitment evidence requires reconciliation before this action.");
        var tags = live.AppliedTags.ToList();
        if (closedTagId is not null && tags.Count < 5 && !tags.Contains(ulong.Parse(closedTagId))) tags.Add(ulong.Parse(closedTagId));
        await live.ModifyAsync(p => { p.AppliedTags = tags; p.Locked = true; p.Archived = true; }, Options(ct));
        InvalidateThread(thread.Id);
        var verified = await ThreadAsync(thread.Id, ct);
        if (!verified.IsLocked || !verified.IsArchived) throw new InvalidOperationException("Thread closure was not confirmed.");
    }
    public async Task ReopenAsync(ThreadSnapshot thread, string? closedTagId, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        var live = await ThreadAsync(thread.Id, ct);
        GuardWrite(live, thread, false);
        await live.ModifyAsync(p => { p.AppliedTags = live.AppliedTags.Where(t => t.ToString() != closedTagId).ToArray(); p.Locked = false; p.Archived = false; }, Options(ct));
        InvalidateThread(thread.Id);
        var verified = await ThreadAsync(thread.Id, ct);
        if (verified.IsLocked || verified.IsArchived) throw new InvalidOperationException("Thread reopening was not confirmed.");
    }
    private async Task<RestThreadChannel> ThreadAsync(string id, CancellationToken ct) =>
        await _client.Rest.GetChannelAsync(ulong.Parse(id), Options(ct)) as RestThreadChannel ?? throw new InvalidOperationException("Thread unavailable.");
    public async Task DeleteAsync(ThreadSnapshot thread, string reason, bool protectPinned, CancellationToken ct, Func<bool>? allowWrite = null)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        var live = await ThreadAsync(thread.Id, ct);
        GuardWrite(live, thread, protectPinned);
        if (allowWrite?.Invoke() == false) throw new InvalidOperationException("New recruitment evidence requires reconciliation before this action.");
        var options = Options(ct); options.AuditLogReason = "Recruitment: " + DiscordDisplayText.Literal(reason);
        await live.DeleteAsync(options);
        InvalidateThread(thread.Id);
    }
    private void GuardWrite(RestThreadChannel live, ThreadSnapshot expected, bool protectPinned)
    {
        if (live.GuildId != _settings.GuildId || live.ParentChannelId.ToString() != expected.ForumId ||
            ((IThreadChannel)live).OwnerId.ToString() != expected.OwnerId || !_settings.Recruitment.Forums.Contains(live.ParentChannelId) ||
            protectPinned && (live.Flags & ChannelFlags.Pinned) != 0)
            throw new InvalidOperationException("Thread identity or protection changed before the action.");
    }
    public async Task DeleteAdvisoryAsync(string threadId, string messageId, CancellationToken ct, string? marker = null)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        var channel = await MessageChannelAsync(threadId, ct);
        if (await channel.GetMessageAsync(ulong.Parse(messageId), CacheMode.AllowDownload, Options(ct)) is IUserMessage message && marker is not null && OwnedMessageLookup.OwnsMessage(message, BotUserId, channel.Id, marker, marker.EndsWith(":public", StringComparison.Ordinal) ? RecruitmentMessageFormat.ComponentsV2 : RecruitmentMessageFormat.Standard))
            await message.DeleteAsync(Options(ct));
    }
    public async Task<bool> PinAdvisoryAsync(string threadId, string messageId, bool restoreArchive, bool restoreLocked,
        Action<bool, bool> beforeWrite, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        // A fresh channel is essential after a lost unarchive/pin response.
        var channel = await _client.Rest.GetChannelAsync(ulong.Parse(threadId), Options(ct)) as RestThreadChannel;
        if (channel is null || channel.GuildId != _settings.GuildId ||
            !_settings.Recruitment.Forums.Mapping().Values.Contains(channel.ParentChannelId))
            throw new InvalidOperationException("Response pin thread unavailable.");
        var guild = _client.GetGuild(_settings.GuildId);
        var parent = guild?.GetChannel(channel.ParentChannelId);
        bool? allowed = parent is null ? null : guild!.CurrentUser.GetPermissions(parent).PinMessages;
        return await DiscordResponsePins.PinOwnedAsync(channel, BotUserId, ulong.Parse(messageId), allowed, restoreArchive, restoreLocked, beforeWrite, ct);
    }

    public Task<string> CreateReferenceAsync(string forumId, string closedTagId, string marker, CancellationToken ct) =>
        _referencePublisher.CreateReferenceAsync(forumId, closedTagId, marker, ct);
    public Task<bool> SecureReferenceAsync(string forumId, string threadId, bool botOwned, string marker, CancellationToken ct) =>
        _referencePublisher.SecureReferenceAsync(forumId, threadId, botOwned, marker, ct);
}
