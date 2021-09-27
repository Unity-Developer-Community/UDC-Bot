using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Extensions;
using DiscordBot.Settings.Deserialized;
using DiscordBot.Skin;
using ImageMagick;
using Newtonsoft.Json;

namespace DiscordBot.Services
{
    public class UserService
    {
        private readonly HashSet<ulong> _canEditThanks; //Doesn't need to be saved
        private readonly DiscordSocketClient _client;
        public readonly string CodeFormattingExample;

        private readonly int _codeReminderCooldownTime;
        public readonly string CodeReminderFormattingExample;
        private readonly DatabaseService _databaseService;
        private readonly ILoggingService _loggingService;

        private readonly List<ulong> _noXpChannels;

        private readonly Settings.Deserialized.Settings _settings;
        private readonly Dictionary<ulong, DateTime> _thanksCooldown;

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

        public UserService(DiscordSocketClient client, DatabaseService databaseService, ILoggingService loggingService, UpdateService updateService,
                           Settings.Deserialized.Settings settings, UserSettings userSettings)
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
            CodeReminderFormattingExample = CodeFormattingExample + Environment.NewLine +
                                             "Simple as that! If you'd like me to stop reminding you about this, simply type \"!disablecodetips\"";

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
            _client.UserJoined += UserJoined;
            _client.GuildMemberUpdated += UserUpdated;
            _client.UserLeft += UserLeft;

            LoadData();
            UpdateLoop();
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
            //Console.WriteLine($"basexp {baseXp} karma {karma}  bonus {bonusXp}");
            _xpCooldown.AddCooldown(userId, waitTime);
            //Console.WriteLine($"{_xpCooldown[id].Minute}  {_xpCooldown[id].Second}");

            await _databaseService.Query().UpdateXp(userId.ToString(), user.Exp + (uint)xpGain);

            _loggingService.LogXp(messageParam.Channel.Name, messageParam.Author.Username, baseXp, bonusXp, reduceXp, xpGain);

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
                    Console.WriteLine(e);
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

            using (var result = profileCard.Mosaic())
            {
                result.Write($"{_settings.ServerRootPath}/images/profiles/{user.Username}-profile.png");
            }

            return $"{_settings.ServerRootPath}/images/profiles/{user.Username}-profile.png";
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
                        "(In the future, if you are trying to thank multiple people, include all their names in the thanks message)").DeleteAfterTime(defaultDelTime);
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
                if ((mentionedSelf || mentionedBot) && mentions.Count == 1 || mentionedBot && mentionedSelf && mentions.Count == 2)
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
            if (messageParam.Author.IsBot)
                return;
            var userId = messageParam.Author.Id;

            //Simple check to cover most large code posting cases without being an issue for most non-code messages
            // TODO Perhaps work out a more advanced Regex based check at a later time
            if (!CodeReminderCooldown.HasUser(userId))
            {
                var content = messageParam.Content;
                //Changed to a regex check so that bot only alerts when there aren't surrounding backticks, instead of just looking if no triple backticks exist.
                var foundCodeTags = Regex.Match(content, ".*?`[^`].*?`", RegexOptions.Singleline).Success;
                var foundCurlyFries = content.Contains("{") && content.Contains("}");
                if (!foundCodeTags && foundCurlyFries)
                {
                    CodeReminderCooldown.AddCooldown(userId, _codeReminderCooldownTime);
                    var sb = new StringBuilder();
                    sb.Append(messageParam.Author.Mention)
                      .AppendLine(
                          " are you trying to post code? If so, please place 3 backticks \\`\\`\\` at the beginning and end of your code, like so:");
                    sb.AppendLine(CodeReminderFormattingExample);
                    await messageParam.Channel.SendMessageAsync(sb.ToString()).DeleteAfterTime(minutes: 10);
                }
                else if (foundCodeTags && foundCurlyFries && content.Contains("```") && !content.ToLower().Contains("```cs"))
                {
                    var sb = new StringBuilder();
                    sb.Append(messageParam.Author.Mention)
                      .AppendLine(
                          " Don't forget to add \"cs\" after your first 3 backticks so that your code receives syntax highlighting:");
                    sb.AppendLine(CodeReminderFormattingExample);
                    await messageParam.Channel.SendMessageAsync(sb.ToString()).DeleteAfterTime(minutes: 8);
                    CodeReminderCooldown.AddCooldown(userId, _codeReminderCooldownTime);
                }
            }
        }

        private async Task ScoldForAtEveryoneUsage(SocketMessage messageParam)
        {
            if (messageParam.Author.IsBot || ((IGuildUser)messageParam.Author).GuildPermissions.MentionEveryone)
                return;
            var content = messageParam.Content;
            if (content.Contains("@everyone") || content.Contains("@here"))
                await messageParam.Channel.SendMessageAsync(
                                      $"That is very rude of you to try and alert **everyone** on the server {messageParam.Author.Mention}!{Environment.NewLine}" +
                                      "Thankfully, you do not have permission to do so. If you are asking a question, people will help you when they have time.")
                                  .DeleteAfterTime(minutes: 5);
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

            // We push into new Task to avoid blocking gateway
            Task.Run(async () =>
            {
                // We wait X amount of seconds before posting a welcome message in chat.
                await Task.Delay(_settings.WelcomeMessageDelaySeconds * 1000);
                var userTest = _client.GetGuild(_settings.GuildId).GetUser(user.Id);
                if (userTest != null)
                {
                    var em = WelcomeMessage(user);
                    if (socketTextChannel != null)
                        await socketTextChannel.SendMessageAsync(string.Empty, false, em);
                }
            });
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

        private async Task UserLeft(SocketGuildUser user)
        {
            DateTime joinDate = user.JoinedAt.Value.Date;

            var timeStayed = DateTime.Now - joinDate;
            await _loggingService.LogAction(
                $"User Left - After {(timeStayed.Days > 1 ? Math.Floor((double)timeStayed.Days) + " days" : " ")}" +
                $" {Math.Floor((double)timeStayed.Hours).ToString(CultureInfo.InvariantCulture)} hours {user.Mention} - `{user.Username}#{user.DiscriminatorValue}` - ID : `{user.Id}`");
            await _databaseService.DeleteUser(user.Id);
        }

        #endregion
    }
}
