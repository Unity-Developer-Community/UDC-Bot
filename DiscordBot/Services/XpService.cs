using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class XpService
{
    private readonly DatabaseService _databaseService;
    private readonly ILoggingService _loggingService;

    private readonly Dictionary<ulong, DateTime> _xpCooldown;
    private readonly List<ulong> _noXpChannels;
    private readonly Random _rand;

    private readonly int _xpMinPerMessage;
    private readonly int _xpMaxPerMessage;
    private readonly int _xpMinCooldown;
    private readonly int _xpMaxCooldown;

    public XpService(DiscordSocketClient client, DatabaseService databaseService, ILoggingService loggingService,
        BotSettings settings, UserSettings userSettings)
    {
        _databaseService = databaseService;
        _loggingService = loggingService;
        _rand = new Random();
        _xpCooldown = new Dictionary<ulong, DateTime>();

        _xpMinPerMessage = userSettings.XpMinPerMessage;
        _xpMaxPerMessage = userSettings.XpMaxPerMessage;
        _xpMinCooldown = userSettings.XpMinCooldown;
        _xpMaxCooldown = userSettings.XpMaxCooldown;

        _noXpChannels = new List<ulong> { settings.BotCommandsChannel.Id };

        client.MessageReceived += EventGuard.Guarded<SocketMessage>(UpdateXp, nameof(UpdateXp));
    }

    private async Task UpdateXp(SocketMessage messageParam)
    {
        if (messageParam.Author.IsBot)
            return;

        if (_noXpChannels.Contains(messageParam.Channel.Id))
            return;

        var userId = messageParam.Author.Id;
        if (_xpCooldown.HasUser(userId))
            return;

        var waitTime = _rand.Next(_xpMinCooldown, _xpMaxCooldown);
        float baseXp = _rand.Next(_xpMinPerMessage, _xpMaxPerMessage);
        float bonusXp = 0;

        _xpCooldown.AddCooldown(userId, waitTime);
        Task.Run(async () =>
        {
            var user = await _databaseService.GetOrAddUser((SocketGuildUser)messageParam.Author);
            if (user == null)
                return;

            bonusXp += baseXp * (1f + user.Karma / 100f);

            if (((IGuildUser)messageParam.Author).RoleIds.Count < 2)
                baseXp *= .9f;

            var reduceXp = 1f;
            if (user.Karma < user.Level) reduceXp = 1 - Math.Min(.9f, (user.Level - user.Karma) * .05f);

            var xpGain = (int)Math.Round((baseXp + bonusXp) * reduceXp);

            await _databaseService.Query.UpdateXp(userId.ToString(), user.Exp + (long)xpGain);

            _loggingService.LogXp(messageParam.Channel.Name, messageParam.Author.Username, baseXp, bonusXp, reduceXp,
                xpGain);

            await LevelUp(messageParam, userId);
        });
    }

    private async Task LevelUp(SocketMessage messageParam, ulong userId)
    {
        var level = await _databaseService.Query.GetLevel(userId.ToString());
        var xp = await _databaseService.Query.GetXp(userId.ToString());

        var xpHigh = GetXpHigh(level);

        if (xp < xpHigh)
            return;

        await _databaseService.Query.UpdateLevel(userId.ToString(), level + 1);

        if (level <= 3)
            return;

        var msg = messageParam.Author.GetUserPreferredName().ToBold() + " has leveled up!";
        await messageParam.Channel.SendMessageAsync(msg).DeleteAfterTime(60);
    }

    public double GetXpLow(int level) => 70d - 139.5d * (level + 1d) + 69.5 * Math.Pow(level + 1d, 2d);

    public double GetXpHigh(int level) => 70d - 139.5d * (level + 2d) + 69.5 * Math.Pow(level + 2d, 2d);
}
