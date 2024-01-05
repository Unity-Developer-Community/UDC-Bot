using System.IO;
using System.ServiceModel.Syndication;
using System.Xml;
using Discord.WebSocket;
using DiscordBot.Settings;
using HtmlAgilityPack;

namespace DiscordBot.Services;

public class FeedService
{
    private const string ServiceName = "FeedService";
    private readonly DiscordSocketClient _client;
    
    private readonly BotSettings _settings;
    private readonly ILoggingService _logging;

    #region Configurable Settings

    private const int MaxFeedLengthBuffer = 400;
    #region News Feed Config

    private class ForumNewsFeed
    {
        public string TitleFormat { get; set; }
        public string Url { get; set; }
        public List<string> IncludeTags { get; set; }
        public bool IsRelease { get; set; } = false;
    }

    private readonly ForumNewsFeed _betaNews = new()
    {
        TitleFormat = "Beta Release - {0}",
        Url = "https://unity3d.com/unity/beta/latest.xml",
        IncludeTags = new(){ "Beta Update" },
        IsRelease = true
    };
    private readonly ForumNewsFeed _releaseNews = new()
    {
        TitleFormat = "New Release - {0}",
        Url = "https://unity3d.com/unity/releases.xml",
        IncludeTags = new(){"New Release"},
        IsRelease = true
    };
    private readonly ForumNewsFeed _blogNews = new()
    {
        TitleFormat = "Blog - {0}",
        Url = "https://blogs.unity3d.com/feed/",
        IncludeTags = new() { "Unity Blog" },
        IsRelease = false
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
            if (_client.GetChannel(channelId) is not IForumChannel channel)
            {
                await _logging.LogAction($"[{ServiceName}] Error: Channel {channelId} not found", ExtendedLogSeverity.Error);
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
                var newsContent = string.Empty;
                List<string> releaseNotes = new();
                if (!newsFeed.IsRelease)
                    newsContent = GetSummary(newsFeed, item);
                else
                {
                    releaseNotes = GetReleaseNotes(item);
                    newsContent = releaseNotes[0];
                }
                
                // If a role is provided we add to end of title to ping the role
                var role = _client.GetGuild(_settings.GuildId).GetRole(roleId ?? 0);
                if (role != null)
                    newsContent += $"\n{role.Mention}";
                // Link to post
                if (item.Links.Count > 0)
                    newsContent += $"\n\n**__Source__**\n{item.Links[0].Uri}";
                
                // The Post
                var post = await channel.CreatePostAsync(newsTitle, ForumArchiveDuration, null, newsContent, null, null, AllowedMentions.All);
                await AddTagsToPost(channel, post, newsFeed.IncludeTags);

                if (releaseNotes.Count == 1)
                    continue;
                
                // post a new message for each release note after the first
                for (int i = 1; i < releaseNotes.Count; i++)
                {
                    if (releaseNotes[i].Length == 0)
                        continue;
                    await post.SendMessageAsync(releaseNotes[i]);
                }
            }
        }
        catch (Exception e)
        {
            await _logging.LogAction($"[{ServiceName}] Error: {e}", ExtendedLogSeverity.Error);
        }
    }
    
    private async Task AddTagsToPost(IForumChannel channel, IThreadChannel post, List<string> tags)
    {
        if (tags.Count <= 0)
            return;
        
        var includedTags = new List<ulong>();
        foreach (var tag in tags)
        {
            var tagContainer = channel.Tags.FirstOrDefault(x => x.Name == tag);
            if (tagContainer != null)
                includedTags.Add(tagContainer.Id);
        }

        await post.ModifyAsync(properties => { properties.AppliedTags = includedTags; });
    }

    private string GetSummary(ForumNewsFeed feed, SyndicationItem item)
    {
        var summary = Utils.Utils.RemoveHtmlTags(item.Summary.Text);

        // If it is too long, we truncate it
        var summaryLength = summary.Length;
        if (summaryLength > Constants.MaxLengthChannelMessage - MaxFeedLengthBuffer)
            summary = summary[..(Constants.MaxLengthChannelMessage - MaxFeedLengthBuffer)] + "...";
        return summary;
    }

    private List<string> GetReleaseNotes(SyndicationItem item)
    {
        List<string> releaseNotes = new();
        var summary = string.Empty;

        var htmlDoc = new HtmlDocument();
        var summaryText = item.Summary.Text;
        
        summaryText = summaryText.Replace("&#x2192;", "->");
        // TODO : (James) Likely other entities we need to replace
        
        htmlDoc.LoadHtml(summaryText);

        // Find "release-notes"
        var summaryNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='release-notes']");
        if (summaryNode == null)
            return new List<string>() { "No release notes found" };

        try
        {
            // Find "Known Issues"
            var knownIssueNode = summaryNode.ChildNodes.FirstOrDefault(x => x.Name == "h3" && x.InnerText.Contains("Known Issues"))?.NextSibling;
            var entriesSinceNode = summaryNode.ChildNodes.FirstOrDefault(x => x.Name == "h3" && x.InnerText.Contains("Entries since"));

            // Find the features node which will be a h4 heading with content "Features"
            var featuresNode = summaryNode.ChildNodes.FirstOrDefault(x => x.Name == "h4" && x.InnerText == "Features")?.NextSibling;
            var improvementsNode = summaryNode.ChildNodes.FirstOrDefault(x => x.Name == "h4" && x.InnerText == "Improvements")?.NextSibling;
            var apiChangesNode = summaryNode.ChildNodes.FirstOrDefault(x => x.Name == "h4" && x.InnerText == "API Changes")?.NextSibling;
            var changesNode = summaryNode.ChildNodes.FirstOrDefault(x => x.Name == "h4" && x.InnerText == "Changes")?.NextSibling;
            var fixesNode = summaryNode.ChildNodes.FirstOrDefault(x => x.Name == "h4" && x.InnerText == "Fixes")?.NextSibling;
            var packagesUpdatedNode = summaryNode.ChildNodes.FirstOrDefault(x => x.Name == "h4" && x.InnerText.ToLower().Contains("package changes"))?.NextSibling.NextSibling.NextSibling;

            // Need to construct the summary which is just a stats summary
            summary += $"**Summary**\n";
            summary += GetNodeLiCountString("Known Issues", knownIssueNode?.NextSibling);

            if (entriesSinceNode != null)
                summary += $"__{entriesSinceNode.InnerText}__\n\n";

            // Construct Stat Summary
            summary += GetNodeLiCountString("Features", featuresNode?.NextSibling);
            summary += GetNodeLiCountString("Improvements", improvementsNode?.NextSibling);
            summary += GetNodeLiCountString("API Changes", apiChangesNode?.NextSibling);
            summary += GetNodeLiCountString("Changes", changesNode?.NextSibling);
            summary += GetNodeLiCountString("Fixes", fixesNode?.NextSibling);
            summary += GetNodeLiCountString("Packages Updated", packagesUpdatedNode?.NextSibling);

            // Add Package Updates to Summary
            releaseNotes.Add(BuildReleaseNote("Packages Updated", packagesUpdatedNode, summary));

            // Features, Improvements
            releaseNotes.Add(BuildReleaseNote("Features", featuresNode));
            releaseNotes.Add(BuildReleaseNote("Improvements", improvementsNode, "", 1000));
            // API Changes, Changes + Fixes
            releaseNotes.Add(BuildReleaseNote("API Changes", apiChangesNode));
            releaseNotes.Add(BuildReleaseNote("Changes", changesNode));
            releaseNotes.Add(BuildReleaseNote("Fixes", fixesNode, ""));

            // Known Issues
            releaseNotes.Add(BuildReleaseNote("Known Issues", knownIssueNode, "", 1200));

            return releaseNotes;
        }
        catch (Exception e)
        {
            _logging.LogChannelAndFile($"[{ServiceName}] Error generating release notes: {e}\nLikely updated format.", ExtendedLogSeverity.Warning);
            // We ignore anything we've generated and return a "No release notes found" to maintain appearance
            return new List<string>() { "No release notes found" };
        }
    }

    private string BuildReleaseNote(string title, HtmlNode node, string contents = "", int maxLength = Constants.MaxLengthChannelMessage - MaxFeedLengthBuffer)
    {
        if (node == null) 
            return string.Empty;
        
        // If we pass in contents, we prepend it to the summary
        var summary = $"{(contents.Length > 0 ? $"{contents}\n" : string.Empty)}**{node.PreviousSibling.InnerText}**\n";
        
        bool needsExtraProcessing = title is "Fixes" or "Known Issues" or "API Changes";

        foreach (var feature in node.NextSibling.ChildNodes.Where(x => x.Name == "li"))
        {
            var extraText = string.Empty;
            if (needsExtraProcessing)
            {
                var nodeContents = feature.ChildNodes[0];
                // Remove \n if any
                nodeContents.InnerHtml = nodeContents.InnerHtml.Replace("\n", " ");
                
                var linkNode = nodeContents.SelectSingleNode("a");
                if (linkNode != null)
                {
                    nodeContents = nodeContents.RemoveChild(linkNode);
                    // Need to remove ()
                    feature.InnerHtml = feature.InnerHtml.Replace("()", "");
                    
                    // Add link to extraText, but use the InnerText as the text, and format so discord will use it as link
                    extraText = $" ([{linkNode.InnerText}](<{linkNode.Attributes["href"].Value}>))";
                }
            }
            
            summary += $"- {feature.InnerText}{extraText}\n";
            if (summary.Length > maxLength)
            {
                // Trim down to the last full line, that is less than limits
                var lastLine = summary[..maxLength].LastIndexOf('\n');
                summary = summary[..lastLine] + $"\n{title} truncated...\n";
                return summary;
            }
        }
        return summary;
    }
    
    private string GetNodeLiCountString(string title, HtmlNode node)
    {
        if (node == null)
            return string.Empty;
        
        var count = node.ChildNodes.Count(x => x.Name == "li");
        return $"{title}: {count}\n";
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