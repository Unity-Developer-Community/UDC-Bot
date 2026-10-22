using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Settings;
using static DiscordBot.Services.Recruitment.RecruitmentRequests;

namespace DiscordBot.Services.Recruitment.Infrastructure.Discord;

internal sealed class DiscordReferencePublisher(
    DiscordSocketClient client,
    BotSettings settings,
    Func<string, CancellationToken, Task<RestForumChannel>> readForum,
    Func<ulong> botIdentity,
    Func<string, string, CancellationToken, Task<bool>> isModerator,
    Action<string> invalidateFacts)
{
    private readonly DiscordSocketClient _client = client;
    private readonly BotSettings _settings = settings;
    internal const string ReferenceTitle = "Read First · Recruitment Information";
    public async Task<string> CreateReferenceAsync(string forumId, string closedTagId, string marker, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        var forum = await readForum(forumId, ct);
        var tags = forum.Tags.Where(t => t.Id.ToString() == closedTagId).ToArray();
        if (tags.Length != 1) throw new InvalidOperationException("Reference post needs a verified Closed tag.");
        var thread = await forum.CreatePostAsync(ReferenceTitle, ThreadArchiveDuration.OneWeek,
            text: ReferenceIntroduction(marker), tags: tags, allowedMentions: AllowedMentions.None, options: Options(ct));
        return thread.Id.ToString();
    }

    internal static string ReferenceIntroduction(string marker) =>
        "Read the guidance below and this forum's Guidelines before posting. Keep first contact, scope, timing and payment in the listing; use DMs only for private information.\n\n" +
        "Moderators and administrators maintain the information below.\n" + $"||{marker}||";

    public async Task<bool> SecureReferenceAsync(string forumId, string threadId, bool botOwned, string marker, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        RestThreadChannel thread;
        try
        {
            thread = await _client.Rest.GetChannelAsync(ulong.Parse(threadId), Options(ct)) as RestThreadChannel
                ?? throw new InvalidOperationException("Reference is not a forum thread.");
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownChannel) { return false; }
        if (thread.GuildId != _settings.GuildId || thread.ParentChannelId.ToString() != forumId || !_settings.Recruitment.Forums.Contains(thread.ParentChannelId))
            throw new InvalidOperationException("Reference forum identity does not match.");
        if (botOwned)
        {
            var starter = await thread.GetMessageAsync(thread.Id, Options(ct));
            var botUserId = botIdentity();
            if (((IThreadChannel)thread).OwnerId != botUserId || starter?.Author.Id != botUserId || !starter.Content.Contains(marker, StringComparison.Ordinal))
                throw new InvalidOperationException("Reference bot ownership or marker does not match.");
        }
        else if (!await isModerator(_settings.GuildId.ToString(), ((IThreadChannel)thread).OwnerId.ToString(), ct))
            throw new InvalidOperationException("Existing reference is no longer owned by a moderator or administrator.");
        if (thread.IsLocked && !thread.IsArchived && thread.Flags.HasFlag(ChannelFlags.Pinned)) return true;
        // Forum threads inherit permissions. Locking is the supported member-posting restriction;
        // Staff with Manage Threads can maintain it. Archiving would clear its forum pin.
        if (thread.IsArchived) await thread.ModifyAsync(p => { p.Archived = false; p.Flags = thread.Flags; }, Options(ct));
        await thread.ModifyAsync(p => { p.Flags = thread.Flags | ChannelFlags.Pinned; p.Locked = true; p.Archived = false; }, Options(ct));
        var verified = await _client.Rest.GetChannelAsync(thread.Id, Options(ct)) as RestThreadChannel;
        if (verified is null || verified.IsArchived || !verified.IsLocked || !verified.Flags.HasFlag(ChannelFlags.Pinned))
            throw new InvalidOperationException("Reference pin/lock was not confirmed.");
        invalidateFacts(threadId);
        return true;
    }
}
