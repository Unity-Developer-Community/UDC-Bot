using System.Text.RegularExpressions;
using Discord.WebSocket;
using DiscordBot.Data;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class MikuService
{
    private readonly BotSettings _settings;

    private DateTime _mikuMentioned;
    private readonly TimeSpan _mikuCooldownTime;
    private readonly string _mikuRegex;
    private readonly string _mikuReply;

    public MikuService(DiscordSocketClient client, BotSettings settings)
    {
        _settings = settings;

        _mikuCooldownTime = new TimeSpan(0, 39, 0); // 39min
        _mikuMentioned = DateTime.Now - _mikuCooldownTime;
        _mikuRegex = @"(?i)\b(miku|hatsune|初音ミク|初音|ミク)\b";
        _mikuReply =
            "(:three: :nine:|:microphone:|:notes:|:musical_note:|:musical_keyboard:|:mirror_ball:) " +
            "(Oi, mite, mite,|Heya,|Hey, look,|Did someone mention Miku?) " +
            "<@358915848515354626> (-chan|)!";

        // Subscription commented out — enable when ready
        //_client.MessageReceived += EventGuard.Guarded<SocketMessage>(MikuCheck, nameof(MikuCheck));
    }

    public async Task MikuCheck(SocketMessage messageParam)
    {
        var channel = (SocketGuildChannel)messageParam.Channel;
        var guildId = channel.Guild.Id;

        if (guildId != _settings.GuildId) return;

        if (messageParam.Author.IsBot)
            return;

        var now = DateTime.Now;
        if ((DateTime.Now - _mikuMentioned) < _mikuCooldownTime)
            return;

        var match = Regex.Match(messageParam.Content, _mikuRegex);
        if (!match.Success)
            return;

        _mikuMentioned = now;
        var reply = FuzzTable.Evaluate(_mikuReply);
        await messageParam.Channel.SendMessageAsync(reply);
    }
}
