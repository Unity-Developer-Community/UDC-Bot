using static DiscordBot.Services.Recruitment.RecruitmentRequests;

namespace DiscordBot.Services.Recruitment.Infrastructure.Discord;

internal static class StandardMessageWriter
{
    internal static async Task<Delivery> UpsertAsync(IMessageChannel channel, ulong botUserId, string? messageId,
        bool recovering, DateTimeOffset since, RecruitmentMessage message, Action beforeWrite, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        var channelId = channel.Id.ToString();
        var existing = (await OwnedMessageLookup.FindAsync(channel, botUserId, messageId, recovering, since,
            message.Marker, message.Format, ct)).RequireCovered();
        var builder = new ComponentBuilder();
        if (message.AcknowledgementRequired) builder.WithButton("Acknowledge", $"recruit:ack:{channelId}", ButtonStyle.Primary);
        if (message.ReaderControls) builder.WithButton("Legend", $"recruit:details:{channelId}", ButtonStyle.Secondary)
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
                await existing.ModifyAsync(p => { p.Content = message.Content; p.Components = components; p.Attachments = Array.Empty<FileAttachment>(); p.Embeds = message.Embed is null ? Array.Empty<Embed>() : new[] { message.Embed }; p.AllowedMentions = new AllowedMentions(AllowedMentionTypes.None); }, Options(ct));
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
        // Only a new, explicit author notice may ping. Recovery edits always suppress mentions above.
        var mentions = message.MentionUserId is { } author
            ? new AllowedMentions(AllowedMentionTypes.None) { UserIds = new List<ulong> { author } } : AllowedMentions.None;
        var sent = await channel.SendMessageAsync(message.Content, options: Options(ct), allowedMentions: mentions, components: components, embed: message.Embed);
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

}
