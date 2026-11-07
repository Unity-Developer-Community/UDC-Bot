using DiscordBot.Domain.UnityNews;
using DiscordBot.Services.Server;

namespace DiscordBot.Services.Code.Unity;

/// <summary>Builds the Components V2 messages of the Unity news posts.</summary>
public static class UnityNewsCards
{
    public const int MaxForumTitleLength = 100;

    public static readonly Color LtsColor = new(70, 130, 230);
    public static readonly Color SupportedColor = new(80, 190, 120);
    public static readonly Color BetaColor = new(240, 160, 60);
    public static readonly Color AlphaColor = new(170, 110, 220);

    public static Color AccentColor(UnityStream stream) => stream switch
    {
        UnityStream.Alpha => AlphaColor,
        UnityStream.Beta => BetaColor,
        UnityStream.Supported => SupportedColor,
        _ => LtsColor,
    };

    /// <summary>
    /// Mentions a post may ping: only <paramref name="roleId"/>, or none when it is null.
    /// Discord.Net rejects the <see cref="AllowedMentionTypes.Roles"/> flag together with a role id list, so only the list is set.
    /// </summary>
    public static AllowedMentions RoleOnlyMentions(ulong? roleId) =>
        roleId is { } id ? new AllowedMentions { RoleIds = [id] } : AllowedMentions.None;

    public const string ReleaseForumTag = "Unity Release";
    public const string ReleaseTitlePrefix = "Unity Release - ";

    /// <summary>Forum post title, for example <c>Unity Release - LTS · 6000.3.28f1</c>.</summary>
    public static string ForumTitle(UnityRelease release) => AuditCard.Fit(ReleaseTitlePrefix + release.Title, MaxForumTitleLength, "…");

    /// <summary>The forum tag names of a release post: the release tag and the stream's tag ("LTS", "Supported", "Beta" or "Alpha").</summary>
    public static IReadOnlyList<string> ReleaseTagNames(UnityRelease release) => [ReleaseForumTag, release.StreamLabel];

    /// <summary>
    /// The messages of a release post: the first one opens the forum thread, the others are replies.
    /// <paramref name="roleMention"/> (for example <c>&lt;@&amp;123&gt;</c>) is the first line of the first message; null adds none.
    /// </summary>
    public static IReadOnlyList<MessageComponent> ReleaseCards(ReleaseSummary summary, string? roleMention = null)
    {
        var messages = ReleaseSummaryFormatter.Format(summary, roleMention);
        var accent = AccentColor(summary.Latest.Stream);
        var cards = new List<MessageComponent>(messages.Count);
        for (var i = 0; i < messages.Count; i++)
            cards.Add(ReleaseCard(messages[i], accent, i == 0 ? summary.Latest.PageUrl : null));
        return cards;
    }

    private static MessageComponent ReleaseCard(ReleaseMessage message, Color accent, string? linkUrl)
    {
        var container = new ContainerBuilder().WithAccentColor(accent);
        var blocks = message.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            // Keeps a message under Discord's text limit even if a block grew past its budget.
            container.WithTextDisplay(AuditCard.Fit(blocks[i], ReleaseSummaryFormatter.MessageBudget));
            // The header of the first message is set apart from the sections.
            if (linkUrl != null && i == 0 && blocks.Count > 1)
                container.WithSeparator();
        }

        if (linkUrl != null)
            container.WithActionRow(new ActionRowBuilder().WithButton(ButtonBuilder.CreateLinkButton("Release notes", linkUrl)));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    #region Blog

    public const string BlogForumTag = "Unity Blog";
    public const string BlogTitlePrefix = "Blog - ";
    private const int BlogLedeLength = 300;
    private const int BlogFallbackLedeLength = 1000;
    private const int BlogChapterLabelLength = 70;
    private const int BlogChaptersBudget = 2200;
    private static readonly Color BlogColor = new(250, 250, 250);

    /// <summary>Forum post title of a blog post.</summary>
    public static string BlogForumTitle(string title) => AuditCard.Fit(BlogTitlePrefix + title.Trim(), MaxForumTitleLength, "…");

    /// <summary>The forum tag names of a blog post.</summary>
    public static IReadOnlyList<string> BlogTagNames() => [BlogForumTag];

    /// <summary>
    /// The message of a blog post. <paramref name="page"/> may be <see cref="BlogPage.Empty"/> (the page could not be read), which gives a card of the
    /// feed item alone. <paramref name="roleMention"/> (for example <c>&lt;@&amp;123&gt;</c>) is the first line; null adds none.
    /// </summary>
    public static MessageComponent BlogCard(string title, string url, string? description, DateTimeOffset published, BlogPage page, string? roleMention = null)
    {
        var container = new ContainerBuilder().WithAccentColor(BlogColor);
        var lede = Collapse(description);
        var header = new System.Text.StringBuilder();
        if (roleMention != null)
            header.Append(roleMention).Append('\n');
        header.Append("## [").Append(LinkLabel(title, 200)).Append("](").Append(LinkTarget(url)).Append(')');
        if (lede.Length > 0)
            header.Append("\n> ").Append(Safe(AuditCard.Fit(lede, page.IsEmpty ? BlogFallbackLedeLength : BlogLedeLength, "…")));
        container.WithTextDisplay(AuditCard.Fit(header.ToString(), 1500));

        var meta = BlogMeta(published, page);
        if (meta.Length > 0)
            container.WithTextDisplay(AuditCard.Fit(meta, 900));

        if (page.Images.Count > 0)
            container.WithMediaGallery(page.Images.Take(BlogPageParser.MaxImages).ToList());

        if (page.Chapters.Count >= 2)
        {
            var lines = new List<string> { "**In this post**" };
            for (var i = 0; i < page.Chapters.Count; i++)
                lines.Add($"{i + 1}. [{LinkLabel(page.Chapters[i], BlogChapterLabelLength)}]({LinkTarget(BlogLinks.ChapterUrl(url, page.Chapters[i]))})");
            container.WithTextDisplay(AuditCard.FitLines(lines, BlogChaptersBudget));
        }

        container.WithActionRow(new ActionRowBuilder().WithButton(ButtonBuilder.CreateLinkButton("Read on unity.com", url)));
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static string BlogMeta(DateTimeOffset published, BlogPage page)
    {
        var lines = new List<string>();
        if (page.Authors.Count > 0)
        {
            var first = page.Authors[0];
            var line = "**" + LinkLabel(first.Name, 80) + "**" + (first.Role == null ? "" : " · " + Safe(AuditCard.Fit(first.Role, 80, "…")));
            if (page.Authors.Count > 1)
                line += page.Authors.Count == 2 ? " and 1 other" : $" and {page.Authors.Count - 1} others";
            lines.Add(line);
        }

        // The page only has a day: noon UTC shows that day for most of the world's time zones.
        var day = page.Published ?? published;
        var facts = new List<string> { $"<t:{new DateTimeOffset(day.UtcDateTime.Date.AddHours(12), TimeSpan.Zero).ToUnixTimeSeconds()}:D>" };
        if (page.ReadMinutes is { } minutes)
            facts.Add($"{minutes} min read");
        lines.Add(string.Join(" · ", facts));

        if (page.Topics.Count > 0)
            lines.Add("**Topics:** " + string.Join(" · ", page.Topics.Select(t => Safe(t))));
        if (page.Tags.Count > 0)
            lines.Add("**Tags:** " + string.Join(" · ", page.Tags.Select(t => Safe(t))));
        return string.Join('\n', lines);
    }

    private static string Collapse(string? text) => string.IsNullOrWhiteSpace(text) ? "" : System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>Text for the label of a markdown link: no brackets, mentions defused.</summary>
    private static string LinkLabel(string text, int max) => Safe(AuditCard.Fit(Collapse(text), max, "…")).Replace("[", "(").Replace("]", ")");

    /// <summary>A URL for the target of a markdown link, where a parenthesis would end the link.</summary>
    private static string LinkTarget(string url) => url.Replace("(", "%28").Replace(")", "%29").Replace(" ", "%20");

    // Text of a web page is untrusted: keep it from pinging.
    private static string Safe(string text) => text
        .Replace("@everyone", "@\u200Beveryone", StringComparison.OrdinalIgnoreCase)
        .Replace("@here", "@\u200Bhere", StringComparison.OrdinalIgnoreCase)
        .Replace("<@", "<\u200B@", StringComparison.Ordinal);

    #endregion // Blog
}
