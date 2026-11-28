namespace DiscordBot.Services.Moderation;

/// <summary>One earlier message of the member, reduced to what the alert shows.</summary>
public sealed record HistoryMessage(
    ulong ChannelId,
    ulong MessageId,
    DateTimeOffset At,
    string Content,
    int AttachmentCount = 0,
    int EmbedCount = 0,
    int StickerCount = 0);

/// <summary>
/// The outcome of the message history lookup: the approximate total and the newest messages, or the reason it
/// failed. Nothing is stored; the lookup runs once when the guard triggers.
/// </summary>
public sealed record HistorySearch(int Total, IReadOnlyList<HistoryMessage> Messages, string? UnavailableReason = null)
{
    public static HistorySearch Unavailable(string reason) => new(0, [], reason);
}

/// <summary>Turns a <see cref="HistorySearch"/> into the lines shown on the AutoMod alert.</summary>
public static class ImageSpamHistory
{
    /// <summary>How many recent messages are quoted.</summary>
    public const int MaxExcerpts = 4;
    /// <summary>How far back a message still counts as recent.</summary>
    public const int RecentDays = 365;
    public const int MaxExcerptLength = 80;
    private const int MaxLineLength = 280;

    public static IReadOnlyList<string> Lines(HistorySearch search, DateTimeOffset now, ulong guildId)
    {
        if (search.UnavailableReason != null)
            return [$"**Message history**\nHistory unavailable: {search.UnavailableReason}"];

        var lines = new List<string> { "**Message history**" };
        var newest = search.Messages.OrderByDescending(m => m.At).ToList();
        if (search.Total <= 0 && newest.Count == 0)
        {
            lines.Add("No earlier messages");
            return lines;
        }

        var total = Math.Max(search.Total, newest.Count);
        lines.Add($"~{total} earlier message{(total == 1 ? "" : "s")}");
        if (newest.Count > 0)
            lines.Add($"Last post: {Relative(newest[0].At)}");

        var cutoff = now.AddDays(-RecentDays);
        var recent = newest.Where(m => m.At >= cutoff).Take(MaxExcerpts).ToList();
        if (recent.Count == 0)
        {
            lines.Add("Nothing in the last 12 months");
            return lines;
        }
        lines.AddRange(recent.Select(m => Excerpt(m, guildId)));
        return lines;
    }

    private static string Relative(DateTimeOffset time) => $"<t:{time.ToUnixTimeSeconds()}:R>";

    private static string Excerpt(HistoryMessage message, ulong guildId)
    {
        var line = $"- <#{message.ChannelId}> · {Relative(message.At)} · {Preview(message)}";
        var jump = $" · [jump](https://discord.com/channels/{guildId}/{message.ChannelId}/{message.MessageId})";
        return line.Length + jump.Length <= MaxLineLength ? line + jump : line;
    }

    private static string Preview(HistoryMessage message)
    {
        var text = string.Join(' ', message.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (text.Length > 0)
            return Format.Sanitize(AuditCard.Fit(text, MaxExcerptLength, "…"));
        if (message.AttachmentCount > 0)
            return $"[{message.AttachmentCount} attachment{(message.AttachmentCount == 1 ? "" : "s")}]";
        if (message.EmbedCount > 0)
            return "[embed]";
        return message.StickerCount > 0 ? "[sticker]" : "[no text]";
    }
}
