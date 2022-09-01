using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Settings;
using DiscordBot.Skin;
using ImageMagick;
using Newtonsoft.Json;

namespace DiscordBot.Services;

public class UserService
{
    private readonly HashSet<ulong> _canEditThanks; //Doesn't need to be saved
    private readonly DiscordSocketClient _client;
    public readonly string CodeFormattingExample;
    private readonly int _codeReminderCooldownTime;
    public readonly string CodeReminderFormattingExample;
    private readonly DatabaseService _databaseService;
    private readonly ILoggingService _loggingService;

    private readonly Regex _x3CodeBlock =
        new Regex("^(?<CodeBlock>`{3}((?<CS>\\w*?$)|$).+?({.+?}).+?`{3})",
            RegexOptions.Multiline | RegexOptions.Singleline);

    private readonly Regex _x2CodeBlock = new Regex("^(`{2})[^`].+?([^`]`{2})$", RegexOptions.Multiline);
    private readonly List<Regex> _codeBlockWarnPatterns;
    private readonly short _maxCodeBlockLengthWarning = 800;

    private readonly List<ulong> _noXpChannels;

    private readonly BotSettings _settings;
    private readonly Dictionary<ulong, DateTime> _thanksCooldown;
    private readonly Dictionary<ulong, DateTime> _everyoneScoldCooldown = new Dictionary<ulong, DateTime>();

    private readonly List<(ulong id, DateTime time)> _welcomeNoticeUsers = new List<(ulong id, DateTime time)>();

    private readonly int _thanksCooldownTime;
    private readonly int _thanksMinJoinTime;

    private readonly string _thanksRegex;
    private readonly UpdateService _updateService;

    private readonly Dictionary<ulong, DateTime> _xpCooldown;
    private readonly int _xpMaxCooldown;
    private readonly int _xpMaxPerMessage;
    private readonly int _xpMinCooldown;

    private readonly int _xpMinPerMessage;

    private readonly Random _rand;

    public Dictionary<ulong, DateTime> MutedUsers { get; private set; }
    public int WaitingWelcomeMessagesCount => _welcomeNoticeUsers.Count;

    public DateTime NextWelcomeMessage =>
        _welcomeNoticeUsers.Any() ? _welcomeNoticeUsers.Min(x => x.time) : DateTime.MaxValue;

    public UserService(DiscordSocketClient client, DatabaseService databaseService, ILoggingService loggingService,
        UpdateService updateService,
        BotSettings settings, UserSettings userSettings)
    {
        _client = client;
        _rand = new Random();
        _databaseService = databaseService;
        _loggingService = loggingService;
        _updateService = updateService;
        _settings = settings;
        MutedUsers = new Dictionary<ulong, DateTime>();
        _xpCooldown = new Dictionary<ulong, DateTime>();
        _canEditThanks = new HashSet<ulong>(32);
        _thanksCooldown = new Dictionary<ulong, DateTime>();
        CodeReminderCooldown = new Dictionary<ulong, DateTime>();

        //TODO We should make this into a config file that we can confiure during runtime.
        _noXpChannels = new List<ulong>
        {
            _settings.BotCommandsChannel.Id
        };

        /*
        Init XP
        */
        _xpMinPerMessage = userSettings.XpMinPerMessage;
        _xpMaxPerMessage = userSettings.XpMaxPerMessage;
        _xpMinCooldown = userSettings.XpMinCooldown;
        _xpMaxCooldown = userSettings.XpMaxCooldown;

        /*
        Init thanks
        */
        var sbThanks = new StringBuilder();
        var thx = userSettings.Thanks;
        sbThanks.Append("(?i)\\b(");
        foreach (var t in thx) sbThanks.Append(t).Append("|");

        sbThanks.Length--; //Efficiently remove the final pipe that gets added in final loop, simplifying loop
        sbThanks.Append(")\\b");
        _thanksRegex = sbThanks.ToString();
        _thanksCooldownTime = userSettings.ThanksCooldown;
        _thanksMinJoinTime = userSettings.ThanksMinJoinTime;

        /*
         Init Code analysis
        */
        _codeReminderCooldownTime = userSettings.CodeReminderCooldown;
        CodeFormattingExample = @"\`\`\`cs" + Environment.NewLine +
                                "Write your code on new line here." + Environment.NewLine +
                                @"\`\`\`" + Environment.NewLine;

        CodeReminderFormattingExample = CodeFormattingExample + "*To disable these reminders use \"!disablecodetips\"*";

        //TODO Detect double code block and tell them to use 3? Seems kinda pointless since all it provides is highlights

        _codeBlockWarnPatterns = new List<Regex>();
        // Checks if there is { } in the message
        _codeBlockWarnPatterns.Add(new Regex(".*?({.+?}).*?", RegexOptions.Singleline));
        // We look for (if, else if) followed by ( and ) somewhere after. We also check that the ) is end of the line, or followed by comments //
        _codeBlockWarnPatterns.Add(new Regex("(if|else\\sif).?\\(.+\\).?($|\\/{2}|\\s?)", RegexOptions.Multiline));
        // Check for a method from start of line (since discord would ignore tab) and if any comments after
        _codeBlockWarnPatterns.Add(new Regex("^(\\w*.\\w*)\\(\\w*?\\);($|.?($|.*?\\/{2}))", RegexOptions.Multiline));
        // Check for some collection of characters being set to some other collection of characters and check if end of line or comment.
        _codeBlockWarnPatterns.Add(new Regex("^.+? =.+?($|.*?\\/\\/)", RegexOptions.Multiline));

        /* Make sure folders we require exist */
        if (!Directory.Exists($"{_settings.ServerRootPath}/images/profiles/"))
        {
            Directory.CreateDirectory($"{_settings.ServerRootPath}/images/profiles/");
        }

        /*
         Event subscriptions
        */
        _client.MessageReceived += UpdateXp;
        _client.MessageReceived += Thanks;
        _client.MessageUpdated += ThanksEdited;
        _client.MessageReceived += CodeCheck;
        _client.MessageReceived += ScoldForAtEveryoneUsage;
        _client.MessageReceived += AutoCreateThread;
        _client.UserJoined += UserJoined;
        _client.GuildMemberUpdated += UserUpdated;
        _client.UserLeft += UserLeft;

        _client.UserIsTyping += UserIsTyping;

        LoadData();
        UpdateLoop();
      
        Task.Run(DelayedWelcomeService);
    }

    private async Task UserLeft(SocketGuild guild, SocketUser user)
    {
        if (user.IsBot) return;
        // Try get user, may not exist anymore since they've "left"
        var guildUser = guild.GetUser(user.Id);
        if (guildUser?.JoinedAt != null)
        {
            var joinDate = guildUser.JoinedAt.Value.Date;

            var timeStayed = DateTime.Now - joinDate;
            await _loggingService.LogAction(
                $"User Left - After {(timeStayed.Days > 1 ? Math.Floor((double)timeStayed.Days) + " days" : " ")}" +
                $" {Math.Floor((double)timeStayed.Hours).ToString(CultureInfo.InvariantCulture)} hours {user.Mention} - `{user.Username}#{user.DiscriminatorValue}` - ID : `{user.Id}`");
        }
        // If bot is to slow to get user info, we just say they left at current time.
        else
        {
            await _loggingService.LogAction(
                $"User `{user.Username}#{user.DiscriminatorValue}` - ID : `{user.Id}` - Left at {DateTime.Now}");
        }
    }

    public Dictionary<ulong, DateTime> CodeReminderCooldown { get; private set; }

    private async void UpdateLoop()
    {
        while (true)
        {
            await Task.Delay(10000);
            SaveData();
        }
        // ReSharper disable once FunctionNeverReturns
    }

    private void LoadData()
    {
        var data = _updateService.GetUserData();
        MutedUsers = data.MutedUsers ?? new Dictionary<ulong, DateTime>();
        CodeReminderCooldown = data.CodeReminderCooldown ?? new Dictionary<ulong, DateTime>();
    }

    private void SaveData()
    {
        var data = new UserData
        {
            MutedUsers = MutedUsers,
            CodeReminderCooldown = CodeReminderCooldown
        };
        _updateService.SetUserData(data);
    }

    public async Task UpdateXp(SocketMessage messageParam)
    {
        if (messageParam.Author.IsBot)
            return;

        if (_noXpChannels.Contains(messageParam.Channel.Id))
            return;

        var userId = messageParam.Author.Id;
        var waitTime = _rand.Next(_xpMinCooldown, _xpMaxCooldown);
        float baseXp = _rand.Next(_xpMinPerMessage, _xpMaxPerMessage);
        float bonusXp = 0;

        if (_xpCooldown.HasUser(userId))
            return;

        var user = await _databaseService.Query().GetUser(userId.ToString());
        if (user == null)
        {
            await _databaseService.AddNewUser((SocketGuildUser)messageParam.Author);
            user = await _databaseService.Query().GetUser(userId.ToString());
        }

        if (messageParam.Author.Activities.Any(a => Regex.Match(a.Name, "(Unity.+)").Length > 0))
            bonusXp += baseXp / 4;

        bonusXp += baseXp * (1f + user.Karma / 100f);

        //Reduce XP for members with no role
        if (((IGuildUser)messageParam.Author).RoleIds.Count < 2)
            baseXp *= .9f;

        //Lower xp for difference between level and karma
        var reduceXp = 1f;
        if (user.Karma < user.Level) reduceXp = 1 - Math.Min(.9f, (user.Level - user.Karma) * .05f);

        var xpGain = (int)Math.Round((baseXp + bonusXp) * reduceXp);
        _xpCooldown.AddCooldown(userId, waitTime);

        await _databaseService.Query().UpdateXp(userId.ToString(), user.Exp + (uint)xpGain);

        _loggingService.LogXp(messageParam.Channel.Name, messageParam.Author.Username, baseXp, bonusXp, reduceXp,
            xpGain);

        await LevelUp(messageParam, userId);
    }

    /// <summary>
    /// Show level up message
    /// </summary>
    /// <param name="messageParam"></param>
    /// <param name="userId"></param>
    /// <returns></returns>
    private async Task LevelUp(SocketMessage messageParam, ulong userId)
    {
        var level = await _databaseService.Query().GetLevel(userId.ToString());
        var xp = await _databaseService.Query().GetXp(userId.ToString());

        var xpHigh = GetXpHigh(level);

        if (xp < xpHigh)
            return;

        await _databaseService.Query().UpdateLevel(userId.ToString(), level + 1);

        await messageParam.Channel.SendMessageAsync($"**{messageParam.Author}** has leveled up !").DeleteAfterTime(60);
        //TODO Add level up card
    }

    private double GetXpLow(uint level) => 70d - 139.5d * (level + 1d) + 69.5 * Math.Pow(level + 1d, 2d);

    private double GetXpHigh(uint level) => 70d - 139.5d * (level + 2d) + 69.5 * Math.Pow(level + 2d, 2d);

    private SkinData GetSkinData() =>
        JsonConvert.DeserializeObject<SkinData>(File.ReadAllText($"{_settings.ServerRootPath}/skins/skin.json"),
            new SkinModuleJsonConverter());

    /// <summary>
    ///     Generate the profile card for a given user and returns the generated image path
    /// </summary>
    /// <param name="user"></param>
    /// <returns></returns>
    public async Task<string> GenerateProfileCard(IUser user)
    {
        var userData = await _databaseService.Query().GetUser(user.Id.ToString());

        var xpTotal = userData.Exp;
        var xpRank = await _databaseService.Query().GetLevelRank(userData.UserID, userData.Level);
        var karmaRank = await _databaseService.Query().GetKarmaRank(userData.UserID, userData.Karma);
        var karma = userData.Karma;
        var level = userData.Level;
        var xpLow = GetXpLow(level);
        var xpHigh = GetXpHigh(level);

        var xpShown = (uint)(xpTotal - xpLow);
        var maxXpShown = (uint)(xpHigh - xpLow);

        var percentage = (float)xpShown / maxXpShown;

        var u = (IGuildUser)user;
        IRole mainRole = null;
        foreach (var id in u.RoleIds)
        {
            var role = u.Guild.GetRole(id);
            if (mainRole == null)
                mainRole = u.Guild.GetRole(id);
            else if (role.Position > mainRole.Position) mainRole = role;
        }

        mainRole ??= u.Guild.EveryoneRole;

        using var profileCard = new MagickImageCollection();
        var skin = GetSkinData();
        var profile = new ProfileData
        {
            Karma = karma,
            KarmaRank = (uint)karmaRank,
            Level = (uint)level,
            MainRoleColor = mainRole.Color,
            MaxXpShown = maxXpShown,
            Nickname = ((IGuildUser)user).Nickname,
            UserId = ulong.Parse(userData.UserID),
            Username = user.Username,
            XpHigh = xpHigh,
            XpLow = xpLow,
            XpPercentage = percentage,
            XpRank = (uint)xpRank,
            XpShown = xpShown,
            XpTotal = (uint)xpTotal
        };

        var background = new MagickImage($"{_settings.ServerRootPath}/skins/{skin.Background}");

        var avatarUrl = user.GetAvatarUrl(ImageFormat.Auto, 256);
        if (string.IsNullOrEmpty(avatarUrl))
            profile.Picture = new MagickImage($"{_settings.ServerRootPath}/images/default.png");
        else
            try
            {
                Stream stream;

                using (var http = new HttpClient())
                {
                    stream = await http.GetStreamAsync(new Uri(avatarUrl));
                }

                profile.Picture = new MagickImage(stream);
            }
            catch (Exception e)
            {
                LoggingService.LogToConsole($"Failed to download user profile image for ProfileCard.\nEx:{e.Message}",
                    LogSeverity.Warning);
                profile.Picture = new MagickImage($"{_settings.ServerRootPath}/images/default.png");
            }

        profile.Picture.Resize(skin.AvatarSize, skin.AvatarSize);
        profileCard.Add(background);

        foreach (var layer in skin.Layers)
        {
            if (layer.Image != null)
            {
                var image = layer.Image.ToLower() == "avatar"
                    ? profile.Picture
                    : new MagickImage($"{_settings.ServerRootPath}/skins/{layer.Image}");

                background.Composite(image, (int)layer.StartX, (int)layer.StartY, CompositeOperator.Over);
            }

            var l = new MagickImage(MagickColors.Transparent, (int)layer.Width, (int)layer.Height);
            foreach (var module in layer.Modules) module.GetDrawables(profile).Draw(l);

            background.Composite(l, (int)layer.StartX, (int)layer.StartY, CompositeOperator.Over);
        }

        var path = $"{_settings.ServerRootPath}/images/profiles/{user.Username}-profile.png";
        
        using (var result = profileCard.Mosaic())
        {
            result.Write(path);
        }

        return path;
    }

    public Embed WelcomeMessage(SocketGuildUser user)
    {
        string icon = user.GetAvatarUrl();
        icon = string.IsNullOrEmpty(icon) ? "https://cdn.discordapp.com/embed/avatars/0.png" : icon;

        var builder = new EmbedBuilder()
            .WithDescription($"Welcome to Unity Developer Community **{user.Username}#{user.Discriminator}**!")
            .WithColor(new Color(0x12D687))
            .WithAuthor(author =>
            {
                author
                    .WithName(user.Username)
                    .WithIconUrl(icon);
            });

        var embed = builder.Build();
        return embed;
    }

    public int GetGatewayPing() => _client.Latency;

    #region Events

    // Message Edited Thanks
    public async Task ThanksEdited(Cacheable<IMessage, ulong> cachedMessage, SocketMessage messageParam,
        ISocketMessageChannel socketMessageChannel)
    {
        if (_canEditThanks.Contains(messageParam.Id)) await Thanks(messageParam);
    }

    public async Task Thanks(SocketMessage messageParam)
    {
        //Get guild id
        var channel = (SocketGuildChannel)messageParam.Channel;
        var guildId = channel.Guild.Id;

        //Make sure its in the UDH server
        if (guildId != _settings.GuildId) return;

        if (messageParam.Author.IsBot)
            return;
        var match = Regex.Match(messageParam.Content, _thanksRegex);
        if (!match.Success)
            return;
        var mentions = messageParam.MentionedUsers;
        mentions = mentions.Distinct().ToList();
        var userId = messageParam.Author.Id;
        const int defaultDelTime = 120;
        if (mentions.Count > 0)
        {
            if (_thanksCooldown.HasUser(userId))
            {
                await messageParam.Channel.SendMessageAsync(
                        $"{messageParam.Author.Mention} you must wait " +
                        $"{DateTime.Now - _thanksCooldown[userId]:ss} " +
                        "seconds before giving another karma point." + Environment.NewLine +
                        "(In the future, if you are trying to thank multiple people, include all their names in the thanks message)")
                    .DeleteAfterTime(defaultDelTime);
                return;
            }

            var joinDate = ((IGuildUser)messageParam.Author).JoinedAt;
            var j = joinDate + TimeSpan.FromSeconds(_thanksMinJoinTime);
            if (j > DateTime.Now)
            {
                return;
            }

            var mentionedSelf = false;
            var mentionedBot = false;
            var sb = new StringBuilder();
            sb.Append("**").Append(messageParam.Author.Username).Append("** gave karma to **");
            foreach (var user in mentions)
            {
                if (user.IsBot)
                {
                    mentionedBot = true;
                    continue;
                }

                if (user.Id == userId)
                {
                    mentionedSelf = true;
                    continue;
                }

                await _databaseService.Query().IncrementKarma(user.Id.ToString());
                sb.Append(user.Username).Append(" , ");
            }

            // Even if a user gives multiple karma in one message, we only add one.
            var authorKarmaGiven = await _databaseService.Query().GetKarmaGiven(messageParam.Author.Id.ToString());
            await _databaseService.Query().UpdateKarmaGiven(messageParam.Author.Id.ToString(), authorKarmaGiven + 1);

            sb.Length -= 2; //Removes last instance of appended comma without convoluted tracking
            sb.Append("**");
            if (mentionedSelf)
                await messageParam.Channel.SendMessageAsync(
                    $"{messageParam.Author.Mention} you can't give karma to yourself.").DeleteAfterTime(defaultDelTime);

            _canEditThanks.Remove(messageParam.Id);

            //Don't give karma cooldown if user only mentioned himself or the bot or both
            if ((mentionedSelf || mentionedBot) && mentions.Count == 1 ||
                mentionedBot && mentionedSelf && mentions.Count == 2)
                return;
            _thanksCooldown.AddCooldown(userId, _thanksCooldownTime);
            await messageParam.Channel.SendMessageAsync(sb.ToString());
            await _loggingService.LogAction(sb + " in channel " + messageParam.Channel.Name);
        }

        if (mentions.Count == 0 && _canEditThanks.Add(messageParam.Id))
        {
            var _ = _canEditThanks.RemoveAfterSeconds(messageParam.Id, 240);
        }
    }

    public async Task CodeCheck(SocketMessage messageParam)
    {
        // Don't correct a Bot, don't correct in off-topic
        if (messageParam.Author.IsBot || messageParam.Channel.Id == _settings.GeneralChannel.Id)
            return;

        // We just ignore anything if it is under 200 characters
        if (messageParam.Content.Length < 200)
            return;
        
        var userId = messageParam.Author.Id;

        //Simple check to cover most large code posting cases without being an issue for most non-code messages
        // TODO Perhaps work out a more advanced Regex based check at a later time
        if (!CodeReminderCooldown.HasUser(userId))
        {
            var content = messageParam.Content;

            // We have a smart cookie using ```cs so we assume they're all knowing and abort early to save cpu
            var foundTrippleCodeBlock = _x3CodeBlock.Match(content);
            if (foundTrippleCodeBlock.Groups["CS"].Length > 0)
                return;
            if (foundTrippleCodeBlock.Groups["CodeBlock"].Success)
            {
                // A ``` codeblock was found, but no CS, let 'em know
                await messageParam.Channel.SendMessageAsync(
                        $"{messageParam.Author.Mention} when using code blocks remember to use the ***syntax highlights*** to improve readability.\n{CodeReminderFormattingExample}")
                    .DeleteAfterSeconds(seconds: 60);
                return;
            }

            // Checks get a bit more expensive from here
            var foundDoubleCodeBlock = _x2CodeBlock.Match(content).Success;

            int hits = 0;
            foreach (var regex in _codeBlockWarnPatterns)
            {
                hits += regex.Match(content).Captures.Count;
            }

            // Some arbitary condition, this means 3 regex captures would be required which should easy enough to trigger without much chance for a false positive.
            if (!foundDoubleCodeBlock && hits >= 3)
            {
                //! CodeReminderCooldown.AddCooldown(userId, _codeReminderCooldownTime);
                await messageParam.Channel.SendMessageAsync(
                        $"{messageParam.Author.Mention} are you sharing c# scripts? Remember to use codeblocks to help readability!\n{CodeReminderFormattingExample}")
                    .DeleteAfterSeconds(seconds: 60);
                if (content.Length > _maxCodeBlockLengthWarning)
                {
                    await messageParam.Channel.SendMessageAsync(
                            $"The code you're sharing is quite long, maybe use a free service like <https://hastebin.com> and share the link here instead.")
                        .DeleteAfterSeconds(seconds: 60);
                }
            }
            // If we know there is a codeblock
            else if (foundDoubleCodeBlock && hits > 0)
            {
                //! CodeReminderCooldown.AddCooldown(userId, _codeReminderCooldownTime);
                await messageParam.Channel.SendMessageAsync(
                        $"{messageParam.Author.Mention} when using code blocks remember to use \\`\\`\\`cs as this will help improve readability for C# scripts.\n{CodeReminderFormattingExample}")
                    .DeleteAfterSeconds(seconds: 60);
            }
        }
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
            // We add to dictionary with the time it must be passed before they'll be notified again.
            _everyoneScoldCooldown[messageParam.Author.Id] =
                DateTime.Now.AddSeconds(_settings.EveryoneScoldPeriodSeconds);

            await messageParam.Channel.SendMessageAsync(
                    $"Please don't try to alert **everyone** on the server {messageParam.Author.Mention}!\nIf you are asking a question, people will help you when they have time.")
                .DeleteAfterTime(minutes: 2);
        }
    }

    // Anything relevant to the first time someone connects to the server

    #region Welcome Service

    // If a user talks before they've been welcomed, we welcome them and remove them from the welcome list so they're not welcomes a second time.
    private async Task UserIsTyping(Cacheable<IUser, ulong> user, Cacheable<IMessageChannel, ulong> channel)
    {
        if (_welcomeNoticeUsers.Count == 0)
            return;
        if (user.Value.IsBot)
            return;

        await ProcessWelcomeUser(user.Id, user.Value);
    }

    private async Task UserJoined(SocketGuildUser user)
    {
        // Send them the Welcome DM first.
        await DMFormattedWelcome(user);

        var socketTextChannel = _client.GetChannel(_settings.GeneralChannel.Id) as SocketTextChannel;
        await _databaseService.AddNewUser(user);

        // Check if moderator commands are enabled, and if so we check if they were previously muted.
        if (_settings.ModeratorCommandsEnabled)
        {
            if (MutedUsers.HasUser(user.Id))
            {
                await user.AddRoleAsync(socketTextChannel?.Guild.GetRole(_settings.MutedRoleId));
                await _loggingService.LogAction(
                    $"Currently muted user rejoined - {user.Mention} - `{user.Username}#{user.DiscriminatorValue}` - ID : `{user.Id}`");
                if (socketTextChannel != null)
                    await socketTextChannel.SendMessageAsync(
                        $"{user.Mention} tried to rejoin the server to avoid their mute. Mute time increased by 72 hours.");
                MutedUsers.AddCooldown(user.Id, hours: 72);
                return;
            }
        }

        await _loggingService.LogAction(
            $"User Joined - {user.Mention} - `{user.Username}#{user.DiscriminatorValue}` - ID : `{user.Id}`");

        // We check if they're already in the welcome list, if they are we don't add them again to avoid double posts
        if (_welcomeNoticeUsers.Count == 0 || !_welcomeNoticeUsers.Exists(u => u.id == user.Id))
        {
            _welcomeNoticeUsers.Add((user.Id, DateTime.Now.AddSeconds(_settings.WelcomeMessageDelaySeconds)));
        }
    }

    // Welcomes users to the server after they've been connected for over x number of seconds.
    private async Task DelayedWelcomeService()
    {
        try
        {
            while (true)
            {
                var now = DateTime.Now;
                // This could be optimized, however the users in this list won't ever really be large enough to matter.
                // We loop through our list, anyone that has been in the list for more than x seconds is welcomed.
                foreach (var userData in _welcomeNoticeUsers.Where(u => u.time < now))
                {
                    await ProcessWelcomeUser(userData.id, null);
                }

                await Task.Delay(10000);
            }
        }
        catch (Exception e)
        {
            // Catch and show exception
            LoggingService.LogToConsole($"UserService Exception during welcome message.\n{e.Message}",
                LogSeverity.Error);
            await _loggingService.LogAction($"UserService Exception during welcome message.\n{e.Message}.", false, true);
        }
    }

    private async Task ProcessWelcomeUser(ulong userID, IUser user = null)
    {
        if (_welcomeNoticeUsers.Exists(u => u.id == userID))
        {
            // If we didn't get the user passed in, we try grab it
            user ??= await _client.GetUserAsync(userID);
            // if they're null, they've likely left, so we just remove them from the list.
            if (user != null)
            {
                var offTopic = await _client.GetChannelAsync(_settings.GeneralChannel.Id) as SocketTextChannel;
                var em = WelcomeMessage(user as SocketGuildUser);
                if (offTopic != null)
                    await offTopic.SendMessageAsync(string.Empty, false, em);
            }

            // Remove the user from the welcome list.
            _welcomeNoticeUsers.RemoveAll(u => u.id == userID);
        }
    }


    public async Task<bool> DMFormattedWelcome(SocketGuildUser user)
    {
        var dm = await user.CreateDMChannelAsync();
        return await dm.TrySendMessage(embed: GetWelcomeEmbed(user.Username));
    }

    public Embed GetWelcomeEmbed(string username = "")
    {
        //TODO Generate this using Settings or some other config, hardcoded isn't ideal.
        var em = new EmbedBuilder()
            .WithColor(new Color(0x12D687))
            .AddField("Hello " + username,
                "Welcome to Unity Developer Community!\nPlease read and respect the rules to keep the community friendly!\n*When asking questions, remember to ask your question, [don't ask to ask](https://dontasktoask.com/).*")
            .AddField("__RULES__",
                ":white_small_square: Be polite and respectful.\n" +
                ":white_small_square: No Direct Messages to users without permission.\n" +
                ":white_small_square: Do not post the same question in multiple channels.\n" +
                ":white_small_square: Only post links to your games in the appropriate channels.\n" +
                ":white_small_square: Some channels have additional rules, please check pinned messages.\n" +
                $":white_small_square: A more inclusive list of rules can be found in {(_settings.RulesChannel is null || _settings.RulesChannel.Id == 0 ? "#rules" : $"<#{_settings.RulesChannel.Id.ToString()}>")}"
            )
            .AddField("__PROGRAMMING RESOURCES__",
                ":white_small_square: Official Unity [Manual](https://docs.unity3d.com/Manual/index.html)\n" +
                ":white_small_square: Official Unity [Script API](https://docs.unity3d.com/ScriptReference/index.html)\n" +
                ":white_small_square: Introductory Tutorials: [Official Unity Tutorials](https://unity3d.com/learn/tutorials)\n" +
                ":white_small_square: Intermediate Tutorials: [CatLikeCoding](https://catlikecoding.com/unity/tutorials/)\n"
            )
            .AddField("__ART RESOURCES__",
                ":white_small_square: Blender Beginner Tutorial [Blender Guru Donut](https://www.youtube.com/watch?v=TPrnSACiTJ4&list=PLjEaoINr3zgEq0u2MzVgAaHEBt--xLB6U&index=2)\n" +
                ":white_small_square: Free Simple Assets [Kenney](https://www.kenney.nl/assets)\n" +
                ":white_small_square: Game Assets [itch.io](https://itch.io/game-assets/free)"
            )
            .AddField("__GAME DESIGN RESOURCES__",
                ":white_small_square: How to write a Game Design Document (GDD) [Gamasutra](https://www.gamasutra.com/blogs/LeandroGonzalez/20160726/277928/How_to_Write_a_Game_Design_Document.php)\n" +
                ":white_small_square: How to start building video games [CGSpectrum](https://www.cgspectrum.com/blog/game-design-basics-how-to-start-building-video-games)\n" +
                ":white_small_square: Keep Things Clear: Don't Confuse Your Players [TutsPlus](https://gamedevelopment.tutsplus.com/articles/keep-things-clear-dont-confuse-your-players--cms-22780)"
            );
        return (em.Build());
    }

    #endregion

    private async Task UserUpdated(Cacheable<SocketGuildUser, ulong> oldUserCached, SocketGuildUser user)
    {
        var oldUser = await oldUserCached.GetOrDownloadAsync();
        if (oldUser.Nickname != user.Nickname)
        {
            await _loggingService.LogAction(
                $"User {oldUser.Nickname ?? oldUser.Username}#{oldUser.DiscriminatorValue} changed his " +
                $"username to {user.Nickname ?? user.Username}#{user.DiscriminatorValue}");
        }
    }

    private async Task AutoCreateThread(SocketMessage messageParam)
    {
        if (messageParam.Author.IsBot) return;

        foreach (var prefix in _settings.AutoThreadExclusionPrefixes)
            if (messageParam.Content.StartsWith(prefix))
                return;

        foreach (var AutoThreadChannel in _settings.AutoThreadChannels)
        {
            var channel = messageParam.Channel as SocketTextChannel;
            if (channel.Id.Equals(AutoThreadChannel.Id))
            {
                try
                {
                    ThreadArchiveDuration wantedDuration;
                    if (!Enum.TryParse<ThreadArchiveDuration>(AutoThreadChannel.Duration, out wantedDuration))
                        wantedDuration = ThreadArchiveDuration.ThreeDays;
                    Discord.ThreadArchiveDuration duration =
                        Utils.Utils.GetMaxThreadDuration(wantedDuration, _client.GetGuild(_settings.GuildId));
                    var title = AutoThreadChannel.GenerateTitle(messageParam.Author);
                    var thread = await channel.CreateThreadAsync(title, Discord.ThreadType.PublicThread, duration,
                        messageParam);

                    if (!String.IsNullOrEmpty(AutoThreadChannel.FirstMessage))
                    {
                        var message =
                            await thread.SendMessageAsync(AutoThreadChannel.GenerateFirstMessage(messageParam.Author));
                        await message.PinAsync();
                    }
                }
                catch (Exception err)
                {
                    LoggingService.LogToConsole($"Failed to CreateThread.\nEx: {err.ToString()}", LogSeverity.Error);
                }
            }
        }

    }

    #endregion
}
