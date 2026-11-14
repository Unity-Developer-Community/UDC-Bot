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

    /// <summary>Serializes the changes to the blog card state in <see cref="FeedData"/>.</summary>
    private readonly SemaphoreSlim _discussionGate = new(1, 1);

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
    /// blog → #news, discussion cards → #talk; checks start &lt;t:…:R&gt;. Not seeded yet, so their first check posts nothing: Beta.</c>
    /// <paramref name="discussionChannelId"/> is null when the blog discussion card is off.
    /// </summary>
    internal static string StartupLine(FeedData data, ulong releasesChannelId, ulong newsChannelId, ulong? discussionChannelId, DateTimeOffset firstCheck)
    {
        static string Channel(ulong id) => id == 0 ? "no channel set" : $"<#{id}>";
        static string Name(UnityStream stream) => stream == UnityStream.Lts ? "LTS" : stream.ToString();

        var line = $"Releases ({string.Join(", ", ReleaseStreams.Select(Name))}) → {Channel(releasesChannelId)}, " +
                   $"blog → {Channel(newsChannelId)}, " +
                   $"discussion cards → {(discussionChannelId is { } discussion ? Channel(discussion) : "off")}; checks start {AuditLogCards.Timestamp(firstCheck, 'R')}.";
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

                    var thread = await PostBlog(channel, item, roleId, fetched.Page);
                    // Only after the thread exists, so a failed post is retried on the next check.
                    feedData.AddPostedId(item.Id);
                    _blogFailures.Remove(item.Id);
                    _blogPageFailures.Remove(item.Id);

                    await RecordBlogPost(feedData, item, fetched.Page, thread);
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

    private static string? BlogDescription(SyndicationItem item) =>
        item.Summary == null ? null : global::DiscordBot.Utils.Utils.RemoveHtmlTags(item.Summary.Text);

    private async Task<IThreadChannel> PostBlog(IForumChannel channel, SyndicationItem item, ulong? roleId, BlogPage page)
    {
        var title = item.Title?.Text ?? item.Id;
        var url = BlogUrl(item);
        var description = BlogDescription(item);

        // Blog posts ping the news role.
        var role = roleId is { } id ? _client.GetGuild(_settings.GuildId)?.GetRole(id) : null;
        var allowedMentions = UnityNewsCards.RoleOnlyMentions(role?.Id);

        var card = UnityNewsCards.BlogCard(title, url, description, item.PublishDate, page, role?.Mention);
        var tags = ForumTags(channel, UnityNewsCards.BlogTagNames());

        return await channel.CreatePostAsync(UnityNewsCards.BlogForumTitle(title), ForumArchiveDuration, null, null, null, null,
            allowedMentions, card, flags: MessageFlags.ComponentsV2, tags: tags);
    }

    /// <summary>True when blog cards go to the discussion channel: the setting is on and a channel is set.</summary>
    private bool DiscussionCardsOn => _settings.UnityNews.BlogDiscussionCard && (_settings.Channels.UnityDiscussion?.Id ?? 0) != 0;

    /// <summary>
    /// Remembers a forum post as the latest blog post and, when discussion cards are on, queues its card for
    /// <see cref="DeliverDiscussionCardsAsync"/>, which waits for the discussion channel to go quiet.
    /// </summary>
    private async Task RecordBlogPost(FeedData feedData, SyndicationItem item, BlogPage page, IThreadChannel thread)
    {
        var card = new BlogCardData
        {
            Id = item.Id,
            Title = item.Title?.Text ?? item.Id,
            Url = BlogUrl(item),
            Description = BlogDescription(item),
            Images = page.Images.ToList(),
            ForumPostUrl = $"https://discord.com/channels/{_settings.GuildId}/{thread.Id}",
            PostedUtc = DateTimeOffset.UtcNow,
        };

        await _discussionGate.WaitAsync();
        try
        {
            feedData.LatestBlog = card;
            if (DiscussionCardsOn)
                feedData.PendingDiscussionCards.Add(card);
        }
        finally
        {
            _discussionGate.Release();
        }
    }

    /// <summary>
    /// Posts the queued blog cards to the discussion channel once no one has talked there for <see cref="BlogDiscussion.IdleTime"/>
    /// (or a card has waited <see cref="BlogDiscussion.MaxWait"/>), so a card doesn't cut into a conversation. Runs every feed loop.
    /// </summary>
    public async Task DeliverDiscussionCardsAsync(FeedData feedData)
    {
        if (feedData.PendingDiscussionCards.Count == 0)
            return;

        await _discussionGate.WaitAsync();
        try
        {
            if (feedData.PendingDiscussionCards.Count == 0)
                return;

            var channel = DiscussionCardsOn ? _client.GetChannel(_settings.Channels.UnityDiscussion!.Id) as IMessageChannel : null;
            if (channel == null)
            {
                await _logging.LogAction($"[{ServiceName}] Blog discussion cards are off or their channel was not found; dropping {feedData.PendingDiscussionCards.Count} queued card(s)", ExtendedLogSeverity.Warning);
                feedData.PendingDiscussionCards.Clear();
                return;
            }

            var queuedAt = feedData.PendingDiscussionCards.Min(c => c.PostedUtc);
            if (!BlogDiscussion.CardDue(DateTimeOffset.UtcNow, queuedAt, await LastHumanMessageAt(channel)))
                return;

            await SendQueuedDiscussionCards(feedData, channel);
        }
        finally
        {
            _discussionGate.Release();
        }
    }

    /// <summary>When a human last wrote among the channel's newest messages; null when none did, or the history can't be read (then the card is posted).</summary>
    private async Task<DateTimeOffset?> LastHumanMessageAt(IMessageChannel channel)
    {
        try
        {
            var messages = await channel.GetMessagesAsync(BlogDiscussion.HistoryScanLimit).FlattenAsync();
            return messages.Where(m => !m.Author.IsBot && !m.Author.IsWebhook)
                .Select(m => (DateTimeOffset?)m.Timestamp)
                .Max();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await _logging.LogAction($"[{ServiceName}] Could not read the discussion channel's history, so the blog card is posted without waiting: {e.Message}", ExtendedLogSeverity.Warning);
            return null;
        }
    }

    /// <summary>Sends every queued card, oldest first, and empties the queue. Call inside <see cref="_discussionGate"/>.</summary>
    private async Task SendQueuedDiscussionCards(FeedData feedData, IMessageChannel channel)
    {
        foreach (var card in feedData.PendingDiscussionCards.ToList())
        {
            await SendDiscussionCard(feedData, channel, card);
            feedData.PendingDiscussionCards.Remove(card);
        }
    }

    /// <summary>
    /// Sends one blog card to the discussion channel and remembers it as the last one. A failure is logged and not retried,
    /// because the forum post is the record. Call inside <see cref="_discussionGate"/>.
    /// </summary>
    private async Task SendDiscussionCard(FeedData feedData, IMessageChannel channel, BlogCardData card)
    {
        try
        {
            var components = UnityNewsCards.BlogDiscussionCard(card.Title, card.Url, card.Description, card.Images, card.ForumPostUrl);
            var message = await channel.SendMessageAsync(components: components, flags: MessageFlags.ComponentsV2, allowedMentions: AllowedMentions.None);
            feedData.LastDiscussionCardMessageId = message.Id;
            feedData.LastDiscussionCardUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await _logging.LogAction($"[{ServiceName}] Could not post the discussion card for '{card.Title}': {e.Message}", ExtendedLogSeverity.Warning);
        }
    }

    /// <summary>How long the "card is just above" reply to <c>!blog</c> stays.</summary>
    private static readonly TimeSpan BlogCommandReplyLifetime = TimeSpan.FromSeconds(15);

    /// <summary>
    /// <c>!blog</c>. In the discussion channel it posts the latest blog card (at once when it is still queued), unless the last card was
    /// sent under <see cref="BlogDiscussion.RepostCooldown"/> ago or is among the channel's newest messages; then it points to that card.
    /// Elsewhere, and before any blog post is known, it posts the small pointer card to the news channel.
    /// </summary>
    public async Task BlogCommandAsync(FeedData? feedData, IMessageChannel channel)
    {
        var latest = feedData?.LatestBlog;
        if (feedData == null || latest == null || !DiscussionCardsOn || channel.Id != _settings.Channels.UnityDiscussion!.Id)
        {
            var pointer = UnityNewsCards.BlogPointerCard(_settings.Channels.UnityNews.Id, latest?.ForumPostUrl, latest?.PostedUtc);
            await channel.SendMessageAsync(components: pointer, flags: MessageFlags.ComponentsV2, allowedMentions: AllowedMentions.None);
            return;
        }

        await _discussionGate.WaitAsync();
        try
        {
            // Someone asked for it, so a card still waiting for a quiet channel goes out now.
            if (feedData.PendingDiscussionCards.Count > 0)
            {
                await SendQueuedDiscussionCards(feedData, channel);
                return;
            }

            var recent = (await channel.GetMessagesAsync(BlogDiscussion.RepostRecentMessages).FlattenAsync()).Select(m => m.Id);
            if (BlogDiscussion.RepostAllowed(DateTimeOffset.UtcNow, feedData.LastDiscussionCardUtc, feedData.LastDiscussionCardMessageId, recent))
            {
                await SendDiscussionCard(feedData, channel, latest);
                return;
            }

            var lastCard = $"https://discord.com/channels/{_settings.GuildId}/{channel.Id}/{feedData.LastDiscussionCardMessageId}";
            await channel.SendMessageAsync($"The latest blog post was shared {AuditLogCards.Timestamp(feedData.LastDiscussionCardUtc, 'R')}: {lastCard}",
                allowedMentions: AllowedMentions.None).DeleteAfterSeconds(BlogCommandReplyLifetime.TotalSeconds)!;
        }
        finally
        {
            _discussionGate.Release();
        }
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

    /// <summary>
    /// Adds the blog news role (<see cref="RoleSettings.SubsNews"/>) to a member who lacks it and removes it from one who has it,
    /// for the "Subscribe / Unsubscribe" button of a blog card. Returns what to tell the member; nothing is posted.
    /// </summary>
    public async Task<string> ToggleNewsRoleAsync(IGuildUser user)
    {
        var role = _client.GetGuild(_settings.GuildId)?.GetRole(_settings.Roles.SubsNews);
        if (role == null)
            return "The blog role isn't set up on this server.";

        try
        {
            if (user.RoleIds.Contains(role.Id))
            {
                await user.RemoveRoleAsync(role);
                return $"You're unsubscribed: you won't be pinged for new Unity blog posts (**{role.Name}** removed).";
            }

            await user.AddRoleAsync(role);
            return $"You're subscribed: you'll be pinged for new Unity blog posts (**{role.Name}** added).";
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await _logging.LogAction($"[{ServiceName}] Could not toggle the news role {role.Id} for {user.Id}: {e.Message}", ExtendedLogSeverity.Warning);
            return "I couldn't change your role. Please try again later or ask a moderator.";
        }
    }

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