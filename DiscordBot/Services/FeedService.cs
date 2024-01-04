using System.IO;
using System.ServiceModel.Syndication;
using System.Xml;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class FeedService
{
    private const string ServiceName = "FeedService";
    private readonly DiscordSocketClient _client;
    
    private readonly BotSettings _settings;
    private readonly ILoggingService _logging;

    #region Configurable Settings

    #region News Feed Config

    private class ForumNewsFeed
    {
        public string TitleFormat { get; set; }
        public string Url { get; set; }
        public List<string> IncludeTags { get; set; }
        public bool IncludeSummary { get; set; } = false;
    }

    private readonly ForumNewsFeed _betaNews = new()
    {
        TitleFormat = "Beta Release - {0}",
        Url = "https://unity3d.com/unity/beta/latest.xml",
        IncludeTags = new(){ "Beta Update" }
    };
    private readonly ForumNewsFeed _releaseNews = new()
    {
        TitleFormat = "New Release - {0}",
        Url = "https://unity3d.com/unity/releases.xml",
        IncludeTags = new(){"New Release"}
    };
    private readonly ForumNewsFeed _blogNews = new()
    {
        TitleFormat = "Blog - {0}",
        Url = "https://blogs.unity3d.com/feed/",
        IncludeTags = new() { "Unity Blog" },
        IncludeSummary = true
    };
    
    #endregion // News Feed Config
    
    // We store the title of the last 40 posts, and check against them to prevent duplicate posts
    private const int MaxHistoryCheck = 40;
    private readonly List<string> _postedFeeds = new( MaxHistoryCheck );
    
    private const int MaximumCheck = 3;
    private const ThreadArchiveDuration ForumArchiveDuration = ThreadArchiveDuration.OneWeek;

    #endregion // Configurable Settings
    
    public FeedService(DiscordSocketClient client, BotSettings settings, ILoggingService logging)
    {
        _client = client;
        _settings = settings;
        _logging = logging;
    }
    
    private async Task<SyndicationFeed> GetFeedData(string url)
    {
        SyndicationFeed feed = null;
        try
        {
            var content = await Utils.WebUtil.GetXMLContent(url);
            var reader = XmlReader.Create(new StringReader(content));
            feed = SyndicationFeed.Load(reader);
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole( $"[{ServiceName} Feed failure: {e.ToString()}", ExtendedLogSeverity.LowWarning);
        }

        // Return the feed, empty feed if null to prevent additional checks for null on return
        return feed ??= new SyndicationFeed();
    }

    #region Feed Handlers

    private async Task HandleFeed(FeedData feedData, ForumNewsFeed newsFeed, ulong channelId, ulong? roleId)
    {
        try
        {
            var feed = await GetFeedData(newsFeed.Url);
            var channel = _client.GetChannel(channelId) as IForumChannel;
            if (channel == null)
            {
                await _logging.LogAction($"[{ServiceName}] Error: Channel {channelId} not found", true, true);
                LoggingService.LogToConsole($"[{ServiceName}] Error: Channel {channelId} not found", LogSeverity.Error);
                return;
            }
            foreach (var item in feed.Items.Take(MaximumCheck))
            {
                if (feedData.PostedIds.Contains(item.Id))
                    continue;
                feedData.PostedIds.Add(item.Id);

                // Title
                var newsTitle = string.Format(newsFeed.TitleFormat, item.Title.Text);
                if (newsTitle.Length > 90)
                    newsTitle = newsTitle[..90] + "...";
                
                // Confirm we haven't posted this title before
                if (_postedFeeds.Contains(newsTitle))
                    continue;
                _postedFeeds.Add(newsTitle);
                if (_postedFeeds.Count > MaxHistoryCheck)
                    _postedFeeds.RemoveAt(0);

                // Message
                string newsContent = string.Empty;
                if (newsFeed.IncludeSummary)
                {
                    var summary = Utils.Utils.RemoveHtmlTags(item.Summary.Text);
                    newsContent = "**__Summary__**\n" + summary;
                    // Unlikely to be over, but we need space for extra local info
                    if (newsContent.Length > Constants.MaxLengthChannelMessage - 400)
                        newsContent = newsContent[..(Constants.MaxLengthChannelMessage - 400)] + "...";
                }
                // If a role is provided we add to end of title to ping the role
                var role = _client.GetGuild(_settings.GuildId).GetRole(roleId ?? 0);
                if (role != null)
                    newsContent = $"{(newsContent.Length > 0 ? $"{newsContent}\n" : "")}{role.Mention}";
                // Link to post
                if (item.Links.Count > 0)
                    newsContent += $"\n\n**__Source__**\n{item.Links[0].Uri}";
                
                // The Post
                var post = await channel.CreatePostAsync(newsTitle, ForumArchiveDuration, null, newsContent, null, null, AllowedMentions.All);
                // If any tags, include them
                if (newsFeed.IncludeTags is { Count: > 0 })
                {
                    var includedTags = new List<ulong>();
                    foreach (var tag in newsFeed.IncludeTags)
                    {
                        var tagContainer = channel.Tags.FirstOrDefault(x => x.Name == tag);
                        if (tagContainer != null)
                            includedTags.Add(tagContainer.Id);
                    }

                    await post.ModifyAsync(properties => { properties.AppliedTags = includedTags; });
                }
            }
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole(e.ToString(), LogSeverity.Error);
            await _logging.LogAction($"[{ServiceName}] Error: {e.ToString()}", true, true);
        }
    }

    #endregion // Feed Handlers

    #region Public Feed Actions

    public async Task CheckUnityBetasAsync(FeedData feedData)
    {
        await HandleFeed(feedData, _betaNews, _settings.UnityReleasesChannel.Id, _settings.SubsReleasesRoleId);
    }

    public async Task CheckUnityReleasesAsync(FeedData feedData)
    {
        await HandleFeed(feedData, _releaseNews, _settings.UnityReleasesChannel.Id, _settings.SubsReleasesRoleId);
    }

    public async Task CheckUnityBlogAsync(FeedData feedData)
    {
        await HandleFeed(feedData, _blogNews, _settings.UnityNewsChannel.Id, _settings.SubsNewsRoleId);
    }

    #endregion // Feed Actions
}