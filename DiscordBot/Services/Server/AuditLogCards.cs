using Discord.WebSocket;

namespace DiscordBot.Services.Server;

/// <summary>Plain identity shown in a card header.</summary>
public sealed record CardUser(ulong Id, string Name, string AvatarUrl, string? Note = null)
{
    public static CardUser From(IUser user, string? note = null) =>
        new(user.Id, user.GetPreferredAndUsername(), user.GetDisplayAvatarUrl(size: 256), note);
}

/// <summary>Plain channel info shown in a card header. <see cref="ParentName"/> is set for threads.</summary>
public sealed record CardChannel(ulong Id, string? Name, string? ParentName = null, string? Kind = null)
{
    public static CardChannel From(IChannel channel)
    {
        if (channel is SocketThreadChannel thread)
            return new CardChannel(thread.Id, thread.Name, thread.ParentChannel?.Name,
                thread.ParentChannel is SocketForumChannel ? "forum post" : "thread");
        return new CardChannel(channel.Id, channel.Name);
    }
}

public sealed record CardAttachment(string Filename, int Size);

/// <summary>
/// Builds one Components V2 container and keeps the text of the whole message under Discord's
/// 4000 character limit. Header and footer are always kept; body parts are cut to fit, in order.
/// </summary>
public sealed class AuditCard
{
    public const int DiscordTextLimit = 4000;
    // Headroom, since Discord may count some characters differently from .NET string length.
    public const int DefaultBudget = 3900;
    public const string TruncatedMarker = "\n-# …(truncated)";
    private const int MaxHeaderLength = 1000;
    private const int MaxFooterLength = 500;
    private const int MaxGalleryItems = 10;

    private abstract record Part;
    private sealed record TextPart(string Text, int MaxLength) : Part;
    private sealed record LinesPart(IReadOnlyList<string> Lines, int MaxLength) : Part;
    private sealed record SeparatorPart : Part;
    private sealed record MediaPart(IReadOnlyList<string> Urls) : Part;

    private readonly Color _accent;
    private string? _header;
    private CardUser? _user;
    private string? _footer;
    private readonly List<Part> _parts = [];

    public AuditCard(Color accent) => _accent = accent;

    /// <summary>Title, identity and channel lines. With a user, the avatar is shown as a thumbnail.</summary>
    public AuditCard WithHeader(string title, CardUser? user, CardChannel? channel, params string[] extraLines)
    {
        _user = user;
        var lines = new List<string> { $"### {title}" };
        if (user != null)
        {
            lines.Add($"<@{user.Id}> {Format.Sanitize(user.Name)}");
            lines.Add(user.Note == null ? $"User ID: `{user.Id}`" : $"User ID: `{user.Id}` · {user.Note}");
        }
        if (channel != null)
            lines.Add($"In {ChannelText(channel)}");
        lines.AddRange(extraLines.Where(l => !string.IsNullOrWhiteSpace(l)));
        _header = string.Join('\n', lines);
        return this;
    }

    public AuditCard AddText(string text, int maxLength = int.MaxValue)
    {
        if (!string.IsNullOrWhiteSpace(text))
            _parts.Add(new TextPart(text, maxLength));
        return this;
    }

    /// <summary>Adds whole lines while there is room, then a note saying how many lines were left out.</summary>
    public AuditCard AddLines(IEnumerable<string> lines, int maxLength = int.MaxValue)
    {
        var list = lines.Where(l => !string.IsNullOrEmpty(l)).ToList();
        if (list.Count > 0)
            _parts.Add(new LinesPart(list, maxLength));
        return this;
    }

    public AuditCard AddSeparator()
    {
        _parts.Add(new SeparatorPart());
        return this;
    }

    /// <summary>Image gallery (max 10). URLs can be <c>attachment://name</c> for files sent with the message.</summary>
    public AuditCard AddMedia(IEnumerable<string> urls)
    {
        var list = urls.Take(MaxGalleryItems).ToList();
        if (list.Count > 0)
            _parts.Add(new MediaPart(list));
        return this;
    }

    public AuditCard WithFooter(string text)
    {
        _footer = string.IsNullOrWhiteSpace(text) ? null : $"-# {text}";
        return this;
    }

    public MessageComponent Build(int budget = DefaultBudget)
    {
        var header = Fit(_header ?? "### Audit log", MaxHeaderLength);
        var footer = _footer == null ? null : Fit(_footer, MaxFooterLength);
        var remaining = budget - header.Length - (footer?.Length ?? 0);

        var container = new ContainerBuilder().WithAccentColor(_accent);
        if (_user != null)
            container.WithSection(new SectionBuilder()
                .AddComponent(new TextDisplayBuilder(header))
                .WithAccessory(new ThumbnailBuilder(new UnfurledMediaItemProperties(_user.AvatarUrl))));
        else
            container.WithTextDisplay(header);

        foreach (var part in _parts)
        {
            switch (part)
            {
                case TextPart text:
                {
                    var fitted = Fit(text.Text, Math.Min(text.MaxLength, remaining));
                    if (fitted.Length == 0) continue;
                    remaining -= fitted.Length;
                    container.WithTextDisplay(fitted);
                    break;
                }
                case LinesPart lines:
                {
                    var fitted = FitLines(lines.Lines, Math.Min(lines.MaxLength, remaining));
                    if (fitted.Length == 0) continue;
                    remaining -= fitted.Length;
                    container.WithTextDisplay(fitted);
                    break;
                }
                case SeparatorPart:
                    container.WithSeparator();
                    break;
                case MediaPart media:
                    container.WithMediaGallery(media.Urls);
                    break;
            }
        }

        if (footer != null)
            container.WithTextDisplay(footer);

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Cuts <paramref name="text"/> to at most <paramref name="max"/> chars, ending with <paramref name="marker"/> when cut.</summary>
    public static string Fit(string text, int max, string marker = TruncatedMarker)
    {
        if (text.Length <= max)
            return text;
        if (max <= marker.Length)
            return string.Empty;
        var cut = max - marker.Length;
        if (char.IsHighSurrogate(text[cut - 1]))
            cut--;
        return text[..cut] + marker;
    }

    /// <summary>Joins whole lines up to <paramref name="max"/> chars, adding a note for lines left out.</summary>
    public static string FitLines(IReadOnlyList<string> lines, int max)
    {
        var all = string.Join('\n', lines);
        if (all.Length <= max)
            return all;

        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var separator = builder.Length == 0 ? 0 : 1;
            // Keep room for the note about the lines after this one.
            if (builder.Length + separator + lines[i].Length + OmittedNote(lines.Count - i - 1).Length <= max)
            {
                if (separator == 1) builder.Append('\n');
                builder.Append(lines[i]);
                continue;
            }

            if (builder.Length == 0)
            {
                // Not even the first line fits: show part of it.
                var note = OmittedNote(lines.Count - 1);
                return Fit(lines[0], max - note.Length, "…") + note;
            }
            var finalNote = OmittedNote(lines.Count - i);
            return builder.Length + finalNote.Length <= max ? builder.Append(finalNote).ToString() : builder.ToString();
        }
        return builder.ToString();
    }

    private static string OmittedNote(int count) => count == 0 ? string.Empty : $"\n-# …{count} more line{(count == 1 ? "" : "s")} not shown";

    public static string ChannelText(CardChannel channel)
    {
        var text = $"<#{channel.Id}>";
        if (channel.Name != null)
            text += $" (`#{channel.Name.Replace("`", "'")}`" +
                    (channel.ParentName != null ? $", {channel.Kind ?? "thread"} in `#{channel.ParentName.Replace("`", "'")}`" : "") + ")";
        return text;
    }
}

/// <summary>Card layouts for the audit log. Inputs are plain values so the layouts can be tested.</summary>
public static class AuditLogCards
{
    public static readonly Color DeletedColor = new(200, 128, 128);
    public static readonly Color EditedColor = new(255, 255, 128);
    public static readonly Color BulkDeletedColor = new(160, 96, 96);

    public const int MaxContentLength = 2000;
    private const int MaxBulkLineLength = 200;

    public static string Timestamp(DateTimeOffset time, char style = 'f') => $"<t:{time.ToUnixTimeSeconds()}:{style}>";

    /// <summary>Block-quotes every line. Empty text becomes an italic placeholder.</summary>
    public static string Quote(string text) =>
        string.IsNullOrWhiteSpace(text) ? "*(no text)*" : string.Join('\n', text.Split('\n').Select(l => $"> {l}"));

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };

    public static string AttachmentLines(string title, IReadOnlyList<CardAttachment> attachments) =>
        $"**{title} ({attachments.Count})**\n" +
        string.Join('\n', attachments.Select(a => $"- `{a.Filename.Replace("`", "'")}` ({FormatSize(a.Size)})"));

    public static string JumpUrl(ulong guildId, ulong channelId, ulong messageId) =>
        $"https://discord.com/channels/{guildId}/{channelId}/{messageId}";

    public sealed record DeletedMessage(
        CardUser Author,
        CardChannel Channel,
        ulong MessageId,
        DateTimeOffset SentAt,
        string Content,
        IReadOnlyList<CardAttachment> Attachments,
        IReadOnlyList<string> StickerNames,
        string? ReplyToUrl = null,
        string? ForwardedContent = null);

    public static MessageComponent DeletedMessageCard(DeletedMessage message, DateTimeOffset deletedAt)
    {
        var card = new AuditCard(DeletedColor)
            .WithHeader("🗑️ Message deleted", message.Author, message.Channel,
                message.ReplyToUrl != null ? $"Reply to {message.ReplyToUrl}" : "");

        if (message.ForwardedContent != null && string.IsNullOrWhiteSpace(message.Content))
            card.AddText("**Forwarded message**\n" + Quote(message.ForwardedContent), MaxContentLength);
        else
            card.AddText(Quote(message.Content), MaxContentLength);

        if (message.Attachments.Count > 0)
            card.AddText(AttachmentLines("Attachments", message.Attachments));
        if (message.StickerNames.Count > 0)
            card.AddText("**Stickers:** " + string.Join(", ", message.StickerNames.Select(Format.Sanitize)));

        return card
            .WithFooter($"Message ID: {message.MessageId} · Sent {Timestamp(message.SentAt)} · Deleted {Timestamp(deletedAt)}")
            .Build();
    }

    public static MessageComponent UncachedDeletedMessageCard(ulong messageId, CardChannel channel, DateTimeOffset deletedAt) =>
        new AuditCard(DeletedColor)
            .WithHeader("🗑️ Uncached message deleted", null, channel, "-# The bot did not have this message cached, so its author and content are unknown.")
            .WithFooter($"Message ID: {messageId} · Sent {Timestamp(SnowflakeUtils.FromSnowflake(messageId))} · Deleted {Timestamp(deletedAt)}")
            .Build();

    public sealed record BulkDeletedMessage(CardUser Author, string Content, int AttachmentCount);

    public static MessageComponent BulkDeletedCard(CardChannel channel, IReadOnlyList<BulkDeletedMessage> cached, int uncachedCount, DateTimeOffset deletedAt)
    {
        var total = cached.Count + uncachedCount;
        var summary = $"{total} message{(total == 1 ? "" : "s")} deleted at once" +
                      (uncachedCount > 0 ? $" ({uncachedCount} not cached, content unknown)" : "");

        var lines = new List<string>();
        foreach (var group in cached.GroupBy(m => m.Author.Id))
        {
            var author = group.First().Author;
            lines.Add($"**{Format.Sanitize(author.Name)}** <@{author.Id}> `{author.Id}` · {group.Count()} message{(group.Count() == 1 ? "" : "s")}");
            foreach (var msg in group)
            {
                var text = string.Join(' ', msg.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                if (msg.AttachmentCount > 0)
                    text += $" *(+{msg.AttachmentCount} attachment{(msg.AttachmentCount == 1 ? "" : "s")})*";
                if (string.IsNullOrWhiteSpace(text))
                    text = "*(no text)*";
                lines.Add("> " + AuditCard.Fit(text, MaxBulkLineLength, "…"));
            }
        }

        return new AuditCard(BulkDeletedColor)
            .WithHeader("🧹 Messages bulk deleted", null, channel, summary)
            .AddLines(lines)
            .WithFooter($"Deleted {Timestamp(deletedAt)} · Usually a moderator purge or a ban that removed message history")
            .Build();
    }
}
