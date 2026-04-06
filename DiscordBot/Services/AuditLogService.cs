using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class AuditLogService
{
    private readonly ILoggingService _loggingService;

    private const int MaxMessageLength = 800;
    private static readonly Color DeletedMessageColor = new(200, 128, 128);
    private static readonly Color EditedMessageColor = new(255, 255, 128);

    private readonly IMessageChannel _botAnnouncementChannel;

    public AuditLogService(DiscordSocketClient client, BotSettings settings, ILoggingService loggingService)
    {
        _loggingService = loggingService;

        client.MessageDeleted += EventGuard.Guarded<Cacheable<IMessage, ulong>, Cacheable<IMessageChannel, ulong>>(MessageDeleted, nameof(MessageDeleted));
        client.MessageUpdated += EventGuard.Guarded<Cacheable<IMessage, ulong>, SocketMessage, ISocketMessageChannel>(MessageUpdated, nameof(MessageUpdated));

        if (settings.BotAnnouncementChannel != null)
            _botAnnouncementChannel = client.GetChannel(settings.BotAnnouncementChannel.Id) as IMessageChannel;
    }

    private async Task MessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        if (message.HasValue == false)
        {
            await _loggingService.LogChannelAndFile($"An uncached Message snowflake:`{message.Id}` was deleted from channel <#{(await channel.GetOrDownloadAsync()).Id}>");
            return;
        }

        if (message.Value.Author.IsBot || channel.Id == _botAnnouncementChannel?.Id)
            return;

        var guildUser = message.Value.Author as SocketGuildUser;
        if (guildUser == null)
            return;

        var content = message.Value.Content;
        if (content.Length > MaxMessageLength)
            content = content[..MaxMessageLength];

        var user = message.Value.Author;
        var builder = new EmbedBuilder()
            .WithColor(DeletedMessageColor)
            .WithTimestamp(message.Value.Timestamp)
            .FooterInChannel(message.Value.Channel)
            .AddAuthorWithAction(user, "Deleted a message", true)
            .AddField($"Deleted Message {(content.Length != message.Value.Content.Length ? "(truncated)" : "")}",
                content);
        var embed = builder.Build();

        await _loggingService.Log(LogBehaviour.Channel, string.Empty, ExtendedLogSeverity.Info, embed);
    }

    private async Task MessageUpdated(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        if (after.Author.IsBot || channel.Id == _botAnnouncementChannel?.Id)
            return;

        bool isCached = true;
        string content = "";
        var beforeMessage = await before.GetOrDownloadAsync();
        if (beforeMessage == null || beforeMessage.Content == after.Content)
            isCached = false;
        else
            content = beforeMessage.Content;

        if (content == after.Content)
            return;
        if (content.Length == 0 && beforeMessage.Attachments.Count == 0)
            return;

        bool isTruncated = false;
        if (content.Length > MaxMessageLength)
        {
            content = content[..MaxMessageLength];
            isTruncated = true;
        }

        var user = after.Author;
        var builder = new EmbedBuilder()
            .WithColor(EditedMessageColor)
            .WithTimestamp(after.Timestamp)
            .FooterInChannel(after.Channel)
            .AddAuthorWithAction(user, "Updated a message", true);
        if (isCached)
        {
            builder.AddField($"Previous message content {(isTruncated ? "(truncated)" : "")}", content);
            if (beforeMessage.Attachments.Count > 0)
            {
                var attachments = beforeMessage.Attachments.Where(x => after.Attachments.All(y => y.Url != x.Url));
                var removedAttachments = attachments.ToList();
                if (removedAttachments.Any())
                {
                    var attachmentString = string.Join("\n", removedAttachments.Select(x => $"[{x.Filename}]({x.Url})"));
                    builder.AddField($"Previous attachments ({removedAttachments.Count()})", attachmentString);
                }
            }
        }

        builder.WithDescription($"Message: [{after.Id}]({after.GetJumpUrl()})");
        var embed = builder.Build();

        await _loggingService.Log(LogBehaviour.Channel, string.Empty, ExtendedLogSeverity.Info, embed);
    }
}
