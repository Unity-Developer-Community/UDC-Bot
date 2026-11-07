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

    #region Blog Feed Config

    private const string BlogFeedUrl = "https://unity.com/blog/rss";

    /// <summary>The most blog posts created per check; the rest follows on the next one.</summary>
    private const int MaxBlogPostsPerCheck = 5;

    /// <summary>Failed attempts after which a blog post is given up (marked posted) so one bad item cannot repeat errors forever.</summary>
    private const int MaxBlogAttempts = 3;

    private readonly Dictionary<string, int> _blogFailures = new();

    #endregion // Blog Feed Config

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

    private const ThreadArchiveDuration ForumArchiveDuration = ThreadArchiveDuration.OneWeek;

    /// <summary>Discord allows at most 5 applied tags on a forum post.</summary>
    private const int MaxForumTags = 5;

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

    private async Task HandleBlog(FeedData feedData, ulong channelId, ulong? roleId)
    {
        try
        {
            var feed = await GetFeedData(BlogFeedUrl);
            var items = feed.Items.Where(i => !string.IsNullOrEmpty(i.Id)).ToList();
            // A failed fetch gives an empty feed: nothing to post and nothing to seed from.
            if (items.Count == 0)
                return;

            // The first check only remembers what is there, so a new feed URL or an old state file never floods the channel.
            if (!feedData.BlogSeeded)
            {
                foreach (var item in items.Where(i => !feedData.PostedIds.Contains(i.Id)))
                    feedData.AddPostedId(item.Id);
                feedData.BlogSeeded = true;
                await _logging.LogAction($"[{ServiceName}] Seeded the blog feed with {items.Count} posts; they are not posted.", ExtendedLogSeverity.Info);
                return;
            }

            var pending = BlogLinks.PendingPosts(items, i => i.Id, i => i.PublishDate, feedData.PostedIds, MaxBlogPostsPerCheck);
            if (pending.Count == 0)
                return;

            if (_client.GetChannel(channelId) is not IForumChannel channel)
            {
                await _logging.LogAction($"[{ServiceName}] Error: Channel {channelId} not found", ExtendedLogSeverity.Error);
                return;
            }

            foreach (var item in pending)
            {
                try
                {
                    await PostBlog(channel, item, roleId);
                    // Only after the thread exists, so a failed post is retried on the next check.
                    feedData.AddPostedId(item.Id);
                    _blogFailures.Remove(item.Id);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    var attempts = _blogFailures.GetValueOrDefault(item.Id) + 1;
                    _blogFailures[item.Id] = attempts;
                    if (attempts >= MaxBlogAttempts)
                    {
                        feedData.AddPostedId(item.Id);
                        _blogFailures.Remove(item.Id);
                    }
                    await _logging.LogAction($"[{ServiceName}] Error posting blog '{item.Title?.Text}' (attempt {attempts}): {e}", ExtendedLogSeverity.Error);
                }
            }
        }
        catch (Exception e)
        {
            await _logging.LogAction($"[{ServiceName}] Error: {e}", ExtendedLogSeverity.Error);
        }
    }

    private async Task PostBlog(IForumChannel channel, SyndicationItem item, ulong? roleId)
    {
        var title = item.Title?.Text ?? item.Id;
        var url = item.Links.FirstOrDefault()?.Uri?.AbsoluteUri ?? item.Id;
        var description = item.Summary == null ? null : global::DiscordBot.Utils.Utils.RemoveHtmlTags(item.Summary.Text);
        var page = await GetBlogPage(url);

        // Blog posts ping the news role.
        var role = roleId is { } id ? _client.GetGuild(_settings.GuildId)?.GetRole(id) : null;
        var allowedMentions = role == null
            ? AllowedMentions.None
            : new AllowedMentions(AllowedMentionTypes.Roles) { RoleIds = [role.Id] };

        var card = UnityNewsCards.BlogCard(title, url, description, item.PublishDate, page, role?.Mention);
        var wanted = UnityNewsCards.BlogTagNames(page);
        var tags = wanted
            .Select(name => channel.Tags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            .OfType<ForumTag>()
            .DistinctBy(t => t.Id)
            .Take(MaxForumTags)
            .ToArray();

        await channel.CreatePostAsync(UnityNewsCards.BlogForumTitle(title), ForumArchiveDuration, null, null, null, null,
            allowedMentions, card, flags: MessageFlags.ComponentsV2, tags: tags);
    }

    /// <summary>The details read from the article page; <see cref="BlogPage.Empty"/> (a card of the feed item alone) when it can't be fetched or read.</summary>
    private async Task<BlogPage> GetBlogPage(string url)
    {
        try
        {
            // The URL comes from the feed: only fetch pages of unity.com.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || (uri.Host != "unity.com" && !uri.Host.EndsWith(".unity.com", StringComparison.Ordinal)))
                throw new InvalidOperationException("The article is not a unity.com page.");

            var page = BlogPageParser.Parse(await _webClient.GetContent(url));
            if (page.IsEmpty)
                throw new InvalidOperationException("Nothing could be read from the article page.");
            return page;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await _logging.LogChannelAndFile($"[{ServiceName}] Could not read the blog page {url}: {e.Message}", ExtendedLogSeverity.Warning);
            return BlogPage.Empty;
        }
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
        await HandleBlog(feedData, _settings.Channels.UnityNews.Id, _settings.Roles.SubsNews);
    }

    #endregion // Feed Actions
}