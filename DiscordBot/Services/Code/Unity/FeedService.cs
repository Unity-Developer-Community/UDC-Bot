using System.IO;
using System.ServiceModel.Syndication;
using System.Xml;
using Discord.WebSocket;
using DiscordBot.Domain.UnityNews;
using DiscordBot.Settings;
using DiscordBot.Utils;

namespace DiscordBot.Services.Code.Unity;

public class FeedService
{
    private const string ServiceName = "FeedService";
    private readonly DiscordSocketClient _client;

    private readonly BotSettings _settings;
    private readonly ILoggingService _logging;
    private readonly IWebClient _webClient;

    #region Configurable Settings

    private const int MaxFeedLengthBuffer = 400;
    #region News Feed Config

    private class ForumNewsFeed
    {
        public string TitleFormat { get; set; } = null!;
        public string Url { get; set; } = null!;
        public List<string> IncludeTags { get; set; } = null!;
    }

    private readonly ForumNewsFeed _blogNews = new()
    {
        TitleFormat = "Blog - {0}",
        Url = "https://blogs.unity3d.com/feed/",
        IncludeTags = new() { "Unity Blog" }
    };

    #endregion // News Feed Config

    #region Release Feed Config

    private const string ReleaseApiUrl = "https://services.api.unity.com/unity/editor/release/v1/releases?stream={0}&order=RELEASE_DATE_DESC&limit={1}";
    private const int ReleaseApiLimit = 25;

    private static readonly UnityStream[] ReleaseStreams = [UnityStream.Lts, UnityStream.Supported, UnityStream.Beta, UnityStream.Alpha];

    /// <summary>The forum tag applied to a stream's posts; tags missing from the forum are skipped.</summary>
    private static string ForumTagName(UnityStream stream) => stream switch
    {
        UnityStream.Lts => "New Release",
        UnityStream.Beta => "Beta Update",
        UnityStream.Alpha => "Alpha",
        _ => "Supported",
    };

    #endregion // Release Feed Config

    private const int MaximumCheck = 3;
    private const ThreadArchiveDuration ForumArchiveDuration = ThreadArchiveDuration.OneWeek;

    #endregion // Configurable Settings

    public FeedService(DiscordSocketClient client, BotSettings settings, ILoggingService logging, IWebClient webClient)
    {
        _client = client;
        _settings = settings;
        _logging = logging;
        _webClient = webClient;
    }

    private async Task<SyndicationFeed> GetFeedData(string url)
    {
        SyndicationFeed? feed = null;
        try
        {
            var content = await _webClient.GetXMLContent(url);
            var reader = XmlReader.Create(new StringReader(content));
            feed = SyndicationFeed.Load(reader);
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[{ServiceName} Feed failure: {e.ToString()}", ExtendedLogSeverity.LowWarning);
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
                feedData.AddPostedId(item.Id);

                // Title
                var newsTitle = string.Format(newsFeed.TitleFormat, item.Title.Text);
                if (newsTitle.Length > 90)
                    newsTitle = newsTitle[..90] + "...";

                // Message
                var newsContent = GetSummary(newsFeed, item);

                // If a role is provided we add to end of title to ping the role
                var role = _client.GetGuild(_settings.GuildId).GetRole(roleId ?? 0);
                if (role != null)
                    newsContent += $"\n{role.Mention}";
                // Link to post
                if (item.Links.Count > 0)
                    newsContent += $"\n\n**__Source__**\n{item.Links[0].Uri}";

                newsContent = newsContent.SanitizeEveryoneHereMentions();

                // The Post
                var post = await channel.CreatePostAsync(newsTitle, ForumArchiveDuration, null, newsContent, null, null, AllowedMentions.All);
                await AddTagsToPost(channel, post, newsFeed.IncludeTags);
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
        var summary = global::DiscordBot.Utils.Utils.RemoveHtmlTags(item.Summary.Text);

        // If it is too long, we truncate it
        var summaryLength = summary.Length;
        if (summaryLength > Constants.MaxLengthChannelMessage - MaxFeedLengthBuffer)
            summary = summary[..(Constants.MaxLengthChannelMessage - MaxFeedLengthBuffer)] + "...";
        return summary;
    }

    #endregion // Feed Handlers

    #region Release Handlers

    private async Task HandleReleaseStream(FeedData feedData, UnityStream stream, DateTimeOffset now)
    {
        var page = await _webClient.GetObjectFromJson<UnityReleaseApiPage>(string.Format(ReleaseApiUrl, stream.ToString().ToUpperInvariant(), ReleaseApiLimit));
        var releases = page?.ToReleases();
        if (releases == null || releases.Count == 0)
        {
            LoggingService.LogToConsole($"[{ServiceName}] No {stream} releases returned by the release API.", ExtendedLogSeverity.LowWarning);
            return;
        }

        var key = stream.ToString();
        if (!feedData.ReleaseStreams.TryGetValue(key, out var state))
            feedData.ReleaseStreams[key] = state = new ReleaseStreamState();

        var plan = ReleaseSchedule.Plan(stream, state, releases, now);
        if (plan.Seed)
        {
            ReleaseSchedule.Seed(stream, state, releases, now);
            await _logging.LogAction($"[{ServiceName}] Seeded the {stream} release stream; the existing releases are not posted.", ExtendedLogSeverity.Info);
            return;
        }
        if (plan.Posts.Count == 0)
            return;

        if (_client.GetChannel(_settings.Channels.UnityReleases.Id) is not IForumChannel channel)
        {
            await _logging.LogAction($"[{ServiceName}] Error: Channel {_settings.Channels.UnityReleases.Id} not found", ExtendedLogSeverity.Error);
            return;
        }

        foreach (var post in plan.Posts)
        {
            try
            {
                await PostRelease(channel, post);
                // Only after the thread exists, so a failed post is retried on the next check.
                ReleaseSchedule.MarkPosted(state, post, now);
            }
            catch (Exception e)
            {
                await _logging.LogAction($"[{ServiceName}] Error posting {post.Latest.Title}: {e}", ExtendedLogSeverity.Error);
            }
        }
    }

    /// <summary>Creates the forum post for a release (or digest); a failing reply is logged and does not fail the post.</summary>
    private async Task PostRelease(IForumChannel channel, ReleasePost post)
    {
        var latest = post.Latest;
        var items = new List<(UnityRelease, ReleaseNotes)>();
        foreach (var release in post.Releases)
            items.Add((release, await GetReleaseNotes(release)));
        var summary = ReleaseSummary.Merge(items);

        // Only LTS posts ping the releases role.
        IRole? role = null;
        if (latest.Stream == UnityStream.Lts && _settings.Roles.SubsReleases is { } roleId)
            role = _client.GetGuild(_settings.GuildId)?.GetRole(roleId);
        var allowedMentions = role == null
            ? AllowedMentions.None
            : new AllowedMentions(AllowedMentionTypes.Roles) { RoleIds = [role.Id] };

        var cards = UnityNewsCards.ReleaseCards(summary, role?.Mention);
        var tag = channel.Tags.Where(t => t.Name == ForumTagName(latest.Stream)).ToArray();

        var thread = await channel.CreatePostAsync(UnityNewsCards.ForumTitle(latest), ForumArchiveDuration, null, null, null, null,
            allowedMentions, cards[0], flags: MessageFlags.ComponentsV2, tags: tag);

        try
        {
            for (var i = 1; i < cards.Count; i++)
                await thread.SendMessageAsync(components: cards[i], flags: MessageFlags.ComponentsV2, allowedMentions: AllowedMentions.None);
        }
        catch (Exception e)
        {
            await _logging.LogAction($"[{ServiceName}] Error sending replies of {latest.Title}: {e}", ExtendedLogSeverity.Warning);
        }
    }

    /// <summary>The parsed notes of a release; empty (a header-only post) when they can't be fetched or parsed.</summary>
    private async Task<ReleaseNotes> GetReleaseNotes(UnityRelease release)
    {
        try
        {
            if (string.IsNullOrEmpty(release.NotesUrl))
                throw new InvalidOperationException("The release has no notes URL.");
            var markdown = await _webClient.GetContent(release.NotesUrl);
            if (string.IsNullOrWhiteSpace(markdown))
                throw new InvalidOperationException("The release notes are empty or could not be fetched.");
            return ReleaseNotesMarkdownParser.Parse(markdown);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await _logging.LogChannelAndFile($"[{ServiceName}] Could not read the release notes of {release.Version}: {e.Message}", ExtendedLogSeverity.Warning);
            return ReleaseNotes.Empty;
        }
    }

    #endregion // Release Handlers

    #region Public Feed Actions

    /// <summary>Checks every Unity release stream and posts what its schedule says is due.</summary>
    public async Task CheckUnityReleasesAsync(FeedData feedData)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var stream in ReleaseStreams)
        {
            try
            {
                await HandleReleaseStream(feedData, stream, now);
            }
            catch (Exception e)
            {
                await _logging.LogAction($"[{ServiceName}] Error handling {stream} releases: {e}", ExtendedLogSeverity.Error);
            }
        }
    }

    public async Task CheckUnityBlogAsync(FeedData feedData)
    {
        await HandleFeed(feedData, _blogNews, _settings.Channels.UnityNews.Id, _settings.Roles.SubsNews);
    }

    #endregion // Feed Actions
}