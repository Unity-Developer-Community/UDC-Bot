using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class ModerationService
{
    private readonly ILoggingService _loggingService;
    private readonly BotSettings _settings;
    
    private const int MaxMessageLength = 800;
    private static readonly Color DeletedMessageColor = new (200, 128, 128);
    private static readonly Color EditedMessageColor = new (255, 255, 128);

    public ModerationService(DiscordSocketClient client, BotSettings settings, ILoggingService loggingService)
    {
        _settings = settings;
        _loggingService = loggingService;

        client.MessageDeleted += MessageDeleted;
        client.MessageUpdated += MessageUpdated;
    }

    private async Task MessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        if (message.HasValue == false)
        {
            await _loggingService.LogChannelAndFile($"An uncached Message snowflake:`{message.Id}` was deleted from channel <#{(await channel.GetOrDownloadAsync()).Id}>");
            return;
        }
        
        if (message.Value.Author.IsBot || channel.Id == _settings.BotAnnouncementChannel.Id)
            return;
        // Check the author is even in the guild
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
            .WithFooter($"In channel {message.Value.Channel.Name}")
            .WithAuthor($"{user.GetPreferredAndUsername()} deleted a message")
            .AddField($"Deleted Message {(content.Length != message.Value.Content.Length ? "(truncated)" : "")}",
                content);
        var embed = builder.Build();

        // TimeStamp for the Footer

        
        await _loggingService.Log(LogBehaviour.Channel, string.Empty, ExtendedLogSeverity.Info, embed);
    }

    private async Task MessageUpdated(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        if (after.Author.IsBot || channel.Id == _settings.BotAnnouncementChannel.Id)
            return;

        bool isCached = true;
        string content = "";
        var beforeMessage = await before.GetOrDownloadAsync();
        if (beforeMessage == null || beforeMessage.Content == after.Content)
            isCached = false;
        else
            content = beforeMessage.Content;

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
            .WithFooter($"In channel {after.Channel.Name}")
            .WithAuthor($"{user.GetPreferredAndUsername()} updated a message");
        if (isCached)
            builder.AddField($"Previous message content {(isTruncated ? "(truncated)" : "")}", content);
        builder.WithDescription($"Message: [{after.Id}]({after.GetJumpUrl()})");
            var embed = builder.Build();
        
        // TimeStamp for the Footer
        
        await _loggingService.Log(LogBehaviour.Channel,string.Empty, ExtendedLogSeverity.Info, embed);
    }
}