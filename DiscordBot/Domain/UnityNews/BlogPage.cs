using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Domain.UnityNews;

/// <summary>An author of a blog post.</summary>
public sealed record BlogAuthor(string Name, string? Role);

/// <summary>What the bot reads from a unity.com blog article page. Every field is optional: pages change shape.</summary>
public sealed record BlogPage
{
    /// <summary>Average reading speed used when the page states no read time.</summary>
    public const int WordsPerMinute = 230;

    public static readonly BlogPage Empty = new();

    public string? Headline { get; init; }
    public IReadOnlyList<BlogAuthor> Authors { get; init; } = [];

    /// <summary>The publication day (the page has no time of day).</summary>
    public DateTimeOffset? Published { get; init; }

    /// <summary>The read time the page states, when it does.</summary>
    public int? DeclaredReadMinutes { get; init; }

    public int WordCount { get; init; }
    public IReadOnlyList<string> Topics { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The heading texts to offer as chapter links, in page order.</summary>
    public IReadOnlyList<string> Chapters { get; init; } = [];

    /// <summary>Image URLs resized for Discord, the hero first.</summary>
    public IReadOnlyList<string> Images { get; init; } = [];

    /// <summary>The stated read time, else an estimate from the word count; null when neither is known.</summary>
    public int? ReadMinutes => DeclaredReadMinutes ?? (WordCount > 0 ? Math.Max(1, (int)Math.Round(WordCount / (double)WordsPerMinute)) : null);

    /// <summary>True when nothing useful was read, so the card falls back to the feed item alone.</summary>
    public bool IsEmpty => Headline == null && Authors.Count == 0 && Published == null && DeclaredReadMinutes == null && Topics.Count == 0
        && Tags.Count == 0 && Chapters.Count == 0 && Images.Count == 0;
}

/// <summary>Reads a <see cref="BlogPage"/> from the HTML of a unity.com article (untrusted input; never throws).</summary>
public static partial class BlogPageParser
{
    public const int MaxChapters = 8;
    public const int MaxImages = 4;

    private const string ImageHost = "https://cdn.sanity.io/images/";
    private const string ImageSuffix = "?w=1280&auto=format";
    private const int MinImageSide = 200;
    private const int MaxAvatarAttributeSize = 100;
    private const string FlightPrefix = "self.__next_f.push(";

    [GeneratedRegex(@"-(\d+)x(\d+)\.[A-Za-z0-9]+$")]
    private static partial Regex ImageSizeRegex();

    public static BlogPage Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return BlogPage.Empty;

        HtmlDocument doc;
        try
        {
            doc = new HtmlDocument();
            doc.LoadHtml(html);
        }
        catch (Exception)
        {
            return BlogPage.Empty;
        }

        var ld = Safe(() => ReadJsonLd(doc));
        var header = Safe(() => ReadHeader(doc));
        var body = Safe(() => ReadBody(doc, header?.AvatarBases ?? [], ld?.Images.FirstOrDefault() ?? header?.HeroImage));

        var authors = header is { Authors.Count: > 0 } ? header.Authors : (ld?.AuthorNames ?? []).Select(n => new BlogAuthor(n, null)).ToList();
        var published = ld?.Published ?? header?.Published;
        var hero = ld?.Images.FirstOrDefault() ?? header?.HeroImage;

        return new BlogPage
        {
            Headline = ld?.Headline ?? header?.Title,
            Authors = authors,
            Published = published,
            DeclaredReadMinutes = header?.ReadMinutes,
            WordCount = body?.WordCount ?? 0,
            Topics = header?.Topics ?? [],
            Tags = header?.Tags ?? [],
            Chapters = body?.Chapters ?? [],
            Images = BuildImages(hero, body?.Images ?? []),
        };
    }

    private static T? Safe<T>(Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    #region JSON-LD

    private sealed record LdData(string? Headline, IReadOnlyList<string> AuthorNames, DateTimeOffset? Published, IReadOnlyList<string> Images);

    private static LdData? ReadJsonLd(HtmlDocument doc)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts == null)
            return null;

        foreach (var script in scripts)
        {
            JToken token;
            try
            {
                token = JToken.Parse(script.InnerText);
            }
            catch (JsonException)
            {
                continue;
            }

            var posting = Flatten(token).FirstOrDefault(IsBlogPosting);
            if (posting == null)
                continue;

            return new LdData(
                Text(posting["headline"]),
                Items(posting["author"]).Select(a => a.Type == JTokenType.String ? Text(a) : Text(a["name"])).OfType<string>().ToList(),
                ParseDate(Text(posting["datePublished"])),
                Items(posting["image"]).Select(i => i.Type == JTokenType.String ? Text(i) : Text(i["url"])).OfType<string>().ToList());
        }
        return null;
    }

    private static IEnumerable<JObject> Flatten(JToken token)
    {
        switch (token)
        {
            case JObject obj:
                yield return obj;
                if (obj["@graph"] is JArray graph)
                    foreach (var inner in graph.SelectMany(Flatten))
                        yield return inner;
                break;
            case JArray array:
                foreach (var inner in array.SelectMany(Flatten))
                    yield return inner;
                break;
        }
    }

    private static bool IsBlogPosting(JObject obj) => obj["@type"] switch
    {
        JArray types => types.Any(t => (string?)t == "BlogPosting"),
        { } type => (string?)type == "BlogPosting",
        _ => false,
    };

    /// <summary>A JSON value that may be a single item or a list.</summary>
    private static IEnumerable<JToken> Items(JToken? token)
    {
        if (token is JArray array)
            return array;
        return token == null ? Enumerable.Empty<JToken>() : new[] { token };
    }

    #endregion // JSON-LD

    #region Page header (Next.js flight data)

    private sealed record HeaderData(
        string? Title,
        IReadOnlyList<BlogAuthor> Authors,
        DateTimeOffset? Published,
        int? ReadMinutes,
        IReadOnlyList<string> Topics,
        IReadOnlyList<string> Tags,
        string? HeroImage,
        IReadOnlyList<string> AvatarBases);

    /// <summary>
    /// The page header (title, authors with role, topics, tags, read time) only exists as JSON inside the script tags Next.js
    /// uses to hydrate the page: the strings of every <c>self.__next_f.push([1,"..."])</c> joined are the flight stream.
    /// </summary>
    private static HeaderData? ReadHeader(HtmlDocument doc)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script");
        if (scripts == null)
            return null;

        var stream = new StringBuilder();
        var pushes = scripts
            .Select(script => script.InnerText.Trim())
            .Where(code => code.StartsWith(FlightPrefix, StringComparison.Ordinal));
        foreach (var code in pushes)
        {
            try
            {
                var argument = code[FlightPrefix.Length..].TrimEnd(';').TrimEnd();
                if (argument.EndsWith(')'))
                    argument = argument[..^1];
                if (JArray.Parse(argument) is [{ Type: JTokenType.Integer } kind, { Type: JTokenType.String } chunk] && (int)kind == 1)
                    stream.Append((string?)chunk);
            }
            catch (JsonException)
            {
                // A chunk that is not a string push (for example the bootstrap) is not needed.
            }
        }

        var text = stream.ToString();
        var marker = text.IndexOf("\"readDuration\"", StringComparison.Ordinal);
        if (marker < 0)
            return null;
        var start = text.LastIndexOf("{\"backButton\"", marker, StringComparison.Ordinal);
        if (start < 0)
            return null;

        using var reader = new JsonTextReader(new StringReader(text[start..])) { DateParseHandling = DateParseHandling.None };
        var header = JObject.Load(reader);

        var authors = new List<BlogAuthor>();
        var avatars = new List<string>();
        foreach (var author in Items(header["authors"]).OfType<JObject>())
        {
            if (Text(author["name"]) is not { } name)
                continue;
            authors.Add(new BlogAuthor(name, Text(author["role"])));
            if (Text(author["avatar"]) is { } avatar)
                avatars.Add(ImageBase(avatar));
        }

        int? minutes = header["readDuration"] is JObject { } duration && duration["minutes"] is { Type: JTokenType.Integer } value && (int)value > 0
            ? (int)value
            : null;

        return new HeaderData(
            Text(header["title"]),
            authors,
            ParseDate(Text(header["date"])),
            minutes,
            Labels(header["topics"]),
            Labels(header["tags"]),
            header["heroImage"] is JObject hero ? Text(hero["src"]) : null,
            avatars);
    }

    private static List<string> Labels(JToken? token) => Items(token)
        .OfType<JObject>()
        .Select(o => Text(o["label"]))
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    #endregion // Page header

    #region Article body

    private sealed record BodyData(int WordCount, IReadOnlyList<string> Chapters, IReadOnlyList<string> Images);

    private static BodyData ReadBody(HtmlDocument doc, IReadOnlyList<string> avatarBases, string? hero)
    {
        var words = 0;
        var headings = new List<(int Level, string Text)>();
        var blocks = doc.DocumentNode.SelectNodes("//div[contains(concat(' ', normalize-space(@class), ' '), ' portable-text-base ')]");
        // A rich text block inside another one is already counted with its parent.
        var topBlocks = (blocks ?? Enumerable.Empty<HtmlNode>()).Where(block => !block.Ancestors("div").Any(IsRichText));
        foreach (var block in topBlocks)
        {
            words += Regex.Matches(HtmlEntity.DeEntitize(block.InnerText), @"\S+").Count;
            foreach (var heading in block.Descendants().Where(n => n.Name is "h2" or "h3" or "h4" or "h5" or "h6"))
            {
                var title = Clean(heading.InnerText);
                if (title.Length > 0)
                    headings.Add((heading.Name[1] - '0', title));
            }
        }

        // The page uses whichever level the author picked, so the highest one present is the chapter level.
        var chapters = headings.Count == 0
            ? []
            : headings.Where(h => h.Level == headings.Min(x => x.Level))
                .Select(h => h.Text)
                .Distinct(StringComparer.Ordinal)
                .Take(MaxChapters)
                .ToList();

        var heroBase = hero == null ? null : ImageBase(hero);
        var images = new List<string>();
        foreach (var img in doc.DocumentNode.Descendants("img"))
        {
            var src = HtmlEntity.DeEntitize(img.GetAttributeValue("src", ""));
            if (!src.StartsWith(ImageHost, StringComparison.Ordinal))
                continue;
            var imageBase = ImageBase(src);
            if (imageBase == heroBase || avatarBases.Contains(imageBase) || images.Contains(imageBase) || IsSmallImage(img, imageBase))
                continue;
            images.Add(imageBase);
        }

        return new BodyData(words, chapters, images);
    }

    private static bool IsRichText(HtmlNode node) => (" " + node.GetAttributeValue("class", "") + " ").Contains(" portable-text-base ", StringComparison.Ordinal);

    /// <summary>Avatars and icons: a small declared size, or a small size in the file name (<c>-96x96.png</c>).</summary>
    private static bool IsSmallImage(HtmlNode img, string imageBase)
    {
        if (int.TryParse(img.GetAttributeValue("width", ""), out var width) && width <= MaxAvatarAttributeSize)
            return true;
        var match = ImageSizeRegex().Match(imageBase);
        return match.Success && (int.Parse(match.Groups[1].Value) < MinImageSide || int.Parse(match.Groups[2].Value) < MinImageSide);
    }

    private static List<string> BuildImages(string? hero, IReadOnlyList<string> body)
    {
        var all = new List<string>();
        if (hero != null && hero.StartsWith(ImageHost, StringComparison.Ordinal))
            all.Add(ImageBase(hero));
        // Hero plus up to three images of the article.
        all.AddRange(body.Where(b => !all.Contains(b)));
        return all.Take(MaxImages).Select(i => i + ImageSuffix).ToList();
    }

    private static string ImageBase(string url)
    {
        var query = url.IndexOf('?', StringComparison.Ordinal);
        return query < 0 ? url : url[..query];
    }

    #endregion // Article body

    private static string? Text(JToken? token)
    {
        if (token is not { Type: JTokenType.String })
            return null;
        var text = Clean((string?)token ?? "");
        // Next.js writes undefined props as this marker.
        return text.Length == 0 || text == "$undefined" ? null : text;
    }

    private static DateTimeOffset? ParseDate(string? text)
    {
        if (text == null)
            return null;
        var styles = System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal;
        return DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, styles, out var date) ? date : null;
    }

    /// <summary>Decodes entities and collapses whitespace.</summary>
    private static string Clean(string text) => Regex.Replace(HtmlEntity.DeEntitize(text), @"\s+", " ").Trim();
}

/// <summary>Links and selection helpers of the blog posts.</summary>
public static class BlogLinks
{
    /// <summary>The longest heading text put into a text fragment; a prefix is enough for the browser to find the heading.</summary>
    public const int MaxFragmentLength = 40;

    /// <summary>
    /// Encodes <paramref name="text"/> for a URL text fragment (<c>#:~:text=</c>): everything but letters and digits is
    /// percent-encoded, including <c>-</c>, <c>,</c> and <c>&amp;</c> that the fragment syntax itself uses.
    /// </summary>
    public static string EncodeTextFragment(string text)
    {
        var builder = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            if (b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'_' or (byte)'.' or (byte)'~')
                builder.Append((char)b);
            else
                builder.Append('%').Append(b.ToString("X2"));
        }
        return builder.ToString();
    }

    /// <summary>The article URL that scrolls to (and highlights) the first match of <paramref name="heading"/>.</summary>
    public static string ChapterUrl(string articleUrl, string heading)
    {
        var text = heading.Trim();
        if (text.Length > MaxFragmentLength)
        {
            // Cut at a word boundary where possible.
            var cut = text.LastIndexOf(' ', MaxFragmentLength);
            text = text[..(cut > MaxFragmentLength / 2 ? cut : MaxFragmentLength)];
            if (char.IsHighSurrogate(text[^1]))
                text = text[..^1];
        }
        var withoutFragment = articleUrl.Split('#')[0];
        return $"{withoutFragment}#:~:text={EncodeTextFragment(text)}";
    }

    /// <summary>
    /// The items still to post, oldest first and at most <paramref name="max"/>; the rest is left for the next check.
    /// <paramref name="items"/> is in feed order (newest first), which orders items published on the same day.
    /// </summary>
    public static IReadOnlyList<T> PendingPosts<T>(IEnumerable<T> items, Func<T, string> id, Func<T, DateTimeOffset> published, ICollection<string> posted, int max) =>
        items.Where(i => !posted.Contains(id(i)))
            .Reverse()
            .OrderBy(published)
            .Take(max)
            .ToList();
}
