using System.IO;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment;

public sealed class DiscordRecruitmentPublisher(DiscordSocketClient client, RecruitmentForumClassifier classifier,
    IOptions<DiscordGuildOptions> guild, IOptions<AuthorizationOptions> authorization) : IRecruitmentPublisher
{
    private static RequestOptions Request(CancellationToken token) => new() { CancelToken = token, Timeout = 15000 };

    private async Task<IForumChannel> ForumAsync(ulong id, CancellationToken token)
    {
        IChannel channel = await ((IDiscordClient)client.Rest).GetChannelAsync(id, CacheMode.AllowDownload, Request(token));
        if (channel is not IForumChannel forum || forum.GuildId != guild.Value.GuildId || classifier.Classify(id) is null)
        {
            throw new InvalidOperationException("Public setup requires a configured forum in the configured guild.");
        }
        return forum;
    }

    public async Task<RecruitmentForumSetup> GetForumAsync(ulong forumId, CancellationToken token) =>
        Snapshot(await ForumAsync(forumId, token));

    public async Task AppendClosedTagAsync(ulong forumId, string expectedTagHash, CancellationToken token)
    {
        IForumChannel forum = await ForumAsync(forumId, token);
        RecruitmentForumSetup before = Snapshot(forum);
        if (RecruitmentGuidelines.TagHash(before.Tags) != expectedTagHash || before.Tags.Count >= 20)
        {
            throw new InvalidOperationException("Tag inventory changed before append; setup will retry from a fresh inventory.");
        }
        // Preserve complete tag objects: omitting IDs would replace existing metadata.
        IForumTag[] tags = forum.Tags.Cast<IForumTag>().Append(new ForumTagBuilder("Closed", null, false, (IEmote?)null).Build()).ToArray();
        await forum.ModifyAsync(properties => properties.Tags = tags, Request(token));
        RecruitmentForumSetup after = await GetForumAsync(forumId, token);
        if (!before.Tags.All(tag => after.Tags.Contains(tag)))
        {
            throw new InvalidOperationException("Existing tags changed during setup; staff review is required.");
        }
    }

    public async Task PublishTopicAsync(ulong forumId, string expectedTopicHash, string topic, CancellationToken token)
    {
        IForumChannel forum = await ForumAsync(forumId, token);
        if (RecruitmentGuidelines.Hash(forum.Topic ?? "") != expectedTopicHash)
        {
            throw new InvalidOperationException("Guidelines changed before publication; preview/adoption is required.");
        }
        await forum.ModifyAsync(properties => properties.Topic = topic, Request(token));
    }

    private async Task<RestThreadChannel?> ThreadAsync(ulong id, CancellationToken token)
    {
        try
        {
            IChannel? channel = await ((IDiscordClient)client.Rest).GetChannelAsync(id, CacheMode.AllowDownload, Request(token));
            if (channel is null) return null;
            if (channel is not RestThreadChannel thread || thread.GuildId != guild.Value.GuildId ||
                classifier.Classify(id, thread.ParentChannelId) is null)
            {
                throw new InvalidOperationException("Public operation is outside the configured recruitment forums.");
            }
            return thread;
        }
        catch (HttpException error) when (error.DiscordCode == DiscordErrorCode.UnknownChannel) { return null; }
    }

    public async Task<RecruitmentPublicPost?> GetPostAsync(ulong threadId, CancellationToken token)
    {
        RestThreadChannel? thread = await ThreadAsync(threadId, token);
        if (thread is null) return null;
        RestGuild restGuild = await client.Rest.GetGuildAsync(guild.Value.GuildId, Request(token));
        IGuildUser? owner = await restGuild.GetUserAsync(thread.OwnerId, Request(token));
        bool? ordinary = owner is null ? null : !owner.IsBot && !owner.IsWebhook &&
            !owner.GuildPermissions.Administrator && !owner.RoleIds.Contains(authorization.Value.ModeratorRoleId);
        return new(thread.Id, thread.ParentChannelId, thread.OwnerId, thread.Flags.HasFlag(ChannelFlags.Pinned),
            thread.IsArchived, thread.IsLocked, ordinary, thread.AppliedTags.ToArray());
    }

    public async Task<RecruitmentAdvisorySearch> FindAdvisoryAsync(ulong threadId, string marker, ulong? beforeId, CancellationToken token)
    {
        IMessageChannel channel = await ThreadAsync(threadId, token) ?? throw new InvalidOperationException("Post unavailable during advisory recovery.");
        IMessage[] messages = (await (beforeId is { } before
            ? channel.GetMessagesAsync(before, Direction.Before, 100, CacheMode.AllowDownload, Request(token))
            : channel.GetMessagesAsync(100, CacheMode.AllowDownload, Request(token))).FlattenAsync()).ToArray();
        IMessage? found = messages.FirstOrDefault(message => Owned(message, marker));
        return new(found is null ? null : new(found.Id, found.Timestamp),
            messages.Length == 0 ? null : messages.Min(message => message.Id), messages.Length < 100);
    }

    public async Task<RecruitmentPublicMessage> SendAdvisoryAsync(ulong threadId, RecruitmentAdvisoryView view, byte[]? image, CancellationToken token)
    {
        var thread = await ThreadAsync(threadId, token) ?? throw new InvalidOperationException("Post unavailable before advisory send.");
        // Sending to an archived thread implicitly unarchives it. Advisory must not do that automatically.
        if (thread.IsArchived || thread.IsLocked) throw new InvalidOperationException("Archived or locked post; public advisory is paused.");
        IUserMessage sent;
        if (image is null)
        {
            sent = await ((IMessageChannel)thread).SendMessageAsync(embed: view.Embed, components: view.Components,
                allowedMentions: AllowedMentions.None, options: Request(token));
        }
        else
        {
            using var stream = new MemoryStream(image, writable: false);
            var attachment = new FileAttachment(stream, "recruitment.png", description: view.ImageDescription);
            Embed embed = view.Embed.ToEmbedBuilder().WithImageUrl("attachment://recruitment.png").Build();
            sent = await ((IMessageChannel)thread).SendFileAsync(attachment, embed: embed, components: view.Components,
                allowedMentions: AllowedMentions.None, options: Request(token));
        }
        return new(sent.Id, sent.Timestamp.ToUniversalTime());
    }

    public async Task<bool> EditAdvisoryAsync(ulong threadId, ulong messageId, RecruitmentAdvisoryView view, CancellationToken token)
    {
        IMessageChannel channel = await ThreadAsync(threadId, token) ?? throw new InvalidOperationException("Post unavailable before advisory refresh.");
        IMessage? message;
        try { message = await channel.GetMessageAsync(messageId, CacheMode.AllowDownload, Request(token)); }
        catch (HttpException error) when (error.DiscordCode == DiscordErrorCode.UnknownMessage) { return false; }
        if (message is null) return false;
        if (message is not IUserMessage owned || !Owned(message, view.Marker))
        {
            throw new InvalidOperationException("Saved advisory is not owned by this recruitment record.");
        }
        EmbedBuilder embed = view.Embed.ToEmbedBuilder();
        if (message.Attachments.FirstOrDefault(file => file.Filename == "recruitment.png") is { } attachment)
        {
            embed.WithImageUrl(attachment.Url);
        }
        await owned.ModifyAsync(properties =>
        {
            properties.Embed = embed.Build();
            properties.Components = view.Components;
            properties.AllowedMentions = AllowedMentions.None;
        }, Request(token));
        return true;
    }

    public async Task ApplyLifecycleActionAsync(RecruitmentPublicPost expected, RecruitmentActionKind action, ulong? closedTagId,
        string actionId, CancellationToken token)
    {
        RestThreadChannel? thread = await ThreadAsync(expected.Id, token);
        if (thread is null && action == RecruitmentActionKind.Delete) return;
        if (thread is null || thread.OwnerId != expected.AuthorId || thread.Flags.HasFlag(ChannelFlags.Pinned))
        {
            throw new InvalidOperationException("Post changed before the owner action; staff review is required.");
        }
        RequestOptions request = Request(token);
        request.AuditLogReason = $"Recruitment owner action {actionId}";
        if (action == RecruitmentActionKind.Delete)
        {
            await thread.DeleteAsync(request);
            return;
        }
        if (action == RecruitmentActionKind.Reopen)
        {
            var forum = await GetForumAsync(thread.ParentChannelId, token);
            ulong[] closedIds = forum.Tags.Where(tag => string.Equals(tag.Name.Trim(), "Closed", StringComparison.OrdinalIgnoreCase))
                .Select(tag => tag.Id).ToArray();
            await thread.ModifyAsync(properties =>
            {
                properties.AppliedTags = thread.AppliedTags.Where(id => id != closedTagId && !closedIds.Contains(id)).ToArray();
                properties.Locked = false;
                properties.Archived = false;
            }, request);
            return;
        }
        if (action != RecruitmentActionKind.LockArchive) throw new InvalidOperationException("Unsupported owner action.");
        // Closure must succeed even when optional decoration cannot fit or the saved tag was removed.
        await thread.ModifyAsync(properties => { properties.Locked = true; properties.Archived = true; }, request);
        try { await DecorateClosedAsync(thread, closedTagId, request, token); }
        catch (Exception) when (!token.IsCancellationRequested) { /* Read-back reports missing decoration to staff. */ }
    }

    private async Task DecorateClosedAsync(RestThreadChannel thread, ulong? closedTagId, RequestOptions request, CancellationToken token)
    {
        var forum = await GetForumAsync(thread.ParentChannelId, token);
        if (!forum.Tags.Any(tag => tag.Id == closedTagId && !tag.Moderated &&
                string.Equals(tag.Name.Trim(), "Closed", StringComparison.OrdinalIgnoreCase))) return;
        ulong[] tags = thread.AppliedTags.ToArray();
        if (closedTagId is { } closed && !tags.Contains(closed) && tags.Length < 5)
        {
            tags = tags.Append(closed).ToArray();
        }
        await thread.ModifyAsync(properties =>
        {
            properties.AppliedTags = tags;
        }, request);
    }

    private bool Owned(IMessage message, string marker) => message.Author.Id == client.CurrentUser.Id &&
        message.Embeds.Any(embed => embed.Footer?.Text == marker);

    private static RecruitmentForumSetup Snapshot(IForumChannel forum) => new(forum.Id, forum.Topic ?? "",
        forum.Tags.Select(tag => new RecruitmentForumTag(tag.Id, tag.Name, tag.IsModerated, (tag.Emoji as Emote)?.Id, (tag.Emoji as Emoji)?.Name)).ToArray());
}
