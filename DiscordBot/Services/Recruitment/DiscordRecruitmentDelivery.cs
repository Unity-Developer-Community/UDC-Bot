using Discord.Net;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Services.Recruitment;

public sealed partial class DiscordRecruitmentAdapter
{
    // One predicate covers stored IDs, interrupted-send discovery and advisory deletion.
    internal static bool OwnsMessage(IUserMessage message, ulong botId, ulong channelId, string marker, RecruitmentMessageFormat format) =>
        message.Author.Id == botId && message.Channel.Id == channelId &&
        (format == RecruitmentMessageFormat.ComponentsV2
            ? (message.Flags & MessageFlags.ComponentsV2) != 0 && TextDisplays(message.Components).Any(t => t == $"||{marker}||")
            : message.Content.Split('\n').Any(t => t == $"||{marker}||" || t == $"-# {marker}"));

    private static IEnumerable<string> TextDisplays(IEnumerable<IMessageComponent> components)
    {
        foreach (var component in components)
        {
            if (component is TextDisplayComponent text) yield return text.Content;
            if (component is ContainerComponent container)
                foreach (var value in TextDisplays(container.Components)) yield return value;
            if (component is SectionComponent section)
                foreach (var value in TextDisplays(section.Components)) yield return value;
        }
    }

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

    internal static async Task<Delivery> UpsertV2Async(IMessageChannel channel, ulong botId, string? messageId,
        bool recovering, DateTimeOffset since, RecruitmentMessage message, bool mediaAvailable, Action beforeWrite, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var observation = RecruitmentMessageTransport.Observe(channel.Id);
        IUserMessage? existing = messageId is null ? null : await channel.GetMessageAsync(ulong.Parse(messageId), CacheMode.AllowDownload, Options(ct)) as IUserMessage;
        if (existing is not null && !OwnsMessage(existing, botId, channel.Id, message.Marker, RecruitmentMessageFormat.ComponentsV2))
            throw new InvalidOperationException("Stored recruitment message identity does not match.");
        if (existing is null && recovering)
        {
            ulong? before = null;
            var covered = false;
            for (var page = 0; page < 20; page++)
            {
                var messages = (before.HasValue ? await channel.GetMessagesAsync(before.Value, Direction.Before, 100, CacheMode.AllowDownload, Options(ct)).FlattenAsync()
                    : await channel.GetMessagesAsync(100, CacheMode.AllowDownload, Options(ct)).FlattenAsync()).ToArray();
                existing = messages.OfType<IUserMessage>().FirstOrDefault(m => OwnsMessage(m, botId, channel.Id, message.Marker, RecruitmentMessageFormat.ComponentsV2));
                if (existing is not null) break;
                if (messages.Length < 100 || messages.Min(m => m.Timestamp) <= since.AddMinutes(-5)) { covered = true; break; }
                before = messages.Min(m => m.Id);
            }
            if (existing is null && !covered) throw new InvalidOperationException("Message recovery history exceeds the scan bound; staff review required before resending.");
        }
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
            return await UpsertV2Async(channel, botId, existing?.Id.ToString(), recovering, since,
                message with { Media = null, Components = message.FallbackComponents }, false, beforeWrite, ct);
        }
        if (message.RestoreArchive && (needsRestore || edit) && channel is IThreadChannel restore)
            await restore.ModifyAsync(p => { p.Archived = true; p.Locked = message.RestoreLocked; }, Options(ct));
        var attachment = media is null ? null : existing.Attachments.SingleOrDefault(a => a.Filename == media.FileName);
        return new Delivery(existing.Id.ToString(), newlySent, attachment?.Id.ToString(), attachment?.Filename);
    }
}
