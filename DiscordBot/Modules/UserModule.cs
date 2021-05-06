using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Extensions;
using DiscordBot.Properties;
using DiscordBot.Services;
using DiscordBot.Settings.Deserialized;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace DiscordBot.Modules
{
    public class UserModule : ModuleBase
    {
        private readonly ILoggingService _loggingService;
        private readonly DatabaseService _databaseService;
        private readonly UserService _userService;
        private readonly PublisherService _publisherService;
        private readonly UpdateService _updateService;
        private readonly CurrencyService _currencyService;

        private readonly Rules _rules;
        private static Settings.Deserialized.Settings _settings;

        public UserModule(ILoggingService loggingService, DatabaseService databaseService, UserService userService,
            PublisherService publisherService, UpdateService updateService, CurrencyService currencyService,
            Rules rules, Settings.Deserialized.Settings settings)
        {
            _loggingService = loggingService;
            _databaseService = databaseService;
            _userService = userService;
            _publisherService = publisherService;
            _updateService = updateService;
            _currencyService = currencyService;
            _rules = rules;
            _settings = settings;
        }

        [Command("help"), Summary("Display available commands (this). Syntax : !help")]
        [Alias("command", "commands")]
        public async Task DisplayHelp()
        {
            //TODO: Be possible in DM
            if (Context.Channel.Id != _settings.BotCommandsChannel.Id)
            {
                await Task.Delay(1000);
                await Context.Message.DeleteAsync();
                return;
            }

            var commands = CommandHandlingService.CommandList.MessageSplit();

            foreach (var message in commands)
                await ReplyAsync(message);
        }

        #region Rules

        [Command("rules"), Summary("Get the of the current channel by DM. Syntax : !rules")]
        public async Task Rules()
        {
            await Rules(Context.Channel);
            await Context.Message.DeleteAsync();
        }

        [Command("rules"), Summary("Get the rules of the mentionned channel by DM. !rules #channel")]
        [Alias("rule")]
        public async Task Rules(IMessageChannel channel)
        {
            var rule = _rules.Channel.First(x => x.Id == channel.Id);
            //IUserMessage m; //Unused, plan to be used in future?
            IDMChannel dm = await Context.User.GetOrCreateDMChannelAsync();
            if (rule == null)
            {
                await dm.SendMessageAsync(
                    "There is no special rule for this channel.\nPlease follow global rules (you can get them by typing `!globalrules`)");
            }
            else
            {
                await dm.SendMessageAsync(
                    $"{rule.Header}{(rule.Content.Length > 0 ? rule.Content : "There is no special rule for this channel.\nPlease follow global rules (you can get them by typing `!globalrules`)")}");
            }

            Task deleteAsync = Context.Message?.DeleteAsync();
            if (deleteAsync != null) await deleteAsync;
        }

        [Command("globalrules"), Summary("Get the Global Rules by DM. Syntax : !globalrules")]
        public async Task GlobalRules(int seconds = 60)
        {
            string globalRules = _rules.Channel.First(x => x.Id == 0).Content;
            IDMChannel dm = await Context.User.GetOrCreateDMChannelAsync();
            await dm.SendMessageAsync(globalRules);
            await Context.Message.DeleteAsync();
        }

        [Command("channels"), Summary("Get description of the channels by DM. Syntax : !channels")]
        public async Task ChannelsDescription()
        {
            //Display rules of this channel for x seconds
            var channelData = _rules.Channel;
            StringBuilder sb = new StringBuilder();
            foreach (var c in channelData)
                sb.Append((await Context.Guild.GetTextChannelAsync(c.Id))?.Mention).Append(" - ").Append(c.Header).Append("\n");
            string text = sb.ToString();

            IDMChannel dm = await Context.User.GetOrCreateDMChannelAsync();

            if (sb.ToString().Length > 2000)
            {
                await dm.SendMessageAsync(text.Substring(0, 2000));
                await dm.SendMessageAsync(text.Substring(2000, text.Length));
            }
            else
            {
                await dm.SendMessageAsync(text);
            }

            await Context.Message.DeleteAsync();
        }

        #endregion

        #region XP & Karma

        [Command("karma"), Summary("Display description of what Karma is for. Syntax : !karma")]
        public async Task KarmaDescription(int seconds = 60)
        {
            await ReplyAsync($"{Context.User.Username}, " +
                             $"Karma is tracked on your !profile, helping indicate how much you've helped others.{Environment.NewLine}" +
                             $"You also earn slightly more EXP from things the higher your Karma level is. Karma may be used for more features in the future.");

            await Task.Delay(TimeSpan.FromSeconds(seconds));
            await Context.Message.DeleteAsync();
        }

        [Command("top"), Summary("Display top 10 users by level. Syntax : !top")]
        [Alias("toplevel", "ranking")]
        public async Task TopLevel()
        {
            var users = _databaseService.GetTopLevel();
            var embed = GenerateRankEmbedFromList(users, "Level");
            await ReplyAsync(embed: embed).DeleteAfterTime(minutes: 3);
        }

        [Command("topkarma"), Summary("Display top 10 users by karma. Syntax : !topkarma")]
        [Alias("karmarank", "rankingkarma")]
        public async Task TopKarma()
        {
            var users = _databaseService.GetTopKarma();
            var embed = GenerateRankEmbedFromList(users, "Karma");
            await ReplyAsync(embed: embed).DeleteAfterTime(minutes: 3);
        }

        private Embed GenerateRankEmbedFromList(List<(ulong userID, int value)> data, string labelName)
        {
            EmbedBuilder embedBuilder = new EmbedBuilder();
            embedBuilder.Title = "Top 10 Users";
            embedBuilder.Description = $"The best of the best, by {labelName}.";
            
            StringBuilder rank = new StringBuilder();
            StringBuilder nick = new StringBuilder();
            StringBuilder level = new StringBuilder();
            for (int i = 0; i < data.Count; i++)
            {
                rank.Append($"#{(i+1)}\n");
                // rank.Append($"{(i+1)}{i switch { 0 => "st", 1 => "nd", 2 => "rd", _ => "th" }}\n");
                nick.Append($"<@{data[i].userID}>\n");
                level.Append($"{data[i].value.ToString()}\n");
            }
            
            embedBuilder.AddField("Rank", $"**{rank}**", true);
            embedBuilder.AddField("User", nick, true);
            embedBuilder.AddField(labelName, $"**{level}**", true);

            return embedBuilder.Build();
        }

        [Command("topudc"), Summary("Display top 10 users by UDC. Syntax : !topudc")]
        [Alias("udcrank")]
        public async Task TopUdc()
        {
            var users = _databaseService.GetTopUdc();

            StringBuilder sb = new StringBuilder();
            sb.Append("Here's the top 10 of users by UDC :");
            for (int i = 0; i < users.Count; i++)
                sb.Append($"\n#{i + 1} - **{(await Context.Guild.GetUserAsync(users[i].userId))?.Username}** ~ **{users[i].udc}** *UDC*");

            await ReplyAsync(sb.ToString()).DeleteAfterTime(minutes: 3);
        }

        [Command("profile"), Summary("Display current user profile card. Syntax : !profile")]
        public async Task DisplayProfile()
        {
            await DisplayProfile(Context.Message.Author);
        }

        [Command("profile"), Summary("Display profile card of mentionned user. Syntax : !profile @user")]
        public async Task DisplayProfile(IUser user)
        {
            try {
            IUserMessage profile = await Context.Channel.SendFileAsync(await _userService.GenerateProfileCard(user));

            await Task.Delay(1000);
            await Context.Message.DeleteAsync();
            await Task.Delay(TimeSpan.FromMinutes(3d));
            await profile.DeleteAsync();
            } catch (Exception e) {
                Console.Error.WriteLine(e);
            }
        }

        [Command("joindate"), Summary("Display your join date. Syntax : !joindate")]
        public async Task JoinDate()
        {
            var userId = Context.User.Id;
            DateTime.TryParse(_databaseService.GetUserJoinDate(userId), out DateTime joinDate);
            await ReplyAsync($"{Context.User.Mention} you joined **{joinDate:dddd dd/MM/yyy HH:mm:ss}**");
            await Context.Message.DeleteAsync();
        }

        #endregion

        #region Codetips

        [Command("codetip"), Summary("Show code formatting example. Syntax : !codetip userToPing(optional)")]
        [Alias("codetips")]
        public async Task CodeTip(IUser user = null)
        {
            var message = (user != null) ? user.Mention + ", " : "";
            message += "When posting code, format it like this to display it properly:" + Environment.NewLine;
            message += _userService._codeFormattingExample;
            await Context.Message.DeleteAsync();
            await ReplyAsync(message).DeleteAfterSeconds(240);
        }

        [Command("disablecodetips"),
         Summary("Prevents being reminded about using proper code formatting when code is detected. Syntax : !disablecodetips")]
        public async Task DisableCodeTips()
        {
            ulong userID = Context.User.Id;
            string replyMessage = "You've already told me to stop reminding you, don't worry, I won't forget!";

            if (!_userService.CodeReminderCooldown.IsPermanent(userID))
            {
                replyMessage = "I will no longer remind you about using proper code formatting.";
                _userService.CodeReminderCooldown.SetPermanent(Context.User.Id, true);
            }

            await ReplyAsync($"{Context.User.Username}, " + replyMessage).DeleteAfterTime(seconds: 20);
        }

        #endregion


        [Command("disablethanksreminder"),
         Summary("Prevents being reminded to mention the person you are thanking. Syntax : !disablethanksreminder")]
        public async Task DisableThanksReminder()
        {
            ulong userID = Context.User.Id;
            string replyMessage = "You've already told me to stop reminding you, don't worry, I won't forget!";

            if (!_userService.ThanksReminderCooldown.IsPermanent(userID))
            {
                replyMessage = "I will no longer remind you to mention the person you're thanking... (◕︿◕✿)";
                _userService.ThanksReminderCooldown.SetPermanent(Context.User.Id, true);
            }

            await Context.Message.DeleteAsync();
            await ReplyAsync($"{Context.User.Username}, " + replyMessage).DeleteAfterTime(seconds: 20);
        }


        [Command("quote"), Summary("Quote a message. Syntax : !quote messageid (#channelname) (optionalSubtitle)")]
        public async Task QuoteMessage(ulong id, IMessageChannel channel = null, string subtitle = null)
        {
            if (subtitle != null && (subtitle.Contains("@everyone") || subtitle.Contains("@here"))) return;
            // If channel is null use Context.Channel, else use the provided channel
            channel ??= Context.Channel;
            var message = await channel.GetMessageAsync(id);
            // Can't imagine we need to quote the bots
            if (message.Author.IsBot)
                return;
            string messageLink = "https://discordapp.com/channels/" + Context.Guild.Id + "/" + channel.Id + "/" + id;
            var msgContent = (message.Content == string.Empty ? "" : message.Content.Truncate(1020));

            string msgAttachment = string.Empty;
            if (message.Attachments?.Count > 0)
            {
                msgAttachment = $"\t📸";
            }
            var builder = new EmbedBuilder()
                .WithColor(new Color(200, 128, 128))
                .WithTimestamp(message.Timestamp)
                .WithFooter(footer =>
                {
                    footer
                        .WithText($"In channel {message.Channel.Name}");
                })
                .WithAuthor(author =>
                {
                    author
                        .WithName(message.Author.Username)
                        .WithIconUrl(message.Author.GetAvatarUrl());
                });
            var messageTitle = "Original message";
            if (msgContent == string.Empty)
            {
                messageTitle = $"~~{messageTitle}~~";
                if (msgAttachment != string.Empty)
                    msgContent = "📸";
            }
            builder.AddField(messageTitle, $"{msgContent}\n" +
                                           $"**Linkback**\t[__Message__]({messageLink})" +
                                           $"{msgAttachment}");
            var embed = builder.Build();
            await ReplyAsync(subtitle == null ? "" : $"`{Context.User.Username}:` {subtitle}", false, embed);
            await Context.Message.DeleteAfterSeconds(1.0);
        }

        [Command("compile"),
         Summary("Try to compile a snippet of C# code. Be sure to escape your strings. Syntax : !compile \"Your code\"")]
        [Alias("code", "compute", "assert")]
        public async Task CompileCode(params string[] code)
        {
            var codeComplete = Resources.PaizaCodeTemplate.Replace("{code}", string.Join(" ", code));

            var parameters = new Dictionary<string, string> {{"source_code", codeComplete}, {"language", "csharp"}, {"api_key", "guest"}};

            var content = new FormUrlEncodedContent(parameters);

            var message = await ReplyAsync(
                $"Please wait a moment, trying to compile your code interpreted as\n {codeComplete.AsCodeBlock()}");

            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage httpResponse = await client.PostAsync("http://api.paiza.io/runners/create", content);
                var response = JsonConvert.DeserializeObject<Dictionary<string, string>>(await httpResponse.Content.ReadAsStringAsync());

                string id = response["id"];
                string status;
                DateTime startTime = DateTime.Now;
                const int maxTime = 30;

                do
                {
                    httpResponse = await client.GetAsync($"http://api.paiza.io/runners/get_details?id={id}&api_key=guest");
                    response = JsonConvert.DeserializeObject<Dictionary<string, string>>(await httpResponse.Content.ReadAsStringAsync());
                    status = response["status"];
                    await Task.Delay(300);
                } while (status != "completed" && (DateTime.Now - startTime).TotalSeconds < maxTime);

                string newMessage;

                if (status != "completed")
                {
                    newMessage = (message.Content + "The code didn't compile in time.").Truncate(1990);
                    await message.ModifyAsync(m => m.Content = newMessage);
                    return;
                }

                string build_stddout = response["build_stdout"];
                string stdout = response["stdout"];
                string stderr = response["stderr"];
                string build_stderr = response["build_stderr"];
                string result = response["build_result"];

                string fullMessage;
                if (result == "failure")
                {
                    fullMessage = message.Content + "The code resulted in a failure.\n";
                    fullMessage += build_stddout.Length > 0 ? build_stddout.AsCodeBlock() : string.Empty;
                    fullMessage += build_stderr.Length > 0 ? build_stderr.AsCodeBlock() : string.Empty;
                }
                else
                {
                    fullMessage = message.Content + "Result : ";
                    fullMessage += stdout.Length > 0 ? stdout.AsCodeBlock() : string.Empty;
                    fullMessage += stderr.Length > 0 ? stderr.AsCodeBlock() : string.Empty;
                }

                httpResponse = await client.PostAsync("https://hastebin.com/documents", new StringContent(fullMessage.Truncate(10000)));
                response = JsonConvert.DeserializeObject<Dictionary<string, string>>(await httpResponse.Content.ReadAsStringAsync());

                newMessage = ($"\nFull result : https://hastebin.com/{response["key"]}\n" + fullMessage).Truncate(1990) + "```";
                await message.ModifyAsync(m => m.Content = newMessage);
            }
        }

        #region Fun

        [Command("slap"), Summary("Slap the specified user(s). Syntax : !slap @user1 [@user2 @user3...]")]
        public async Task SlapUser(params IUser[] users)
        {
            StringBuilder sb = new StringBuilder();
            string[] slaps = {"trout", "duck", "truck"};
            var random = new Random();

            sb.Append("**").Append(Context.User.Username).Append("** Slaps ");
            foreach (var user in users)
            {
                sb.Append(user.Mention).Append(" ");
            }

            sb.Append("around a bit with a large ").Append(slaps[random.Next() % 3]);

            await Context.Channel.SendMessageAsync(sb.ToString());
            await Task.Delay(1000);
            await Context.Message.DeleteAsync();
        }


        [Command("coinflip"), Summary("Flip a coin and see the result. Syntax : !coinflip")]
        [Alias("flipcoin")]
        public async Task CoinFlip()
        {
            Random rand = new Random();
            var coin = new[] {"Heads", "Tails"};

            await ReplyAsync($"**{Context.User.Username}** flipped a coin and got **{coin[rand.Next() % 2]}** !");
            await Task.Delay(1000);
            await Context.Message.DeleteAsync();
        }

        /* [Command("subtitle"), Summary("Add a subtitle to an image attached. Syntax : !subtitle \"Text to write\"")]
         [Alias("subtitles", "sub", "subs")]
         private async Task Subtitles(string text)
         {
             var msg = await _userService.SubtitleImage(Context.Message, text);
             if (msg.Length < 6)
                 await ReplyAsync("Sorry, there was an error processing your image.");
             else
                 await Context.Channel.SendFileAsync(msg, $"From {Context.Message.Author.Mention}");
             await Context.Message.DeleteAsync();
         }*/

        #endregion

        #region Publisher

        [Command("pinfo"), Summary("Information on how to get the publisher role. Syntax : !pinfo")]
        [Alias("publisherinfo")]
        public async Task PublisherInfo()
        {
            if (Context.Channel.Id != _settings.BotCommandsChannel.Id)
            {
                await Task.Delay(1000);
                await Context.Message.DeleteAsync();
                return;
            }

            await ReplyAsync($"\n" +
                             "**Publisher - BOT COMMANDS : ** ``these commands are not case-sensitive.``\n" +
                             "``!publisher ID`` - Your Publisher ID, assetstore.unity.com/publishers/yourID.\n" +
                             "``!verify publisherID verifCode`` - Verify your ID with the code sent to your email.");

            //x await ReplyAsync($"\n" +
            //x                  "**Publisher - BOT COMMANDS : ** ``these commands are not case-sensitive.``\n" +
            //x                  "``!pkg ID`` - To add your package to Publisher everyday Advertising , ID means the digits on your package link.\n" +
            //x                  "``!verify packageId verifCode`` - Verify your package with the code send to your email.");

            await Task.Delay(10000);
            await Context.Message.DeleteAsync();
        }

        [Command("publisher"),
         Summary("Get the Asset-Publisher role by verifying who you are. Syntax: !publisher publisherID")]
        public async Task Publisher(uint publisherId)
        {
            if (Context.Channel.Id != _settings.BotCommandsChannel.Id)
            {

                await ReplyAsync($"Please use the <#{_settings.BotCommandsChannel.Id}> channel!")
                    .DeleteAfterSeconds(2.0f);
                await Context.Message.DeleteAfterSeconds(1.0f);
                return;
            }
            if (_settings.Gmail == string.Empty)
            {
                await ReplyAsync("Asset Publisher role is currently disabled.").DeleteAfterSeconds(5f);
                return;
            }

            (bool, string) verify = await _publisherService.VerifyPublisher(publisherId, Context.User.Username);
            if (verify.Item1)
                await ReplyAsync(verify.Item2);
            else
            {
                await ReplyAsync(verify.Item2).DeleteAfterSeconds(2.0f);
                await Context.Message.DeleteAfterSeconds(1.0f);
            }
        }

        // No longer works due to change in Unity Store API
        //x [Command("pkg"), Summary("Add your published package to the daily advertising. Syntax : !pkg packageId")]
        //x [Alias("package")]
        //x private async Task Package(uint packageId)
        //x {
        //x     if (Context.Channel.Id != _settings.BotCommandsChannel.Id)
        //x     {
        //x         await Task.Delay(1000);
        //x         await Context.Message.DeleteAsync();
        //x         return;
        //x     }
        //x 
        //x     (bool, string) verif = await _publisherService.VerifyPackage(packageId);
        //x     await ReplyAsync(verif.Item2);
        //x }

        [Command("verify"), Summary("Verify a publisher with the code received by email. Syntax : !verify publisherId code")]
        public async Task VerifyPackage(uint packageId, string code)
        {
            if (Context.Channel.Id != _settings.BotCommandsChannel.Id)
            {
                await Task.Delay(1000);
                await Context.Message.DeleteAsync();
                return;
            }

            string verif = await _publisherService.ValidatePackageWithCode(Context.Message.Author, packageId, code);
            await ReplyAsync(verif);
        }

        #endregion

        #region Search

        [Command("search"), Summary("Searches on DuckDuckGo for web results. Syntax : !search \"query\" resNum site")]
        [Alias("s", "ddg")]
        public async Task SearchResults(string query, uint resNum = 3, string site = "")
        {
            // Cleaning inputs from user (maybe we can ban certain domains or keywords)
            resNum = resNum <= 5 ? resNum : 5;
            string searchQuery = "https://duckduckgo.com/html/?q=" + query.Replace(' ', '+');

            if (!site.Equals(""))
            {
                searchQuery += "+site:" + site;
            }

            HtmlDocument doc = new HtmlWeb().Load(searchQuery);
            int counter = 1;
            List<string> results = new List<string>();

            // XPath for DuckDuckGo as of 10/05/2018, if results stop showing up, check this first!
            foreach (HtmlNode row in doc.DocumentNode.SelectNodes("/html/body/div[1]/div[3]/div/div/div[*]/div/h2/a"))
            {
                // Check if we are within the allowed number of results and if the result is valid (i.e. no evil ads)
                if (counter <= resNum && IsValidResult(row))
                {
                    string title = WebUtility.UrlDecode(row.InnerText);
                    string url = WebUtility.UrlDecode(row.Attributes["href"].Value.Replace("/l/?kh=-1&amp;uddg=", ""));
                    string msg = "";

                    // Added line for pretty output
                    if (counter > 1)
                    {
                        msg += "──────────────────────────────────────────\n";
                    }

                    msg += counter + ". **" + title + "**\nRead More: " + url;
                    results.Add(msg);
                    counter++;
                }
            }

            // Send each result as separate message for embedding
            foreach (string msg in results)
            {
                await ReplyAsync(msg);
            }

            // Utility function for avoiding evil ads from DuckDuckGo
            bool IsValidResult(HtmlNode node)
            {
                return !node.Attributes["href"].Value.Contains("duckduckgo.com") &&
                       !node.Attributes["href"].Value.Contains("duck.co");
            }
        }

        [Command("manual"), Summary("Searches on Unity3D manual results. Syntax : !manual \"query\"")]
        public async Task SearchManual(params string[] queries)
        {
            // Download Unity3D Documentation Database (lol)

            // Calculate the closest match to the input query
            double minimumScore = double.MaxValue;
            string[] mostSimilarPage = null;
            string[][] pages = await _updateService.GetManualDatabase();
            string query = String.Join(" ", queries);
            foreach (string[] p in pages)
            {
                double curScore = CalculateScore(p[1], query);
                if (curScore < minimumScore)
                {
                    minimumScore = curScore;
                    mostSimilarPage = p;
                }
            }

            // If a page has been found (should be), return the message, else return information
            if (mostSimilarPage != null)
                await ReplyAsync($"** {mostSimilarPage[1]} **\nRead More: https://docs.unity3d.com/Manual/{mostSimilarPage[0]}.html");
            else
                await ReplyAsync("No Results Found.");
        }

        [Command("doc"), Summary("Searches on Unity3D API results. Syntax : !api \"query\"")]
        [Alias("ref", "reference", "api", "docs")]
        public async Task SearchApi(params string[] queries)
        {
            // Download Unity3D Documentation Database (lol)

            // Calculate the closest match to the input query
            double minimumScore = double.MaxValue;
            string[] mostSimilarPage = null;
            string[][] pages = await _updateService.GetApiDatabase();
            string query = String.Join(" ", queries);
            foreach (string[] p in pages)
            {
                double curScore = CalculateScore(p[1], query);
                if (curScore < minimumScore)
                {
                    minimumScore = curScore;
                    mostSimilarPage = p;
                }
            }

            // If a page has been found (should be), return the message, else return information
            if (mostSimilarPage != null)
                await ReplyAsync(
                    $"** {mostSimilarPage[1]} **\nRead More: https://docs.unity3d.com/ScriptReference/{mostSimilarPage[0]}.html");
            else
                await ReplyAsync("No Results Found.");
        }

        private double CalculateScore(string s1, string s2)
        {
            double curScore = 0;
            int i = 0;

            foreach (string q in s1.Split(' '))
            {
                foreach (string x in s2.Split(' '))
                {
                    i++;
                    if (x.Equals(q))
                        curScore -= 50;
                    else
                        curScore += x.CalculateLevenshteinDistance(q);
                }
            }

            curScore /= i;
            return curScore;
        }

        [Command("faq"), Summary("Searches UDH FAQs. Syntax : !faq \"query\"")]
        public async Task SearchFaqs(params string[] queries)
        {
            List<FaqData> faqDataList = _updateService.GetFaqData();

            // Check if query is faq ID (e.g. "!faq 1")
            if (queries.Length == 1 && ParseNumber(queries[0]) > 0)
            {
                int id = ParseNumber(queries[0]) - 1;
                if (id < faqDataList.Count)
                {
                    await ReplyAsync(embed: GetFaqEmbed(id + 1, faqDataList[id]));
                }
                else
                {
                    await ReplyAsync("Invalid FAQ ID selected.");
                }
            }
            // Check if query contains "list" command (i.e. "!faq list")
            else if (queries.Length > 0 && !(queries.Length == 1 && queries[0].Equals("list")))
            {
                // Calculate the closest match to the input query
                double minimumScore = double.MaxValue;
                FaqData mostSimilarFaq = null;
                string query = String.Join(" ", queries);
                int index = 1;
                int mostSimilarIndex = 0;

                // Go through each FAQ in the list and check the most similar
                foreach (FaqData faq in faqDataList)
                {
                    foreach (string keyword in faq.Keywords)
                    {
                        double curScore = CalculateScore(keyword, query);
                        if (curScore < minimumScore)
                        {
                            minimumScore = curScore;
                            mostSimilarFaq = faq;
                            mostSimilarIndex = index;
                        }
                    }

                    index++;
                }

                // If an FAQ has been found (should be), return the FAQ, else return information msg
                if (mostSimilarFaq != null)
                    await ReplyAsync(embed: GetFaqEmbed(mostSimilarIndex, mostSimilarFaq));
                else
                    await ReplyAsync("No FAQs Found.");
            }
            else
            {
                // List all the FAQs available
                ListFaqs(faqDataList);
            }
        }

        private async void ListFaqs(List<FaqData> faqs)
        {
            StringBuilder sb = new StringBuilder(faqs.Count);
            int index = 1;
            foreach (FaqData faq in faqs)
            {
                sb.Append(FormatFaq(index, faq) + "\n");
                string keywords = "[";
                for (int i = 0; i < faq.Keywords.Length; i++)
                {
                    keywords += faq.Keywords[i] + (i < faq.Keywords.Length - 1 ? ", " : "]\n\n");
                }

                index++;
                sb.Append(keywords);
            }

            await ReplyAsync(sb.ToString()).DeleteAfterTime(minutes: 3);
        }

        private Embed GetFaqEmbed(int id, FaqData faq)
        {
            var builder = new EmbedBuilder()
                .WithTitle($"{faq.Question}")
                .WithDescription($"{faq.Answer}")
                .WithColor(new Color(0x33CC00));
            return builder.Build();
        }

        private string FormatFaq(int id, FaqData faq)
        {
            return $"{id}. **{faq.Question}** - {faq.Answer}";
        }


        [Command("wiki"), Summary("Searches Wikipedia. Syntax : !wiki \"query\"")]
        [Alias("wikipedia")]
        public async Task SearchWikipedia([Remainder] string query)
        {
            (string name, string extract, string url) article = await _updateService.DownloadWikipediaArticle(query);

            // If an article is found return it, else return error message
            if (article.url == null)
            {
                await ReplyAsync($"No Articles for \"{query}\" were found.");
                return;
            }

            await ReplyAsync(embed: GetWikipediaEmbed(article.name, article.extract, article.url));
        }

        private Embed GetWikipediaEmbed(string subject, string articleExtract, string articleUrl)
        {
            var builder = new EmbedBuilder()
                .WithTitle($"Wikipedia | {subject}")
                .WithDescription($"{articleExtract}")
                .WithUrl(articleUrl)
                .WithColor(new Color(0x33CC00));
            return builder.Build();
        }

        private int ParseNumber(string s)
        {
            int id;
            if (int.TryParse(s, out id))
            {
                return id;
            }
            else
            {
                return -1;
            }
        }

        #endregion

        #region Birthday

        [Command("birthday"), Summary("Display next member birthday. Syntax : !birthday")]
        [Alias("bday")]
        public async Task Birthday()
        {
            // URL to cell C15/"Next birthday" cell from Corn's google sheet
            string nextBirthday =
                "https://docs.google.com/spreadsheets/d/10iGiKcrBl1fjoBNTzdtjEVYEgOfTveRXdI5cybRTnj4/gviz/tq?tqx=out:html&range=C15:C15";
            HtmlDocument doc = new HtmlWeb().Load(nextBirthday);

            // XPath to the table row
            HtmlNode row = doc.DocumentNode.SelectSingleNode("/html/body/table/tr[2]/td");
            string tableText = System.Net.WebUtility.HtmlDecode(row.InnerText);
            string message = $"**{tableText}**";

            await ReplyAsync(message).DeleteAfterTime(minutes: 3);
            await Context.Message.DeleteAfterTime(minutes: 3);
        }

        [Command("birthday"), Summary("Display birthday of mentioned user. Syntax : !birthday @user")]
        [Alias("bday")]
        public async Task Birthday(IUser user)
        {
            string searchName = user.Username;
            // URL to columns B to D of Corn's google sheet
            string birthdayTable =
                "https://docs.google.com/spreadsheets/d/10iGiKcrBl1fjoBNTzdtjEVYEgOfTveRXdI5cybRTnj4/gviz/tq?tqx=out:html&gid=318080247&range=B:D";
            HtmlDocument doc = new HtmlWeb().Load(birthdayTable);
            DateTime birthdate = default(DateTime);

            HtmlNode matchedNode = null;
            int matchedLength = int.MaxValue;

            // XPath to each table row
            foreach (HtmlNode row in doc.DocumentNode.SelectNodes("/html/body/table/tr"))
            {
                // XPath to the name column (C)
                HtmlNode nameNode = row.SelectSingleNode("td[2]");
                string name = nameNode.InnerText;
                if (name.ToLower().Contains(searchName.ToLower()))
                {
                    // Check for a "Closer" match
                    if (name.Length < matchedLength)
                    {
                        matchedNode = row;
                        matchedLength = name.Length;
                        // Nothing will match "Better" so we may as well break out
                        if (name.Length == searchName.Length)
                        {
                            break;
                        }
                    }
                }
            }

            if (matchedNode != null)
            {
                // XPath to the date column (B)
                HtmlNode dateNode = matchedNode.SelectSingleNode("td[1]");
                // XPath to the year column (D)
                HtmlNode yearNode = matchedNode.SelectSingleNode("td[3]");

                CultureInfo provider = CultureInfo.InvariantCulture;
                string wrongFormat = "M/d/yyyy";
                //string rightFormat = "dd-MMMM-yyyy";

                string dateString = dateNode.InnerText;
                if (!yearNode.InnerText.Contains("&nbsp;"))
                {
                    dateString = dateString + "/" + yearNode.InnerText;
                }

                dateString = dateString.Trim();

                try
                {
                    // Converting the birthdate from the wrong format to the right format WITH year
                    birthdate = DateTime.ParseExact(dateString, wrongFormat, provider);
                }
                catch (FormatException)
                {
                    // Converting the birthdate from the wrong format to the right format WITHOUT year
                    birthdate = DateTime.ParseExact(dateString, "M/d", provider);
                }
            }

            // Business as usual
            if (birthdate == default(DateTime))
            {
                await ReplyAsync(
                        $"Sorry, I couldn't find **{searchName}**'s birthday date. They can add it at https://docs.google.com/forms/d/e/1FAIpQLSfUglZtJ3pyMwhRk5jApYpvqT3EtKmLBXijCXYNwHY-v-lKxQ/viewform ! :stuck_out_tongue_winking_eye: ")
                    .DeleteAfterSeconds(30);
            }
            else
            {
                string message =
                    $"**{searchName}**'s birthdate: __**{birthdate.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture)}**__ " +
                    $"({(int) ((DateTime.Now - birthdate).TotalDays / 365)}yo)";

                await ReplyAsync(message).DeleteAfterTime(minutes: 3);
            }

            await Context.Message.DeleteAfterTime(minutes: 3);
        }

        #endregion

        #region temperatures

        [Command("ftoc"), Summary("Converts a temperature in fahrenheit to celsius. Syntax : !ftoc temperature")]
        public async Task FahrenheitToCelsius(float f)
        {
            await ReplyAsync($"{Context.User.Mention} {f}°F is {Math.Round((f - 32) * 0.555555f, 2)}°C.");
        }

        [Command("ctof"), Summary("Converts a temperature in celsius to fahrenheit. Syntax : !ftoc temperature")]
        public async Task CelsiusToFahrenheit(float c)
        {
            await ReplyAsync($"{Context.User.Mention}  {c}°C is {Math.Round(c * 1.8f + 32, 2)}°F");
        }

        #endregion

        #region Translate

        [Command("translate"), Summary("Translate a message. Syntax : !translate messageId language")]
        public async Task Translate(ulong id, string language = "en")
        {
            await Translate((await Context.Channel.GetMessageAsync(id)).Content, language);
        }

        [Command("translate"), Summary("Translate a message. Syntax : !translate text language")]
        public async Task Translate(string message, string language = "en")
        {
            await ReplyAsync($"Here: https://translate.google.com/#auto/{language}/{message.Replace(" ", "%20")}");
            await Task.Delay(1000);
            await Context.Message.DeleteAsync();
        }

        #endregion

        #region Currency

        [Command("currency"), Summary("Converts a currency. Syntax : !currency fromCurrency toCurrency")]
        [Alias("curr")]
        public async Task ConvertCurrency(string from, string to)
        {
            await ConvertCurrency(1, from, to);
        }

        [Command("currency"), Summary("Converts a currency. Syntax : !currency amount fromCurrency toCurrency")]
        [Alias("curr")]
        public async Task ConvertCurrency(double amount, string from, string to)
        {
            from = from.ToUpper();
            to = to.ToUpper();

            // Get USD to fromCurrency rate
            double fromRate = await _currencyService.GetRate(from);
            // Get USD to toCurrency rate
            double toRate = await _currencyService.GetRate(to);

            if (fromRate == -1 || toRate == -1)
            {
                await ReplyAsync(
                    $"{Context.User.Mention}, {from} or {to} are invalid currencies or I can't understand them.\nPlease use international currency code (example : **USD** for $, **EUR** for €, **PKR** for pakistani rupee).");
                return;
            }

            // Convert fromCurrency amount to USD to toCurrency
            double value = Math.Round((toRate / fromRate) * amount, 2);

            await ReplyAsync($"{Context.User.Mention}  **{amount} {from}** = **{value} {to}**");
        }

        #endregion

        [Command("ping"), Summary("Display bot ping. Syntax : !ping")]
        [Alias("pong")]
        public async Task Ping()
        {
            var message = await ReplyAsync($"Pong :blush:");
            var time = message.CreatedAt.Subtract(Context.Message.Timestamp);
            await message.ModifyAsync(m =>
                m.Content = $"Pong :blush: (**{time.TotalMilliseconds}** *ms* / gateway **{_userService.GetGatewayPing()}** *ms*)");
            await message.DeleteAfterTime(minutes: 3);


            await Context.Message.DeleteAfterTime(minutes: 3);
        }

        [Command("members"), Summary("Displays number of members Syntax : !members")]
        [Alias("MemberCount")]
        public async Task MemberCount()
        {
            await ReplyAsync(
                $"We currently have {(await Context.Guild.GetUsersAsync()).Count - 1} members. Let's keep on growing as the strong community we are :muscle:");
        }

        [Group("role")]
        public class RoleModule : ModuleBase
        {
            private readonly ILoggingService _logging;

            public RoleModule(ILoggingService logging)
            {
                _logging = logging;
            }

            [Command("add"), Summary("Add a role to yourself. Syntax : !role add role")]
            public async Task AddRoleUser(IRole role)
            {
                if (Context.Channel.Id != _settings.BotCommandsChannel.Id)
                {
                    await Task.Delay(1000);
                    await Context.Message.DeleteAsync();
                    return;
                }

                if (!_settings.AllRoles.Roles.Contains(role.Name))
                {
                    await ReplyAsync("This role is not assigneable");
                    return;
                }

                var u = Context.User as IGuildUser;

                await u.AddRoleAsync(role);
                await ReplyAsync($"{u.Username} you now have the role of `{role.Name}`");
                await _logging.LogAction($"{Context.User.Username} has added role {role} to himself in {Context.Channel.Name}");
            }

            [Command("remove"), Summary("Remove a role from yourself. Syntax : !role remove role")]
            [Alias("delete")]
            public async Task RemoveRoleUser(IRole role)
            {
                if (Context.Channel.Id != _settings.BotCommandsChannel.Id)
                {
                    await Task.Delay(1000);
                    await Context.Message.DeleteAsync();
                    return;
                }

                if (!_settings.AllRoles.Roles.Contains(role.Name))
                {
                    await ReplyAsync("Role is not assigneable");
                    return;
                }

                var u = Context.User as IGuildUser;

                await u.RemoveRoleAsync(role);
                await ReplyAsync($"{u.Username} your role of `{role.Name}` has been removed");
                await _logging.LogAction($"{Context.User.Username} has removed role {role} from himself in {Context.Channel.Name}");
            }

            [Command("list"), Summary("Display the list of roles. Syntax : !role list")]
            public async Task ListRole()
            {
                if (Context.Channel.Id != _settings.BotCommandsChannel.Id)
                {
                    await Task.Delay(1000);
                    await Context.Message.DeleteAsync();
                    return;
                }

                await ReplyAsync("**The following roles are available on this server** :\n" +
                                 "\n" +
                                 "We offer multiple roles to show what you specialize in, whether it's professionally or as a hobby, so if there's something you're good at, assign the corresponding role! \n" +
                                 "You can assign as much roles as you want, but try to keep them for what you're good at :) \n" +
                                 "\n" +
                                 "```To get the publisher role type **!pinfo** and follow the instructions." +
                                 "https://www.assetstore.unity3d.com/en/#!/search/page=1/sortby=popularity/query=publisher:1 <= Example Digits```\n");
                await ReplyAsync(
                    "```!role add/remove 2D-Artists - If you're good at drawing, painting, digital art, concept art or anything else that's flat. \n" +
                    "!role add/remove 3D-Artists - If you are a wizard with vertices or like to forge your models from mud. \n" +
                    "!role add/remove Animators - If you like to bring characters to life. \n" +
                    "!role add/remove Technical-Artists - If you write tools and shaders to bridge the gap between art and programming. \n" +
                    "!role add/remove Programmers - If you like typing away to make your dreams come true (or the code come to your dreams). \n" +
                    "!role add/remove Game-Designers - If you are good at designing games, mechanics and levels.\n" +
                    "!role add/remove Audio-Engineers - If you live life to the rhythm of your own music and sounds.\n" +
                    "!role add/remove Generalists - If you like to dabble in everything.\n" +
                    "!role add/remove Hobbyists - If you're using Unity as a hobby.\n" +
                    "!role add/remove Students - If you're currently studying in a gamedev related field. \n" +
                    "!role add/remove XR-Developers - If you're a VR, AR or MR sorcerer. \n" +
                    "!role add/remove Writers - If you like writing lore, scenarii, characters and stories. \n" +
                    "======Below are special roles that will get pinged for specific reasons====== \n" +
                    "!role add/remove Subs-Gamejam - Will be pinged when there is UDC gamejam related news. \n" +
                    "!role add/remove Subs-Poll - Will be pinged when there is new public polls. \n" +
                    "!role add/remove Subs-Releases - Will be pinged when there is new unity releases (beta and stable versions). \n" +
                    "!role add/remove Subs-News - Will be pinged when there is new unity news (mainly blog posts). \n" +
                    "```");
            }
        }
        
        [Command("ChristmasCompleted"), Summary("Gives rewards to people who complete the christmas event.")]
        public async Task UserCompleted(String message)
        {

            //Make sure they're the santa bot
            if (Context.Message.Author.Id != 514979161144557600L) {
                return;
            }

            long userId = 0;

            if (!long.TryParse(message, out userId)) {
                await ReplyAsync("Invalid user id");
                return;
            }

            int xpGain = 5000;

            _databaseService.AddUserXp((ulong)userId, xpGain);

            await Context.Message.DeleteAsync();
        }
    }
}