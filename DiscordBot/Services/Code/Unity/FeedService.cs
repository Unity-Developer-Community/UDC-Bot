using System.IO;
using System.ServiceModel.Syndication;
using System.Xml;
using Discord.WebSocket;
using DiscordBot.Domain.UnityNews;
using DiscordBot.Services.Server;
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

    /// <summary>Checks an unreadable article page is waited for (about an hour at one check per 10 minutes) before the feed-only card is posted: the forum post pings a role and can't be upgraded later.</summary>
    private const int MaxBlogPageAttempts = 6;

    /// <summary>Failed reads of an item's article page, by item id; in memory, like <see cref="_blogFailures"/>.</summary>
    private readonly Dictionary<string, int> _blogPageFailures = new();

    #endregion // Blog Feed Config

    #region Release Feed Config

    private const string ReleaseApiUrl = "https://services.api.unity.com/unity/editor/release/v1/releases?stream={0}&order=RELEASE_DATE_DESC&limit={1}";
    private const int ReleaseApiLimit = 25;

    private static readonly UnityStream[] ReleaseStreams = [UnityStream.Lts, UnityStream.Supported, UnityStream.Beta, UnityStream.Alpha];

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

    /// <summary>
    /// What the feeds track, for the startup report: <c>Releases (LTS, Supported, Beta, Alpha) → #releases,
    /// blog → #news; checks start &lt;t:…:R&gt;. Not seeded yet, so their first check posts nothing: Beta.</c>
    /// </summary>
    internal static string StartupLine(FeedData data, ulong releasesChannelId, ulong newsChannelId, DateTimeOffset firstCheck)
    {
        static string Channel(ulong id) => id == 0 ? "no channel set" : $"<#{id}>";
        static string Name(UnityStream stream) => stream == UnityStream.Lts ? "LTS" : stream.ToString();

        var line = $"Releases ({string.Join(", ", ReleaseStreams.Select(Name))}) → {Channel(releasesChannelId)}, " +
                   $"blog → {Channel(newsChannelId)}; checks start {AuditLogCards.Timestamp(firstCheck, 'R')}.";
        var unseeded = ReleaseStreams
            .Where(s => !(data.ReleaseStreams.TryGetValue(s.ToString(), out var state) && state.Seeded))
            .Select(Name)
            .ToList();
        if (!data.BlogSeeded)
            unseeded.Add("blog");
        if (unseeded.Count > 0)
            line += $" Not seeded yet, so their first check posts nothing: {string.Join(", ", unseeded)}.";
        return line;
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

    /// <summary>The forum's tags with these names (case-insensitive); names the forum has no tag for are skipped.</summary>
    private static ForumTag[] ForumTags(IForumChannel channel, IEnumerable<string> names) => names
        .Select(name => channel.Tags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        .OfType<ForumTag>()
        .DistinctBy(t => t.Id)
        .Take(MaxForumTags)
        .ToArray();

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
                    var url = BlogUrl(item);
                    var fetched = await GetBlogPage(url);
                    if (fetched.Failure != null)
                    {
                        var failedReads = _blogPageFailures.GetValueOrDefault(item.Id);
                        if (fetched.CanRetry && BlogLinks.DeferUnreadablePage(failedReads, MaxBlogPageAttempts))
                        {
                            _blogPageFailures[item.Id] = ++failedReads;
                            await _logging.LogChannelAndFile($"[{ServiceName}] Could not read the blog page {url} ({fetched.Failure}); attempt {failedReads}/{MaxBlogPageAttempts}, trying again next check", ExtendedLogSeverity.Warning);
                            continue;
                        }
                        await _logging.LogChannelAndFile($"[{ServiceName}] Could not read the blog page {url} ({fetched.Failure}); posting the card of the feed item alone", ExtendedLogSeverity.Warning);
                    }

                    await PostBlog(channel, item, roleId, fetched.Page);
                    // Only after the thread exists, so a failed post is retried on the next check.
                    feedData.AddPostedId(item.Id);
                    _blogFailures.Remove(item.Id);
                    _blogPageFailures.Remove(item.Id);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    var attempts = _blogFailures.GetValueOrDefault(item.Id) + 1;
                    _blogFailures[item.Id] = attempts;
                    if (attempts >= MaxBlogAttempts)
                    {
                        feedData.AddPostedId(item.Id);
                        _blogFailures.Remove(item.Id);
                        _blogPageFailures.Remove(item.Id);
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

    private static string BlogUrl(SyndicationItem item) => item.Links.FirstOrDefault()?.Uri?.AbsoluteUri ?? item.Id;

    private async Task PostBlog(IForumChannel channel, SyndicationItem item, ulong? roleId, BlogPage page)
    {
        var title = item.Title?.Text ?? item.Id;
        var url = BlogUrl(item);
        var description = item.Summary == null ? null : global::DiscordBot.Utils.Utils.RemoveHtmlTags(item.Summary.Text);

        // Blog posts ping the news role.
        var role = roleId is { } id ? _client.GetGuild(_settings.GuildId)?.GetRole(id) : null;
        var allowedMentions = UnityNewsCards.RoleOnlyMentions(role?.Id);

        var card = UnityNewsCards.BlogCard(title, url, description, item.PublishDate, page, role?.Mention);
        var tags = ForumTags(channel, UnityNewsCards.BlogTagNames());

        await channel.CreatePostAsync(UnityNewsCards.BlogForumTitle(title), ForumArchiveDuration, null, null, null, null,
            allowedMentions, card, flags: MessageFlags.ComponentsV2, tags: tags);
    }

    /// <summary>
    /// The article page read: <see cref="Page"/> holds the details, or <see cref="BlogPage.Empty"/> (a card of the feed item alone) when
    /// <see cref="Failure"/> says why it couldn't be fetched or read. <see cref="CanRetry"/> is false when another fetch can't help.
    /// </summary>
    private sealed record BlogPageResult(BlogPage Page, string? Failure = null, bool CanRetry = true);

    private async Task<BlogPageResult> GetBlogPage(string url)
    {
        try
        {
            // The URL comes from the feed: only fetch pages of unity.com.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || (uri.Host != "unity.com" && !uri.Host.EndsWith(".unity.com", StringComparison.Ordinal)))
                return new BlogPageResult(BlogPage.Empty, "not a unity.com page", CanRetry: false);

            var html = await _webClient.TryGetContent(url);
            if (html == null)
                return new BlogPageResult(BlogPage.Empty, "the request failed or the response was not a success status; see the console log for the HTTP status");

            var page = BlogPageParser.Parse(html);
            return page.IsEmpty
                ? new BlogPageResult(BlogPage.Empty, $"nothing could be read from the {html.Length} characters of the page")
                : new BlogPageResult(page);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new BlogPageResult(BlogPage.Empty, e.Message);
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
        var allowedMentions = UnityNewsCards.RoleOnlyMentions(role?.Id);

        var cards = UnityNewsCards.ReleaseCards(summary, role?.Mention);
        var tags = ForumTags(channel, UnityNewsCards.ReleaseTagNames(latest));

        var thread = await channel.CreatePostAsync(UnityNewsCards.ForumTitle(latest), ForumArchiveDuration, null, null, null, null,
            allowedMentions, cards[0], flags: MessageFlags.ComponentsV2, tags: tags);

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