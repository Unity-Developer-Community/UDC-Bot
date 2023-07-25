using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class ModerationService
{
    private readonly ILoggingService _loggingService;
    private readonly BotSettings _settings;

    public ModerationService(DiscordSocketClient client, BotSettings settings, ILoggingService loggingService)
    {
        _settings = settings;
        _loggingService = loggingService;

        client.MessageDeleted += MessageDeleted;
    }

    private async Task MessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        if (message.Value.Author.IsBot || channel.Id == _settings.BotAnnouncementChannel.Id)
            return;

        var content = message.Value.Content;
        if (content.Length > 800)
            content = content.Substring(0, 800);

        var user = message.Value.Author;
        var builder = new EmbedBuilder()
            .WithColor(new Color(200, 128, 128))
            .WithTimestamp(message.Value.Timestamp)
            .WithFooter(footer =>
            {
                footer
                    .WithText($"In channel {message.Value.Channel.Name}");
            })
            .WithAuthor(author =>
            {
                author
                    .WithName($"{user.Username}");
            })
            .AddField("Deleted message", content);
        var embed = builder.Build();
        
        await _loggingService.LogAction(
            $"User {user.UserNameReference()} has " +
            $"deleted the message\n{content}\n from channel #{(await channel.GetOrDownloadAsync()).Name}", true, false);
        await _loggingService.LogAction(" ", false, true, embed);
    }
}