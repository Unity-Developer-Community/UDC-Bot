using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class EveryoneScoldService
{
    private readonly BotSettings _settings;
    private readonly Dictionary<ulong, DateTime> _everyoneScoldCooldown = new();

    public EveryoneScoldService(DiscordSocketClient client, BotSettings settings)
    {
        _settings = settings;
        client.MessageReceived += EventGuard.Guarded<SocketMessage>(ScoldForAtEveryoneUsage, nameof(ScoldForAtEveryoneUsage));
    }

    private async Task ScoldForAtEveryoneUsage(SocketMessage messageParam)
    {
        if (messageParam.Author.IsBot || ((IGuildUser)messageParam.Author).GuildPermissions.MentionEveryone)
            return;
        var content = messageParam.Content;
        if (content.Contains("@everyone") || content.Contains("@here"))
        {
            if (_everyoneScoldCooldown.ContainsKey(messageParam.Author.Id) &&
                _everyoneScoldCooldown[messageParam.Author.Id] > DateTime.Now)
                return;
            _everyoneScoldCooldown[messageParam.Author.Id] =
                DateTime.Now.AddSeconds(_settings.EveryoneScoldPeriodSeconds);

            await (messageParam.Channel.SendMessageAsync(
                    $"Please don't try to alert **everyone** on the server, {messageParam.Author.Mention}!\n" +
                    "If you are asking a question, people will help you when they have time.")
                .DeleteAfterTime(minutes: 2) ?? Task.CompletedTask);
        }
    }
}
