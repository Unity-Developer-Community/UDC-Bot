using static DiscordBot.Services.Recruitment.RecruitmentRequests;

namespace DiscordBot.Services.Recruitment.Infrastructure.Discord;

internal enum MessageLookupStatus { Found, Absent, Incomplete }
internal sealed record MessageLookup(MessageLookupStatus Status, IUserMessage? Message)
{
    public IUserMessage? RequireCovered() => Status == MessageLookupStatus.Incomplete
        ? throw new InvalidOperationException("Message recovery history exceeds the scan bound; staff review required before resending.") : Message;
}

internal static class OwnedMessageLookup
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

    internal static async Task<MessageLookup> FindAsync(IMessageChannel channel, ulong botId, string? messageId,
        bool recovering, DateTimeOffset since, string marker, RecruitmentMessageFormat format, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var existing = messageId is null ? null : await channel.GetMessageAsync(ulong.Parse(messageId), CacheMode.AllowDownload, Options(ct)) as IUserMessage;
        if (existing is not null)
        {
            if (!OwnsMessage(existing, botId, channel.Id, marker, format))
                throw new InvalidOperationException("Stored recruitment message identity does not match.");
            return new(MessageLookupStatus.Found, existing);
        }
        if (!recovering) return new(MessageLookupStatus.Absent, null);
        ulong? before = null;
        for (var page = 0; page < 20; page++)
        {
            ct.ThrowIfCancellationRequested();
            var messages = (before.HasValue
                ? await channel.GetMessagesAsync(before.Value, Direction.Before, 100, CacheMode.AllowDownload, Options(ct)).FlattenAsync()
                : await channel.GetMessagesAsync(100, CacheMode.AllowDownload, Options(ct)).FlattenAsync()).ToArray();
            existing = messages.OfType<IUserMessage>().FirstOrDefault(m => OwnsMessage(m, botId, channel.Id, marker, format));
            if (existing is not null) return new(MessageLookupStatus.Found, existing);
            if (messages.Length < 100 || messages.Min(m => m.Timestamp) <= since.AddMinutes(-5))
                return new(MessageLookupStatus.Absent, null);
            before = messages.Min(m => m.Id);
        }
        return new(MessageLookupStatus.Incomplete, null);
    }
}
