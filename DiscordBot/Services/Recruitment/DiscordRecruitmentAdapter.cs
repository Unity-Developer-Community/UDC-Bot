using System.IO;
using System.Net;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Recruitment;

public sealed class DiscordRecruitmentAdapter : IRecruitmentDiscord
{
    private readonly DiscordSocketClient _client;
    private readonly BotSettings _settings;
    private readonly DatabaseService _database;
    public DiscordRecruitmentAdapter(DiscordSocketClient client, BotSettings settings, DatabaseService database)
        => (_client, _settings, _database) = (client, settings, database);
    private static RequestOptions Options(CancellationToken ct) => new() { CancelToken = ct, Timeout = 15000 };
    private SocketGuild Guild => _client.GetGuild(_settings.GuildId) ?? throw new InvalidOperationException("Configured guild is unavailable.");

    public async Task ValidateAccessAsync(CancellationToken ct)
    {
        if (_settings.Roles.Moderator == 0 || Guild.GetRole(_settings.Roles.Moderator) is null)
            throw new InvalidOperationException("The configured moderator role is unavailable.");
        foreach (var id in _settings.Recruitment.Forums.Mapping().Values)
        {
            var forum = await ForumAsync(id.ToString(), ct);
            var cached = Guild.GetChannel(id) ?? throw new InvalidOperationException("Forum permission cache unavailable.");
            var perms = Guild.CurrentUser.GetPermissions(cached);
            if (!perms.ViewChannel || !perms.ReadMessageHistory || !perms.SendMessagesInThreads || !perms.ManageThreads || !perms.ManageChannel)
                throw new InvalidOperationException("Recruitment needs View Channel, Read Message History, Send Messages in Threads, Manage Threads and Manage Channels on each forum.");
        }
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
            else if (overwrite.TargetId == _client.CurrentUser.Id || await IsModeratorAsync(_settings.GuildId.ToString(), overwrite.TargetId.ToString(), ct)) continue;
            throw new InvalidOperationException("The recruitment feed grants access outside the configured moderator/admin roles.");
        }
    }

    public async Task<bool> IsModeratorAsync(string guildId, string userId, CancellationToken ct)
    {
        if (guildId != _settings.GuildId.ToString() || !ulong.TryParse(userId, out var id)) return false;
        var user = await _client.Rest.GetGuildUserAsync(_settings.GuildId, id, Options(ct));
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
        return new ForumSnapshot(forumId, forum.Topic ?? "", (forum.Flags & ChannelFlags.RequireTag) != 0,
            forum.Tags.Select(t => new LiveTag(t.Id.ToString(), t.Name, t.IsModerated, t.Emoji)).ToArray());
    }

    public async Task PublishForumAsync(string forumId, string topic, IReadOnlyList<LiveTag> tags, CancellationToken ct)
    {
        var forum = await ForumAsync(forumId, ct);
        await forum.ModifyAsync(p =>
        {
            p.Topic = topic;
            p.Flags = forum.Flags | ChannelFlags.RequireTag;
            p.Tags = tags.Select(t =>
            {
                var builder = new ForumTagBuilder(t.Name, string.IsNullOrEmpty(t.Id) ? null : ulong.Parse(t.Id), t.Moderated);
                if (t.Emoji is not null) builder.WithEmoji(t.Emoji);
                return builder.Build();
            }).ToArray();
        }, Options(ct));
    }

    public async Task<ThreadInventory> InventoryAsync(DateTimeOffset floor, IReadOnlySet<string> knownIds,
        IReadOnlyDictionary<string, ArchiveCursor> cursors, CancellationToken ct)
    {
        var found = new Dictionary<ulong, RestThreadChannel>();
        var progress = new Dictionary<string, ArchiveCursor>();
        var complete = true;
        foreach (var id in _settings.Recruitment.Forums.Mapping().Values)
        {
            var forum = await ForumAsync(id.ToString(), ct);
            foreach (var thread in await forum.GetActiveThreadsAsync(Options(ct))) found[thread.Id] = thread;
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
        var unseen = found.Values.Where(t => t.CreatedAt >= floor && !knownIds.Contains(t.Id.ToString())).ToArray();
        foreach (var thread in unseen.Take(50)) snapshots.Add(await SnapshotAsync(thread, ct));
        // Do not advance past an archive page until all its unknown identities have been retained.
        return unseen.Length > 50 ? new ThreadInventory(snapshots, false, cursors) : new ThreadInventory(snapshots, complete, progress);
    }

    public async Task<ThreadSnapshot?> GetThreadAsync(string threadId, CancellationToken ct)
    {
        try
        {
            var channel = await _client.Rest.GetChannelAsync(ulong.Parse(threadId), Options(ct));
            if (channel is not RestThreadChannel thread || thread.GuildId != _settings.GuildId || !_settings.Recruitment.Forums.Contains(thread.ParentChannelId))
                throw new InvalidOperationException("Thread is outside configured recruitment forums.");
            return await SnapshotAsync(thread, ct);
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownChannel) { return null; }
    }

    private async Task<ThreadSnapshot> SnapshotAsync(RestThreadChannel thread, CancellationToken ct)
    {
        var ownerId = ((IThreadChannel)thread).OwnerId;
        var user = await _client.Rest.GetGuildUserAsync(_settings.GuildId, ownerId, Options(ct));
        var starter = await thread.GetMessageAsync(thread.Id, Options(ct));
        return new ThreadSnapshot(thread.Id.ToString(), thread.ParentChannelId.ToString(), ownerId.ToString(), thread.Name,
            thread.CreatedAt.ToUniversalTime(), user?.IsBot ?? starter?.Author.IsBot ?? false,
            (thread.Flags & ChannelFlags.Pinned) != 0, thread.IsArchived, thread.IsLocked,
            thread.AppliedTags.Select(t => t.ToString()).ToArray(), starter?.Content,
            SnowflakeUtils.FromSnowflake(ownerId), user?.JoinedAt);
    }

    public async Task<bool?> HasReplyAsync(ThreadSnapshot thread, CancellationToken ct)
    {
        var channel = await MessageChannelAsync(thread.Id, ct);
        var unknown = false;
        // Positive evidence can be recovered; this scan never certifies complete historical coverage.
        foreach (var message in await channel.GetMessagesAsync(100, CacheMode.AllowDownload, Options(ct)).FlattenAsync())
        {
            if (message.Id.ToString() == thread.Id || message.Author.Id.ToString() == thread.OwnerId || message.Author.IsBot || message.Source == MessageSource.Webhook) continue;
            var user = await _client.Rest.GetGuildUserAsync(_settings.GuildId, message.Author.Id, Options(ct));
            if (user is null) { unknown = true; continue; }
            if (!user.GuildPermissions.Administrator && !user.RoleIds.Contains(_settings.Roles.Moderator)) return true;
        }
        return unknown ? null : false;
    }

    public async Task<string> GetActivityAsync(string userId, CancellationToken ct)
    {
        try
        {
            var query = _database.Query;
            if (query is null) return "unknown";
            var user = await query.GetUser(userId).WaitAsync(TimeSpan.FromSeconds(5), ct);
            if (user is null) return "unknown";
            return user.Exp > 0 || user.Karma > 0 || user.KarmaGiven > 0 ? "recorded" : "none recorded";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return "unknown"; }
    }

    private async Task<IMessageChannel> MessageChannelAsync(string id, CancellationToken ct)
    {
        var channel = await _client.Rest.GetChannelAsync(ulong.Parse(id), Options(ct));
        if (channel is not IGuildChannel guildChannel || guildChannel.GuildId != _settings.GuildId || channel is not IMessageChannel messages)
            throw new InvalidOperationException("Recruitment message channel unavailable.");
        return messages;
    }

    public async Task<Delivery> UpsertAsync(string channelId, string? messageId, bool recovering, DateTimeOffset since, RecruitmentMessage message, Action beforeWrite, CancellationToken ct)
    {
        var channel = await MessageChannelAsync(channelId, ct);
        IUserMessage? existing = null;
        if (messageId is not null) existing = await channel.GetMessageAsync(ulong.Parse(messageId), CacheMode.AllowDownload, Options(ct)) as IUserMessage;
        if (existing is not null && (existing.Author.Id != _client.CurrentUser.Id || !existing.Content.Contains(message.Marker)))
            throw new InvalidOperationException("Stored recruitment message identity does not match.");
        if (existing is null && recovering)
        {
            ulong? before = null;
            var covered = false;
            for (var page = 0; page < 20; page++)
            {
                var messages = (before.HasValue ? await channel.GetMessagesAsync(before.Value, Direction.Before, 100, CacheMode.AllowDownload, Options(ct)).FlattenAsync()
                    : await channel.GetMessagesAsync(100, CacheMode.AllowDownload, Options(ct)).FlattenAsync()).ToArray();
                existing = messages.OfType<IUserMessage>().FirstOrDefault(m => m.Author.Id == _client.CurrentUser.Id && m.Content.Contains(message.Marker));
                if (existing is not null) break;
                if (messages.Length < 100 || messages.Min(m => m.Timestamp) <= since.AddMinutes(-5)) { covered = true; break; }
                before = messages.Min(m => m.Id);
            }
            if (existing is null && !covered) throw new InvalidOperationException("Message recovery history exceeds the scan bound; staff review required before resending.");
        }
        var components = message.OwnerControls ? new ComponentBuilder()
            .WithButton("Acknowledge", $"recruit:ack:{channelId}", ButtonStyle.Primary)
            .WithButton("Close listing", $"recruit:close:{channelId}", ButtonStyle.Secondary)
            .WithButton("Remove post", $"recruit:remove:{channelId}", ButtonStyle.Danger).Build() : new ComponentBuilder().Build();
        if (existing is not null)
        {
            var unarchivedForEdit = false;
            var edit = existing.Content != message.Content || existing.Components.Count == 0 && message.OwnerControls || existing.Components.Count > 0 && !message.OwnerControls;
            if (edit)
            {
                beforeWrite();
                if (channel is IThreadChannel { IsArchived: true } archived)
                {
                    if (archived.IsLocked && !message.RestoreArchive) throw new InvalidOperationException("A locked listing requires moderator review.");
                    await archived.ModifyAsync(p => p.Archived = false, Options(ct));
                    unarchivedForEdit = true;
                }
                await existing.ModifyAsync(p => { p.Content = message.Content; p.Components = components; p.AllowedMentions = AllowedMentions.None; }, Options(ct));
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
        IUserMessage sent;
        if (message.Image is not null)
        {
            using var stream = new MemoryStream(message.Image, false);
            try
            {
                sent = await channel.SendFileAsync(new FileAttachment(stream, "recruitment.png", message.ImageDescription), message.Content,
                    options: Options(ct), allowedMentions: AllowedMentions.None, components: components);
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.BadRequest || ex.DiscordCode == DiscordErrorCode.MissingPermissions)
            {
                // These are definitive rejections, so a text fallback cannot duplicate an ambiguous successful send.
                sent = await channel.SendMessageAsync(message.Content, options: Options(ct), allowedMentions: AllowedMentions.None, components: components);
            }
        }
        else sent = await channel.SendMessageAsync(message.Content, options: Options(ct), allowedMentions: AllowedMentions.None, components: components);
        if (message.RestoreArchive && channel is IThreadChannel restoreThread)
            await restoreThread.ModifyAsync(p => p.Archived = true, Options(ct));
        return new Delivery(sent.Id.ToString(), true);
    }

    public async Task CloseAsync(ThreadSnapshot thread, string? closedTagId, bool protectPinned, CancellationToken ct)
    {
        var live = await ThreadAsync(thread.Id, ct);
        GuardWrite(live, thread, protectPinned);
        var tags = live.AppliedTags.ToList();
        if (closedTagId is not null && tags.Count < 5 && !tags.Contains(ulong.Parse(closedTagId))) tags.Add(ulong.Parse(closedTagId));
        await live.ModifyAsync(p => { p.AppliedTags = tags; p.Locked = true; p.Archived = true; }, Options(ct));
        var verified = await ThreadAsync(thread.Id, ct);
        if (!verified.IsLocked || !verified.IsArchived) throw new InvalidOperationException("Thread closure was not confirmed.");
    }
    public async Task ReopenAsync(ThreadSnapshot thread, string? closedTagId, CancellationToken ct)
    {
        var live = await ThreadAsync(thread.Id, ct);
        GuardWrite(live, thread, false);
        await live.ModifyAsync(p => { p.AppliedTags = live.AppliedTags.Where(t => t.ToString() != closedTagId).ToArray(); p.Locked = false; p.Archived = false; }, Options(ct));
        var verified = await ThreadAsync(thread.Id, ct);
        if (verified.IsLocked || verified.IsArchived) throw new InvalidOperationException("Thread reopening was not confirmed.");
    }
    private async Task<RestThreadChannel> ThreadAsync(string id, CancellationToken ct) =>
        await _client.Rest.GetChannelAsync(ulong.Parse(id), Options(ct)) as RestThreadChannel ?? throw new InvalidOperationException("Thread unavailable.");
    public async Task DeleteAsync(ThreadSnapshot thread, string reason, bool protectPinned, CancellationToken ct)
    {
        var live = await ThreadAsync(thread.Id, ct);
        GuardWrite(live, thread, protectPinned);
        var options = Options(ct); options.AuditLogReason = "Recruitment: " + RecruitmentPresentation.Safe(reason);
        await live.DeleteAsync(options);
    }
    private void GuardWrite(RestThreadChannel live, ThreadSnapshot expected, bool protectPinned)
    {
        if (live.GuildId != _settings.GuildId || live.ParentChannelId.ToString() != expected.ForumId ||
            ((IThreadChannel)live).OwnerId.ToString() != expected.OwnerId || !_settings.Recruitment.Forums.Contains(live.ParentChannelId) ||
            protectPinned && (live.Flags & ChannelFlags.Pinned) != 0)
            throw new InvalidOperationException("Thread identity or protection changed before the action.");
    }
    public async Task DeleteAdvisoryAsync(string threadId, string messageId, CancellationToken ct)
    {
        var channel = await MessageChannelAsync(threadId, ct);
        if (await channel.GetMessageAsync(ulong.Parse(messageId), CacheMode.AllowDownload, Options(ct)) is IUserMessage message && message.Author.Id == _client.CurrentUser.Id)
            await message.DeleteAsync(Options(ct));
    }
}
