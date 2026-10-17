using System.IO;
using System.Collections.Concurrent;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Recruitment;

/// <summary>Discord I/O and authoritative pre-action checks for the serialized recruitment engine.</summary>
public sealed partial class DiscordRecruitmentAdapter : IRecruitmentDiscord
{
    private readonly DiscordSocketClient _client;
    private readonly BotSettings _settings;
    private readonly Dictionary<string, (ForumSnapshot Snapshot, RestForumChannel Channel)> _forums = new();
    private readonly ConcurrentDictionary<string, ForumSnapshot> _expectedForums = new();
    private readonly Dictionary<string, ThreadSnapshot> _threads = new();
    private readonly Dictionary<string, ThreadSnapshot> _initialFacts = new();
    private readonly Dictionary<string, IMessageChannel> _messageChannels = new();
    public void InvalidateThread(string threadId) { _threads.Remove(threadId); _messageChannels.Remove(threadId); }
    public void InvalidateFacts(string threadId) { _initialFacts.Remove(threadId); InvalidateThread(threadId); }
    public void InvalidateCache() { _threads.Clear(); _initialFacts.Clear(); _messageChannels.Clear(); _forums.Clear(); }
    public DiscordRecruitmentAdapter(DiscordSocketClient client, BotSettings settings)
        => (_client, _settings) = (client, settings);
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
        var user = await MemberAsync(ulong.Parse(userId), ct);
        return user is null ? null : new RecruitmentAuthor(user.IsBot,
            user.GuildPermissions.Administrator || user.RoleIds.Contains(_settings.Roles.Moderator));
    }

    public async Task<bool> IsModeratorAsync(string guildId, string userId, CancellationToken ct)
    {
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
        using var scope = tags is null ? null : RecruitmentForumTransport.PreserveTags(forum.Id, tags);
        if (tags is not null) options.AuditLogReason = RecruitmentForumTransport.TagWriteReason;
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

    public async Task<ThreadInventory> InventoryAsync(DateTimeOffset floor, IReadOnlySet<string> knownIds,
        IReadOnlyDictionary<string, ArchiveCursor> cursors, CancellationToken ct)
    {
        var found = new Dictionary<ulong, RestThreadChannel?>();
        var progress = new Dictionary<string, ArchiveCursor>();
        var complete = true;
        var firstId = _settings.Recruitment.Forums.Mapping().Values.First().ToString();
        var firstForum = _forums.TryGetValue(firstId, out var first) ? first.Channel : await ForumAsync(firstId, ct);
        using (var active = RecruitmentForumTransport.ObserveActiveInventory(_settings.GuildId))
        {
            foreach (var thread in await firstForum.GetActiveThreadsAsync(Options(ct))) found[thread.Id] = thread;
            if (active.Identities is null) throw new InvalidOperationException("Active-thread inventory response was not observed; acceptance is held.");
            foreach (var identity in active.Identities.Where(t => _settings.Recruitment.Forums.Contains(t.Parent)))
                found.TryAdd(identity.Id, null);
        }
        foreach (var id in _settings.Recruitment.Forums.Mapping().Values)
        {
            var forum = _forums.TryGetValue(id.ToString(), out var observed) ? observed.Channel : await ForumAsync(id.ToString(), ct);
            var cursor = cursors[id.ToString()];
            var started = cursor.StartedAt ?? DateTimeOffset.UtcNow;
            var since = cursor.Since > floor ? cursor.Since : floor;
            var before = cursor.Before;
            var finished = false;
            // Resume bounded archive pages across sweeps. A completed pass advances its lower boundary.
            for (var page = 0; page < 2; page++)
            {
                var batch = await forum.GetPublicArchivedThreadsAsync(100, before, Options(ct));
                foreach (var thread in batch.Where(t => t.CreatedAt >= floor)) found[thread.Id] = thread;
                if (batch.Count < 100 || batch.Min(t => t.ArchiveTimestamp) < since) { finished = true; break; }
                before = batch.Min(t => t.ArchiveTimestamp);
            }
            complete &= finished;
            // An overlap protects against timestamp precision and threads archiving during the scan.
            progress[id.ToString()] = finished ? new ArchiveCursor(started.AddMinutes(-5)) : new ArchiveCursor(since, before, started);
        }
        var snapshots = new List<ThreadSnapshot>();
        var unseen = found.Where(t => SnowflakeUtils.FromSnowflake(t.Key) >= floor && !knownIds.Contains(t.Key.ToString())).ToArray();
        foreach (var thread in unseen.Take(50))
        {
            var snapshot = thread.Value is not null ? await SnapshotAsync(thread.Value, ct) : await GetThreadAsync(thread.Key.ToString(), ct);
            if (snapshot is not null) snapshots.Add(snapshot);
        }
        // Do not advance past an archive page until all its unknown identities have been retained.
        return unseen.Length > 50 ? new ThreadInventory(snapshots, false, cursors) : new ThreadInventory(snapshots, complete, progress);
    }

    public async Task<ThreadSnapshot?> GetThreadAsync(string threadId, CancellationToken ct, bool refresh = false)
    {
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
        if (!_initialFacts.TryGetValue(id, out var facts))
        {
            var user = await MemberAsync(ownerId, ct);
            var starter = await thread.GetMessageAsync(thread.Id, Options(ct));
            facts = new ThreadSnapshot(id, thread.ParentChannelId.ToString(), ownerId.ToString(), thread.Name,
                thread.CreatedAt.ToUniversalTime(), user?.IsBot ?? starter?.Author.IsBot ?? false, false, false, false,
                Array.Empty<string>(), starter?.Content, SnowflakeUtils.FromSnowflake(ownerId), user?.JoinedAt,
                user is not null && (user.GuildPermissions.Administrator || user.RoleIds.Contains(_settings.Roles.Moderator)),
                user?.RoleIds.Count(r => r != _settings.GuildId), user is null ? null : !string.IsNullOrEmpty(user.DisplayAvatarId),
                starter?.Attachments.Take(10).Select(a => new StarterAttachment(a.Id.ToString(), RecruitmentPresentation.Safe(a.Filename, 120), a.ContentType?[..Math.Min(a.ContentType.Length, 80)])).ToArray(),
                RecruitmentSignals.SafeIdentity(user?.Nickname), RecruitmentSignals.SafeIdentity(user?.GlobalName),
                RecruitmentSignals.SafeIdentity(user?.Username ?? starter?.Author.Username),
                RecruitmentSignals.SafeAvatarUrl(user?.GetDisplayAvatarUrl(ImageFormat.Png, 128)),
                RecruitmentSignals.SafeIdentity(user?.DisplayAvatarId, 200));
            // Missing starters remain retryable. Bound cached initial facts independently of retained history.
            if (starter is not null)
            {
                if (_initialFacts.Count >= 4096) _initialFacts.Remove(_initialFacts.Keys.First());
                _initialFacts[id] = facts;
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
        var channel = await MessageChannelAsync(channelId, ct);
        var botUserId = BotUserId;
        if (message.Embed is not null && message.TextFallback is not null && channel is RestThreadChannel listing &&
            !Guild.CurrentUser.GetPermissions(Guild.GetChannel(listing.ParentChannelId)).EmbedLinks)
            message = message with { Content = message.TextFallback, Embed = null };
        IUserMessage? existing = null;
        if (messageId is not null) existing = await channel.GetMessageAsync(ulong.Parse(messageId), CacheMode.AllowDownload, Options(ct)) as IUserMessage;
        if (existing is not null && (existing.Author.Id != botUserId || !existing.Content.Contains(message.Marker)))
            throw new InvalidOperationException("Stored recruitment message identity does not match.");
        if (existing is null && recovering)
        {
            ulong? before = null;
            var covered = false;
            for (var page = 0; page < 20; page++)
            {
                var messages = (before.HasValue ? await channel.GetMessagesAsync(before.Value, Direction.Before, 100, CacheMode.AllowDownload, Options(ct)).FlattenAsync()
                    : await channel.GetMessagesAsync(100, CacheMode.AllowDownload, Options(ct)).FlattenAsync()).ToArray();
                existing = messages.OfType<IUserMessage>().FirstOrDefault(m => m.Author.Id == botUserId && m.Content.Contains(message.Marker));
                if (existing is not null) break;
                if (messages.Length < 100 || messages.Min(m => m.Timestamp) <= since.AddMinutes(-5)) { covered = true; break; }
                before = messages.Min(m => m.Id);
            }
            if (existing is null && !covered) throw new InvalidOperationException("Message recovery history exceeds the scan bound; staff review required before resending.");
        }
        var builder = new ComponentBuilder();
        if (message.AcknowledgementRequired) builder.WithButton("Acknowledge", $"recruit:ack:{channelId}", ButtonStyle.Primary);
        if (message.ReaderControls) builder.WithButton("Details", $"recruit:details:{channelId}", ButtonStyle.Secondary)
            .WithButton("Report concern", $"recruit:report:{channelId}", ButtonStyle.Secondary);
        if (message.OwnerControls) builder.WithButton("Your listing", $"recruit:owner:{channelId}", ButtonStyle.Secondary);
        var components = builder.Build();
        if (existing is not null)
        {
            var unarchivedForEdit = false;
            var edit = existing.Content != message.Content || RecruitmentMessage.EmbedKey(existing.Embeds.FirstOrDefault()) != RecruitmentMessage.EmbedKey(message.Embed) ||
                existing.Embeds.Count > (message.Embed is null ? 0 : 1) || existing.Attachments.Count > 0 || ComponentKey(existing.Components) != ComponentKey(components.Components);
            if (edit)
            {
                beforeWrite();
                if (channel is IThreadChannel { IsArchived: true } archived)
                {
                    if (archived.IsLocked && !message.RestoreArchive) throw new InvalidOperationException("A locked listing requires moderator review.");
                    await archived.ModifyAsync(p => p.Archived = false, Options(ct));
                    unarchivedForEdit = true;
                }
                await existing.ModifyAsync(p => { p.Content = message.Content; p.Components = components; p.Attachments = Array.Empty<FileAttachment>(); p.Embeds = message.Embed is null ? Array.Empty<Embed>() : new[] { message.Embed }; p.AllowedMentions = AllowedMentions.None; }, Options(ct));
            }
            // The caller persists this restoration intent before publication, so a restart can finish it.
            if (message.RestoreArchive && channel is IThreadChannel thread && (unarchivedForEdit || !thread.IsArchived))
            {
                if (!edit) beforeWrite();
                await thread.ModifyAsync(p => p.Archived = true, Options(ct));
            }
            return new Delivery(existing.Id.ToString(), false);
        }
        beforeWrite();
        if (channel is IThreadChannel { IsArchived: true } archivedThread)
        {
            if (archivedThread.IsLocked && !message.RestoreArchive) throw new InvalidOperationException("A locked thread needs moderator review before receiving a challenge.");
            await archivedThread.ModifyAsync(p => p.Archived = false, Options(ct));
        }
        var sent = await channel.SendMessageAsync(message.Content, options: Options(ct), allowedMentions: AllowedMentions.None, components: components, embed: message.Embed);
        if (message.RestoreArchive && channel is IThreadChannel restoreThread)
            await restoreThread.ModifyAsync(p => p.Archived = true, Options(ct));
        return new Delivery(sent.Id.ToString(), true);
    }

    // Compare controls, not just presence, so acknowledgement disappears after acceptance.
    private static string ComponentKey(IEnumerable<IMessageComponent> components) => string.Join("|", components.Select(c => c switch
    {
        ActionRowComponent row => "row:" + ComponentKey(row.Components),
        ButtonComponent button => $"{button.CustomId}:{button.Label}:{button.Style}:{button.IsDisabled}:{button.Url}",
        _ => c.Type.ToString()
    }));

    public async Task CloseAsync(ThreadSnapshot thread, string? closedTagId, bool protectPinned, CancellationToken ct, Func<bool>? allowWrite = null)
    {
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
        var live = await ThreadAsync(thread.Id, ct);
        GuardWrite(live, thread, protectPinned);
        if (allowWrite?.Invoke() == false) throw new InvalidOperationException("New recruitment evidence requires reconciliation before this action.");
        var options = Options(ct); options.AuditLogReason = "Recruitment: " + RecruitmentPresentation.Safe(reason);
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
        var channel = await MessageChannelAsync(threadId, ct);
        if (await channel.GetMessageAsync(ulong.Parse(messageId), CacheMode.AllowDownload, Options(ct)) is IUserMessage message && message.Author.Id == BotUserId && (marker is null || message.Content.Contains(marker)))
            await message.DeleteAsync(Options(ct));
    }
}
