using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services.Server;

/// <summary>
/// Posts join, leave and name-change cards to the member-log channel, or to bot-announcement when
/// that isn't set. <see cref="MemberLogSettings"/> picks which name changes get a card; every name
/// change is written to the log file. The layouts are in <see cref="MemberLogCards"/>.
/// </summary>
public class MemberLogService
{
    // Discord adds new members to its search index about 20 s after they join.
    private static readonly TimeSpan JoinSourceFirstDelay = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan JoinSourceRetryDelay = TimeSpan.FromSeconds(20);
    // The audit-log entry can show up a little after the gateway event.
    private static readonly TimeSpan AuditLogDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AuditLogMaxAge = TimeSpan.FromSeconds(30);

    private readonly DiscordSocketClient _client;
    private readonly BotSettings _settings;
    private readonly ILoggingService _loggingService;

    private readonly NameChangeTracker _nameChanges = new();
    private readonly SemaphoreSlim _nameChangeLock = new(1, 1);

    private IMessageChannel? _channel;
    private int _auditLogWarned;
    private int _joinSourceWarned;

    public MemberLogService(DiscordSocketClient client, BotSettings settings, ILoggingService loggingService)
    {
        _client = client;
        _settings = settings;
        _loggingService = loggingService;

        client.UserJoined += EventGuard.Guarded<SocketGuildUser>(UserJoined, nameof(UserJoined));
        client.UserLeft += EventGuard.Guarded<SocketGuild, SocketUser>(UserLeft, nameof(UserLeft));
        client.GuildMemberUpdated += EventGuard.Guarded<Cacheable<SocketGuildUser, ulong>, SocketGuildUser>(GuildMemberUpdated, nameof(GuildMemberUpdated));
        client.UserUpdated += EventGuard.Guarded<SocketUser, SocketUser>(UserUpdated, nameof(UserUpdated));
    }

    #region Join

    private Task UserJoined(SocketGuildUser user)
    {
        if (user.Guild.Id != _settings.GuildId)
            return Task.CompletedTask;
        // The join-source lookup waits for Discord's search index, so don't hold up the gateway.
        LogJoin(user).SafeFireAndForget(nameof(UserJoined));
        return Task.CompletedTask;
    }

    private async Task LogJoin(SocketGuildUser user)
    {
        var member = new MemberLogCards.JoinedMember(
            CardUser.From(user),
            user.CreatedAt,
            user.JoinedAt ?? DateTimeOffset.UtcNow,
            user.IsBot,
            user.AvatarId == null,
            user.Flags.HasFlag(GuildUserFlags.DidRejoin),
            user.IsPending == true,
            user.Flags.HasFlag(GuildUserFlags.AutomodQuarantinedUsername));

        var message = await SendCardAsync(MemberLogCards.JoinedCard(member),
            $"User Joined - `{user.GetPreferredAndUsername()}` - ID : `{user.Id}`");
        if (message == null)
            return;

        var sourceLine = await FindJoinSourceLine(user.Guild, user.Id);
        if (sourceLine == null)
            return;
        try
        {
            await message.ModifyAsync(m =>
            {
                m.Components = MemberLogCards.JoinedCard(member, sourceLine);
                m.Flags = MessageFlags.ComponentsV2;
                m.AllowedMentions = AllowedMentions.None;
            });
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[{nameof(MemberLogService)}] Couldn't add the join source to the join card: {e.Message}", LogSeverity.Warning);
        }
    }

    /// <summary>Asks Discord's members search how the member joined. Null when it can't say.</summary>
    private async Task<string?> FindJoinSourceLine(SocketGuild guild, ulong userId)
    {
        try
        {
            await Task.Delay(JoinSourceFirstDelay);
            var data = await SearchMember(guild, userId);
            if (data == null)
            {
                await Task.Delay(JoinSourceRetryDelay);
                data = await SearchMember(guild, userId);
            }
            return data is { } found ? MemberLogCards.JoinSourceLine(found.JoinSourceType, found.SourceInviteCode, found.InviterId) : null;
        }
        catch (Exception e)
        {
            // Members search is undocumented for bots: never let it break the join card.
            if (Interlocked.Exchange(ref _joinSourceWarned, 1) == 0)
                LoggingService.LogToConsole($"[{nameof(MemberLogService)}] Can't show how members joined: members search failed ({e.Message})", LogSeverity.Warning);
            return null;
        }
    }

    private static async Task<MemberSearchData?> SearchMember(SocketGuild guild, ulong userId)
    {
        var result = await guild.SearchUsersAsyncV2(1, new MemberSearchPropertiesV2
        {
            AndQuery = new MemberSearchFilter { UserId = new MemberSearchSnowflakeQuery { OrQuery = [userId] } },
        });
        foreach (var member in result.Members ?? [])
            if (member.User?.Id == userId)
                return member;
        return null;
    }

    #endregion

    #region Leave

    private Task UserLeft(SocketGuild guild, SocketUser user)
    {
        if (user.IsBot || guild.Id != _settings.GuildId)
            return Task.CompletedTask;

        // Discord.Net passes the removed member when it was cached, so its join date and roles are still there.
        var member = user as SocketGuildUser;
        var roles = member?.Roles
            .Where(r => !r.IsEveryone)
            .OrderByDescending(r => r.Position)
            .Select(r => r.Name)
            .ToList() ?? [];
        var left = new MemberLogCards.LeftMember(CardUser.From(user), member?.JoinedAt, roles);

        // The audit-log lookup waits a moment, so don't hold up the gateway.
        LogLeave(guild, user, left, DateTimeOffset.UtcNow).SafeFireAndForget(nameof(UserLeft));
        return Task.CompletedTask;
    }

    private async Task LogLeave(SocketGuild guild, SocketUser user, MemberLogCards.LeftMember left, DateTimeOffset leftAt)
    {
        var removal = await FindRemovalAudit(guild, user.Id);
        if (removal != null)
            left = left with
            {
                Kind = removal.Action == ActionType.Ban ? MemberLogCards.LeaveKind.Banned : MemberLogCards.LeaveKind.Kicked,
                ModeratorId = removal.User?.Id,
                ModeratorName = removal.User?.GetPreferredAndUsername(),
                Reason = removal.Reason,
            };

        var how = left.Kind switch
        {
            MemberLogCards.LeaveKind.Kicked => "Kicked",
            MemberLogCards.LeaveKind.Banned => "Banned",
            _ => "Left",
        };
        await SendCardAsync(MemberLogCards.LeftCard(left, leftAt),
            $"User {how} - `{user.GetPreferredAndUsername()}` - ID : `{user.Id}`" +
            (left.JoinedAt is { } joinedAt ? $" - After {MemberLogCards.StayDuration(leftAt - joinedAt)}" : ""));
    }

    /// <summary>The kick or ban entry for a member who just left, or null for a plain leave.</summary>
    private async Task<RestAuditLogEntry?> FindRemovalAudit(SocketGuild guild, ulong userId)
    {
        if (!guild.CurrentUser.GuildPermissions.ViewAuditLog)
        {
            WarnAuditLogOnce("missing the View Audit Log permission");
            return null;
        }

        await Task.Delay(AuditLogDelay);
        try
        {
            foreach (var action in new[] { ActionType.Ban, ActionType.Kick })
            {
                var entries = await guild.GetAuditLogsAsync(10, actionType: action).FlattenAsync();
                var entry = entries.FirstOrDefault(e =>
                    DateTimeOffset.UtcNow - e.CreatedAt < AuditLogMaxAge &&
                    e.Data switch
                    {
                        BanAuditLogData ban => ban.Target?.Id == userId,
                        KickAuditLogData kick => kick.Target?.Id == userId,
                        _ => false,
                    });
                if (entry != null)
                    return entry;
            }
            return null;
        }
        catch (HttpException e)
        {
            WarnAuditLogOnce($"audit log request failed ({e.HttpCode})");
            return null;
        }
    }

    private void WarnAuditLogOnce(string reason)
    {
        if (Interlocked.Exchange(ref _auditLogWarned, 1) == 0)
            LoggingService.LogToConsole($"[{nameof(MemberLogService)}] Can't show who kicked or banned members: {reason}", LogSeverity.Warning);
    }

    #endregion

    #region Name changes

    private async Task GuildMemberUpdated(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        // Never download "before": that returns the current state, so the change would be missed.
        if (!before.HasValue || after.IsBot || after.Guild.Id != _settings.GuildId)
            return;
        if (before.Value.Nickname != after.Nickname)
            await LogNameChanges(after, [new(MemberLogCards.NameKind.Nickname, before.Value.Nickname, after.Nickname)]);
    }

    private async Task UserUpdated(SocketUser before, SocketUser after)
    {
        if (after.IsBot)
            return;
        var member = _client.GetGuild(_settings.GuildId)?.GetUser(after.Id);
        if (member == null)
            return;

        var changes = new List<NameChangeTracker.Change>();
        if (before.Username != after.Username)
            changes.Add(new(MemberLogCards.NameKind.Username, before.Username, after.Username));
        if (before.GlobalName != after.GlobalName)
            changes.Add(new(MemberLogCards.NameKind.DisplayName, before.GlobalName, after.GlobalName));
        if (changes.Count > 0)
            await LogNameChanges(member, changes);
    }

    /// <summary>
    /// Writes every change to the log file, then posts a card for the kinds the settings allow, or
    /// edits the user's card from the last <see cref="NameChangeTracker.Window"/>.
    /// </summary>
    private async Task LogNameChanges(SocketGuildUser member, IReadOnlyList<NameChangeTracker.Change> changes)
    {
        foreach (var change in changes)
            await _loggingService.Log(LogBehaviour.File,
                $"User {change.Kind} Changed - `{change.Before}` -> `{change.After}` - ID : `{member.Id}`");

        var shown = changes.Where(c => CardEnabled(c.Kind)).ToList();
        if (shown.Count == 0)
            return;

        // One at a time, so two quick changes can't both post a new card.
        await _nameChangeLock.WaitAsync();
        try
        {
            var entry = _nameChanges.Record(member.Id, shown, DateTimeOffset.UtcNow);
            var card = MemberLogCards.NameChangedCard(CardUser.From(member), entry.Fields, entry.FirstAt, entry.LastAt);
            if (entry.MessageId is { } messageId && await TryEditCardAsync(messageId, card))
                return;
            // New entry, or the old card is gone (deleted by a moderator, say): post a new one.
            var message = await PostCardAsync(card);
            if (message != null)
                _nameChanges.SetMessage(member.Id, message.Id);
        }
        finally
        {
            _nameChangeLock.Release();
        }
    }

    private bool CardEnabled(MemberLogCards.NameKind kind) =>
        kind == MemberLogCards.NameKind.Username ? _settings.MemberLog.UsernameChanges : _settings.MemberLog.DisplayNameChanges;

    private async Task<bool> TryEditCardAsync(ulong messageId, MessageComponent card)
    {
        var channel = _channel ??= ResolveChannel();
        if (channel == null)
            return false;
        try
        {
            await channel.ModifyMessageAsync(messageId, m =>
            {
                m.Components = card;
                m.Flags = MessageFlags.ComponentsV2;
                m.AllowedMentions = AllowedMentions.None;
            });
            return true;
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[{nameof(MemberLogService)}] Couldn't update name-change card, posting a new one: {e.Message}", LogSeverity.Warning);
            return false;
        }
    }

    #endregion

    /// <summary>Sends a card to the member-log channel and writes <paramref name="fileLine"/> to the log file. Null when the card couldn't be sent.</summary>
    private async Task<IUserMessage?> SendCardAsync(MessageComponent card, string fileLine)
    {
        await _loggingService.Log(LogBehaviour.File, fileLine);
        return await PostCardAsync(card);
    }

    /// <summary>Sends a card to the member-log channel. Null when it couldn't be sent.</summary>
    private async Task<IUserMessage?> PostCardAsync(MessageComponent card)
    {
        var channel = _channel ??= ResolveChannel();
        if (channel == null)
            return null;
        try
        {
            return await channel.SendMessageAsync(components: card, flags: MessageFlags.ComponentsV2, allowedMentions: AllowedMentions.None);
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[{nameof(MemberLogService)}] Couldn't send member log card: {e.Message}", LogSeverity.Warning);
            return null;
        }
    }

    private IMessageChannel? ResolveChannel()
    {
        var memberLogId = _settings.Channels.MemberLog?.Id ?? 0;
        if (memberLogId != 0 && _client.GetChannel(memberLogId) is IMessageChannel memberLog)
            return memberLog;
        var announcementId = _settings.Channels.BotAnnouncement?.Id ?? 0;
        return announcementId != 0 ? _client.GetChannel(announcementId) as IMessageChannel : null;
    }
}
