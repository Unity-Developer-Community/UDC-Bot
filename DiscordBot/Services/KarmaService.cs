using System.Text;
using System.Text.RegularExpressions;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class KarmaService
{
    private readonly DatabaseService _databaseService;
    private readonly ILoggingService _loggingService;
    private readonly BotSettings _settings;

    private readonly HashSet<ulong> _canEditThanks;
    private readonly Dictionary<ulong, DateTime> _thanksCooldown;
    private readonly string _thanksRegex;
    private readonly int _thanksCooldownTime;
    private readonly int _thanksMinJoinTime;

    public KarmaService(DiscordSocketClient client, DatabaseService databaseService, ILoggingService loggingService,
        BotSettings settings, UserSettings userSettings)
    {
        _databaseService = databaseService;
        _loggingService = loggingService;
        _settings = settings;
        _canEditThanks = new HashSet<ulong>(32);
        _thanksCooldown = new Dictionary<ulong, DateTime>();

        var sbThanks = new StringBuilder();
        var thx = userSettings.Thanks;
        sbThanks.Append(@"(?i)(?<!\bno\s*)\b(");
        foreach (var t in thx)
            sbThanks.Append(t).Append('|');
        sbThanks.Length--;
        sbThanks.Append(@")\b");

        _thanksRegex = sbThanks.ToString();
        _thanksCooldownTime = userSettings.ThanksCooldown;
        _thanksMinJoinTime = userSettings.ThanksMinJoinTime;

        client.MessageReceived += EventGuard.Guarded<SocketMessage>(Thanks, nameof(Thanks));
        client.MessageUpdated += EventGuard.Guarded<Cacheable<IMessage, ulong>, SocketMessage, ISocketMessageChannel>(ThanksEdited, nameof(ThanksEdited));
    }

    private async Task ThanksEdited(Cacheable<IMessage, ulong> cachedMessage, SocketMessage messageParam,
        ISocketMessageChannel socketMessageChannel)
    {
        if (_canEditThanks.Contains(messageParam.Id)) await Thanks(messageParam);
    }

    private async Task Thanks(SocketMessage messageParam)
    {
        var channel = (SocketGuildChannel)messageParam.Channel;
        var guildId = channel.Guild.Id;

        if (guildId != _settings.GuildId) return;

        if (messageParam.Author.IsBot)
            return;
        var match = Regex.Match(messageParam.Content, _thanksRegex);
        if (!match.Success)
            return;

        var userId = messageParam.Author.Id;
        var mentions = messageParam.MentionedUsers;
        mentions = mentions.Distinct().Where(who => !who.IsBot && who.Id != userId).ToList();

        const int defaultDelTime = 120;
        if (mentions.Count > 0)
        {
            if (_thanksCooldown.HasUser(userId))
            {
                await messageParam.Channel!.SendMessageAsync(
                        $"{messageParam.Author!.Mention} you must wait " +
                        $"{DateTime.Now - _thanksCooldown[userId]:ss} " +
                        "seconds before giving another karma point." + Environment.NewLine +
                        "(In the future, if you are trying to thank multiple people, include all their names in the thanks message.)")
                    .DeleteAfterTime(defaultDelTime)!;
                return;
            }

            var joinDate = ((IGuildUser)messageParam.Author).JoinedAt;
            var j = joinDate + TimeSpan.FromSeconds(_thanksMinJoinTime);
            if (j > DateTime.Now)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.Append(messageParam.Author.GetUserPreferredName().ToBold());
            sb.Append(" gave karma to ");
            sb.Append(mentions.ToArray().ToUserPreferredNameArray().ToBoldArray().ToCommaList());
            var dbQuery = _databaseService.Query;
            if (dbQuery != null)
            {
                foreach (var mention in mentions)
                    await dbQuery.IncrementKarma(mention.Id.ToString());

                var authorKarmaGiven = await dbQuery.GetKarmaGiven(messageParam.Author.Id.ToString());
                await dbQuery.UpdateKarmaGiven(messageParam.Author.Id.ToString(), authorKarmaGiven + 1);
            }

            sb.Append(".");

            _canEditThanks.Remove(messageParam.Id);
            _thanksCooldown.AddCooldown(userId, _thanksCooldownTime);

            await messageParam.Channel.SendMessageAsync(sb.ToString());
            await _loggingService.LogChannelAndFile(sb + " in channel " + messageParam.Channel.Name);
        }

        if (mentions.Count == 0 && _canEditThanks.Add(messageParam.Id))
        {
            var _ = _canEditThanks.RemoveAfterSeconds(messageParam.Id, 240);
        }
    }
}
