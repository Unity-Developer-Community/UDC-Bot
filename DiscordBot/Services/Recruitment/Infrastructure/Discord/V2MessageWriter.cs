using static DiscordBot.Services.Recruitment.RecruitmentRequests;
using Discord.Net;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Services.Recruitment.Infrastructure.Discord;

internal static class V2MessageWriter
{
    // Serialize the complete recursive builder model, dropping only Discord-assigned IDs and
    // resolved CDN metadata. Attachment URLs and avatar CDN signatures have a stable identity.
    internal static string V2ComponentKey(IEnumerable<IMessageComponent> components)
    {
        var value = JToken.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(components.ToArray()));
        Normalize(value);
        return value.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static void Normalize(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToArray())
            {
                var field = property.Value;
                if (property.Name is "Id" or "id" or "ProxyUrl" or "proxy_url" or "Height" or "height" or "Width" or "width" or "LoadingState" or "loading_state" or "ContentType" or "content_type") property.Remove();
                else if (field is JValue { Value: string text })
                {
                    if (text.StartsWith("attachment://", StringComparison.Ordinal)) property.Value = System.IO.Path.GetFileName(text);
                    else if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                        (uri.Host == "cdn.discordapp.com" || uri.Host == "media.discordapp.net"))
                        property.Value = uri.AbsolutePath.Contains("/attachments/", StringComparison.Ordinal)
                            ? System.IO.Path.GetFileName(uri.AbsolutePath) : uri.GetLeftPart(UriPartial.Path);
                }
                else Normalize(field);
            }
        }
        else if (token is JArray array) foreach (var item in array) Normalize(item);
    }

    internal static async Task<Delivery> UpsertAsync(IMessageChannel channel, ulong botId, string? messageId,
        bool recovering, DateTimeOffset since, RecruitmentMessage message, bool mediaAvailable, Action beforeWrite, CancellationToken ct)
    {
        using var requestScope = RecruitmentRequests.BeginScope();
        ct.ThrowIfCancellationRequested();
        using var observation = RecruitmentMessageTransport.Observe(channel.Id);
        var existing = (await OwnedMessageLookup.FindAsync(channel, botId, messageId, recovering, since,
            message.Marker, RecruitmentMessageFormat.ComponentsV2, ct)).RequireCovered();
        var media = mediaAvailable ? message.Media : null;
        var components = media is null ? message.FallbackComponents ?? message.Components! : message.Components!;
        var retained = media is null ? null : existing?.Attachments.SingleOrDefault(a => a.Filename == media.FileName);
        var replace = media is not null && (retained is null || existing!.Attachments.Count != 1);
        var edit = existing is null || V2ComponentKey(existing.Components) != V2ComponentKey(components.Components) ||
            replace || media is null && existing.Attachments.Count > 0;
        if (edit) beforeWrite();
        var needsRestore = message.RestoreArchive && channel is IThreadChannel observed &&
            (!observed.IsArchived || observed.IsLocked != message.RestoreLocked);
        if (!edit && needsRestore) beforeWrite();
        if (edit && channel is IThreadChannel { IsArchived: true } archived)
        {
            if (archived.IsLocked && !message.RestoreArchive) throw new InvalidOperationException("A locked listing requires moderator review.");
            await archived.ModifyAsync(p => p.Archived = false, Options(ct));
            needsRestore = message.RestoreArchive;
        }
        var newlySent = existing is null;
        using var stream = media is null || !replace && !newlySent ? null : new System.IO.MemoryStream(media.Bytes, writable: false);
        try
        {
            using var publication = RecruitmentMessageTransport.Publish(channel.Id, existing?.Id, retained is null ? Array.Empty<IAttachment>() : new[] { retained });
            if (existing is null)
            {
                existing = media is null
                    ? await channel.SendMessageAsync(components: components, flags: MessageFlags.ComponentsV2, allowedMentions: AllowedMentions.None, options: Options(ct))
                    : await channel.SendFilesAsync(new[] { new FileAttachment(stream!, media.FileName, media.Description) }, components: components,
                        flags: MessageFlags.ComponentsV2, allowedMentions: AllowedMentions.None, options: Options(ct));
            }
            else if (edit)
            {
                // FileAttachment represents uploads only in 3.20.1. Retain by the actual returned snowflake
                // through the narrowly scoped transport correction; never treat upload index 0 as an ID.
                await existing.ModifyAsync(p =>
                {
                    p.Components = components; p.Flags = MessageFlags.ComponentsV2; p.AllowedMentions = AllowedMentions.None;
                    if (replace) p.Attachments = new[] { new FileAttachment(stream!, media!.FileName, media.Description) };
                }, Options(ct));
            }
        }
        catch (HttpException ex) when (media is not null && ex.HttpCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest)
        {
            // A definite rejected media write is safe to retry as text. Ambiguous responses and
            // cancellation propagate with the saved publication/restoration intent untouched.
            return await UpsertAsync(channel, botId, existing?.Id.ToString(), recovering, since,
                message with { Media = null, Components = message.FallbackComponents }, false, beforeWrite, ct);
        }
        if (message.RestoreArchive && (needsRestore || edit) && channel is IThreadChannel restore)
            await restore.ModifyAsync(p => { p.Archived = true; p.Locked = message.RestoreLocked; }, Options(ct));
        var attachment = media is null ? null : existing.Attachments.SingleOrDefault(a => a.Filename == media.FileName);
        return new Delivery(existing.Id.ToString(), newlySent, attachment?.Id.ToString(), attachment?.Filename);
    }
}
