using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Recruitment.Runtime;

internal interface IRecruitmentGatewaySink
{
    void NotifyThread(string id);
    void NotifyCreation(ThreadCreation creation);
    void NotifyFactsChanged(string id);
    void NotifyThreadDeletion(string id);
    void NotifyMessageDeletion(string channel, IReadOnlySet<string> ids);
    void SetGatewayReady(bool ready);
    void ScheduleRecovery();
    bool TryGetOwnerThreads(string owner, out string[] threads);
}

/// <summary>Translates gateway events into queued work without reading mutable listing state.</summary>
internal sealed class RecruitmentGateway(
    DiscordSocketClient client,
    BotSettings settings,
    IRecruitmentDiscord discord,
    IRecruitmentGatewaySink sink)
{
    private readonly DiscordSocketClient _client = client;
    private readonly BotSettings _settings = settings;
    private readonly IRecruitmentDiscord _discord = discord;
    private readonly IRecruitmentGatewaySink _sink = sink;
    private Task MessageUpdated(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        if (channel is SocketThreadChannel thread && IsRecruitment(thread) && after.Id == thread.Id &&
            (!before.HasValue || before.Value.Content != after.Content ||
                !before.Value.Attachments.Select(a => a.Id).SequenceEqual(after.Attachments.Select(a => a.Id)))) _sink.NotifyFactsChanged(thread.Id.ToString());
        return Task.CompletedTask;
    }

    private Task ThreadCreated(SocketThreadChannel thread)
    {
        if (IsRecruitment(thread)) _sink.NotifyCreation(new ThreadCreation(thread.Id.ToString(), thread.ParentChannel.Id.ToString(),
            ((IThreadChannel)thread).OwnerId.ToString(), thread.Name, thread.CreatedAt));
        return Task.CompletedTask;
    }

    private Task ThreadUpdated(Cacheable<SocketThreadChannel, ulong> before, SocketThreadChannel after)
    {
        if (IsRecruitment(after) && (!before.HasValue || before.Value.Flags != after.Flags || before.Value.IsArchived != after.IsArchived ||
            before.Value.IsLocked != after.IsLocked || !before.Value.AppliedTags.SequenceEqual(after.AppliedTags))) _sink.NotifyThread(after.Id.ToString());
        return Task.CompletedTask;
    }

    private Task ThreadDeleted(Cacheable<SocketThreadChannel, ulong> thread)
    {
        _sink.NotifyThreadDeletion(thread.Id.ToString());
        return Task.CompletedTask;
    }

    private bool IsRecruitment(SocketThreadChannel thread) => thread.Guild.Id == _settings.GuildId && _settings.Recruitment.Forums.Contains(thread.ParentChannel.Id);

    private Task MessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel) =>
        MessagesDeleted(new[] { message }, channel);

    private Task MessagesDeleted(IReadOnlyCollection<Cacheable<IMessage, ulong>> messages, Cacheable<IMessageChannel, ulong> channel)
    {
        _sink.NotifyMessageDeletion(channel.Id.ToString(), messages.Select(m => m.Id.ToString()).ToHashSet());
        return Task.CompletedTask;
    }

    private Task Disconnected(Exception _) { _sink.SetGatewayReady(false); return Task.CompletedTask; }

    private Task Ready()
    {
        if (_client?.GetGuild(_settings.GuildId)?.IsConnected == true) { _sink.SetGatewayReady(true); _sink.ScheduleRecovery(); }
        return Task.CompletedTask;
    }

    private Task GuildAvailable(SocketGuild guild) { if (guild.Id == _settings.GuildId) { _sink.SetGatewayReady(true); _sink.ScheduleRecovery(); } return Task.CompletedTask; }

    private Task GuildUnavailable(SocketGuild guild) { if (guild.Id == _settings.GuildId) { _sink.SetGatewayReady(false); } return Task.CompletedTask; }

    private Task ChannelUpdated(SocketChannel before, SocketChannel after)
    {
        if (after is not SocketGuildChannel channel || channel.Guild.Id != _settings.GuildId) return Task.CompletedTask;
        var permissionsChanged = before is not SocketGuildChannel prior || !prior.PermissionOverwrites.SequenceEqual(channel.PermissionOverwrites) ||
            (before as INestedChannel)?.CategoryId != (after as INestedChannel)?.CategoryId;
        if (after is SocketForumChannel forum && _settings.Recruitment.Forums.Contains(forum.Id))
        {
            var snapshot = new ForumSnapshot(forum.Id.ToString(), forum.Topic ?? "", (forum.Flags & ChannelFlags.RequireTag) != 0,
                forum.Tags.Select(t => new LiveTag(t.Id.ToString(), t.Name, t.IsModerated, t.Emoji)).ToArray(), forum.Flags);
            if (!permissionsChanged && _discord.IsExpectedForumUpdate(snapshot)) return Task.CompletedTask;
        }
        else if (!permissionsChanged || after.Id != _settings.Recruitment.FeedChannelId && after is not SocketCategoryChannel) return Task.CompletedTask;
        _sink.ScheduleRecovery(); return Task.CompletedTask;
    }

    private Task ChannelDestroyed(SocketChannel channel)
    {
        if (channel is SocketGuildChannel guild && guild.Guild.Id == _settings.GuildId &&
            (channel.Id == _settings.Recruitment.FeedChannelId || _settings.Recruitment.Forums.Contains(channel.Id) || channel is SocketCategoryChannel)) _sink.ScheduleRecovery();
        return Task.CompletedTask;
    }

    private Task RoleUpdated(SocketRole before, SocketRole after)
    { if (after.Guild.Id == _settings.GuildId && !before.Permissions.Equals(after.Permissions)) _sink.ScheduleRecovery(); return Task.CompletedTask; }

    private Task RoleDeleted(SocketRole role) { if (role.Guild.Id == _settings.GuildId) _sink.ScheduleRecovery(); return Task.CompletedTask; }

    private Task UserUpdated(SocketUser before, SocketUser after)
    {
        if ((before.AvatarId != after.AvatarId || before.Username != after.Username || before.GlobalName != after.GlobalName) && _sink.TryGetOwnerThreads(after.Id.ToString(), out var threads))
            foreach (var id in threads) _sink.NotifyFactsChanged(id);
        return Task.CompletedTask;
    }

    private Task MemberUpdated(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        if (after.Guild.Id != _settings.GuildId || before.HasValue && before.Value.Roles.Select(r => r.Id).SequenceEqual(after.Roles.Select(r => r.Id)) && before.Value.DisplayAvatarId == after.DisplayAvatarId && before.Value.Nickname == after.Nickname && before.Value.GlobalName == after.GlobalName && before.Value.Username == after.Username) return Task.CompletedTask;
        if (after.Id == _client?.CurrentUser.Id) _sink.ScheduleRecovery();
        else if (_sink.TryGetOwnerThreads(after.Id.ToString(), out var threads))
            foreach (var id in threads) _sink.NotifyFactsChanged(id);
        return Task.CompletedTask;
    }

    public void Subscribe(bool subscribe)
    {
        if (_client is null || !_settings.Recruitment.Enabled) return;
        if (subscribe)
        {
            _client.ThreadCreated += ThreadCreated; _client.ThreadUpdated += ThreadUpdated; _client.ThreadDeleted += ThreadDeleted;
            _client.MessageDeleted += MessageDeleted; _client.MessagesBulkDeleted += MessagesDeleted;
            _client.MessageUpdated += MessageUpdated;
            _client.Disconnected += Disconnected; _client.Ready += Ready; _client.GuildAvailable += GuildAvailable; _client.GuildUnavailable += GuildUnavailable;
            _client.ChannelUpdated += ChannelUpdated; _client.ChannelDestroyed += ChannelDestroyed;
            _client.RoleUpdated += RoleUpdated; _client.RoleDeleted += RoleDeleted; _client.GuildMemberUpdated += MemberUpdated; _client.UserUpdated += UserUpdated;
        }
        else
        {
            _client.ThreadCreated -= ThreadCreated; _client.ThreadUpdated -= ThreadUpdated; _client.ThreadDeleted -= ThreadDeleted;
            _client.MessageDeleted -= MessageDeleted; _client.MessagesBulkDeleted -= MessagesDeleted;
            _client.MessageUpdated -= MessageUpdated;
            _client.Disconnected -= Disconnected; _client.Ready -= Ready; _client.GuildAvailable -= GuildAvailable; _client.GuildUnavailable -= GuildUnavailable;
            _client.ChannelUpdated -= ChannelUpdated; _client.ChannelDestroyed -= ChannelDestroyed;
            _client.RoleUpdated -= RoleUpdated; _client.RoleDeleted -= RoleDeleted; _client.GuildMemberUpdated -= MemberUpdated; _client.UserUpdated -= UserUpdated;
        }
    }

}
