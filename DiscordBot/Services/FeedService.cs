using System;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Discord.WebSocket;

namespace DiscordBot.Services
{
    public class FeedService
    {
        private const string BetaUrl = "https://unity3d.com/unity/beta/latest.xml";
        private const string ReleaseUrl = "https://unity3d.com/unity/releases.xml";
        private const string BlogUrl = "https://blogs.unity3d.com/feed/";

        private const int MaximumCheck = 3;
        private readonly DiscordSocketClient _client;

        private readonly Settings.Deserialized.Settings _settings;

        public FeedService(DiscordSocketClient client, Settings.Deserialized.Settings settings)
        {
            _settings = settings;
            _client = client;
        }

        public async Task HandleFeed(FeedData feedData, string url, ulong channelId, ulong? roleId, string message, bool createThread = false)
        {
            try
            {
                var webRequest = (HttpWebRequest)WebRequest.Create(url);
                var webResponse = (HttpWebResponse)webRequest.GetResponse();
                var dataStream = webResponse.GetResponseStream();
                var streamReader = new StreamReader(dataStream, Encoding.UTF8);
                var response = streamReader.ReadToEnd();
                streamReader.Close();
                response = Utils.Utils.SanitizeXml(response);
                XmlReader reader = new XmlTextReader(new StringReader(response));

                var feed = SyndicationFeed.Load(reader);
                var channel = _client.GetChannel(channelId) as ISocketMessageChannel;
                foreach (var item in feed.Items.Take(MaximumCheck))
                    if (!feedData.PostedIds.Contains(item.Id))
                    {
                        feedData.PostedIds.Add(item.Id);

                        var messageToSend = string.Format(message, item.Title.Text, item.Links[0].Uri);

                        var role = _client.GetGuild(_settings.GuildId).GetRole(roleId ?? 0);
                        var wasRoleMentionable = false;
                        if (role != null)
                        {
                            wasRoleMentionable = role.IsMentionable;
                            await role.ModifyAsync(properties => { properties.Mentionable = true; });
                            messageToSend = $"{role.Mention} {messageToSend}";
                        }

                        var postedMessage = await channel.SendMessageAsync(messageToSend);
                        if (channel is SocketNewsChannel) await postedMessage.CrosspostAsync();

                        if (role != null) await role.ModifyAsync(properties => { properties.Mentionable = wasRoleMentionable; });

                        if (createThread)
                        {
                            var threadTitle = $"{item.Title.Text} - Discussion";
                            SocketThreadChannel thread = null;
                            try
                            {
                                // This will fail is the server is not boost level 1
                                thread = await (channel as SocketTextChannel).CreateThreadAsync(threadTitle, Discord.ThreadType.NewsThread, Discord.ThreadArchiveDuration.ThreeDays, postedMessage);
                            }
                            catch (Exception)
                            {
                                thread = await (channel as SocketTextChannel).CreateThreadAsync(threadTitle, Discord.ThreadType.NewsThread, Discord.ThreadArchiveDuration.OneDay, postedMessage);
                            }
                            var summary = Regex.Replace(item.Summary.Text, "<.*?>", String.Empty);
                            var firstThreadPost = string.Format("Summary: \n>>> {0}", summary);
                            await thread.SendMessageAsync(firstThreadPost);
                        }
                    }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public async Task CheckUnityBetasAsync(FeedData feedData)
        {
            await HandleFeed(feedData, BetaUrl, _settings.UnityReleasesChannel.Id, _settings.SubsReleasesRoleId, "New unity **beta **release !** {0}** \n <{1}>");
        }

        public async Task CheckUnityReleasesAsync(FeedData feedData)
        {
            await HandleFeed(feedData, ReleaseUrl, _settings.UnityReleasesChannel.Id, _settings.SubsReleasesRoleId, "New unity release ! **{0}** \n <{1}>");
        }

        public async Task CheckUnityBlogAsync(FeedData feedData)
        {
            await HandleFeed(feedData, BlogUrl, _settings.UnityNewsChannel.Id, _settings.SubsNewsRoleId, "New unity blog post ! **{0}**\n{1}", true);
        }
    }
}