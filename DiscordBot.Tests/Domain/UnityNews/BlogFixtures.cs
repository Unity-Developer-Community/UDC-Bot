using DiscordBot.Domain.UnityNews;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Tests.Domain.UnityNews;

/// <summary>Small pages shaped like unity.com blog articles (the real ones are 600 KB of Next.js markup).</summary>
internal static class BlogFixtures
{
    private const string Cdn = "https://cdn.sanity.io/images/abc/production/";
    public const string Hero = Cdn + "aaaa-1920x1080.png";
    public const string Avatar = Cdn + "bbbb-1058x1058.jpg";
    public const string Body1 = Cdn + "c1-1392x831.png";
    public const string Body2 = Cdn + "c2-1814x1020.png";
    public const string Body3 = Cdn + "c3-1999x838.png";
    public const string Body4 = Cdn + "c4-1200x700.png";
    public const string SmallIcon = Cdn + "dddd-96x96.png";

    public const string ArticleUrl = "https://unity.com/blog/how-seed-renders";

    private static string LdScript(string headline, string[] authors, string date, string? image) =>
        "<script type=\"application/ld+json\">" + new JObject
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BlogPosting",
            ["headline"] = headline,
            ["image"] = image == null ? null : new JArray(image),
            ["datePublished"] = date,
            ["author"] = new JArray(authors.Select(a => new JObject { ["@type"] = "Person", ["name"] = a })),
        }.ToString(Formatting.None) + "</script>";

    /// <summary>The Next.js flight data: the header object sits inside a string pushed in two chunks, as in the real pages.</summary>
    public static string FlightScript(object header)
    {
        var stream = "1c:[\"$\",\"$L75\",null," + JsonConvert.SerializeObject(header) + "]\n";
        var half = stream.Length / 2;
        string Push(string chunk) => "<script>self.__next_f.push([1," + JsonConvert.SerializeObject(chunk) + "])</script>";
        return "<script>self.__next_f.push([0])</script>" + Push(stream[..half]) + Push(stream[half..]);
    }

    public static object Header(int? minutes = 6) => new Dictionary<string, object?>
    {
        ["backButton"] = "$undefined",
        ["title"] = "How SEED renders a world",
        ["tags"] = "$undefined",
        ["date"] = "Nov 6, 2026",
        ["readDuration"] = minutes == null ? new { label = "Min" } : new { minutes, label = "Min" },
        ["topics"] = new[] { new { label = "Platforms and publishing", href = "$undefined" }, new { label = "Programming", href = "/blog?topics=programming" } },
        ["authors"] = new[]
        {
            new { name = "Adam Axler", role = "Senior Content Marketing Manager ", company = " Unity", avatar = Avatar },
            new { name = "Mike Geig", role = "Principal Advocate", company = "Unity", avatar = "$undefined" },
        },
        ["heroImage"] = new { src = Hero },
    };

    private static string Img(string url, string? width = null) =>
        $"<img alt=\"\" {(width == null ? "" : $"width=\"{width}\" ")}src=\"{url}?w=3840&amp;auto=format&amp;q=75\"/>";

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("word", count));

    public static string Page(string richText, string? flight = null, string[]? authors = null, string images = "") =>
        "<html><head>" + LdScript("How SEED renders a world", authors ?? ["Adam Axler"], "2026-11-06", Hero) + "</head><body>"
        + "<h1>How SEED renders a world</h1>"
        + Img(Avatar, "42") + Img(Hero) + images
        + "<div class=\"portable-text-base\" data-x=\"RichText\">" + richText + "</div>"
        + (flight ?? FlightScript(Header())) + "</body></html>";

    public static string AllImages => Img(Body1) + Img(Body2) + Img(Body3) + Img(Body4) + Img(SmallIcon) + Img(Body1);

    public static readonly string H2Page = Page(
        "<h2><strong>A world built from tiny pieces</strong></h2><p>" + Words(100) + "</p><h3>Detail one</h3><p>" + Words(100) + "</p>"
        + "<h2>Why we&#x27;re stuck, reloading - a &amp; b</h2><h3>Detail two</h3><h2>A world built from tiny pieces</h2><h2>   </h2>",
        authors: ["Adam Axler", "Mike Geig"], images: AllImages);

    public static readonly string H3OnlyPage = Page("<h3>First</h3><p>" + Words(50) + "</p><h4>Sub</h4><h3>Second</h3>");

    public static readonly string NoFlightPage = Page("<h2>One</h2><p>" + Words(460) + "</p>", flight: "");

    public static readonly string NoMinutesPage = Page("<p>" + Words(920) + "</p>", flight: FlightScript(Header(minutes: null)));

    private static object NumberedHeader() => new Dictionary<string, object?>
    {
        ["backButton"] = "$undefined",
        ["title"] = "How to organize a game project with AI",
        ["tags"] = "$undefined",
        ["date"] = "Nov 10, 2026",
        ["readDuration"] = new { minutes = 5, label = "Min" },
        ["topics"] = "$undefined",
        ["authors"] = new[] { new { name = "Unity Team", role = " ", company = " Unity Technologies", avatar = Avatar } },
        ["heroImage"] = new { src = Hero },
    };

    /// <summary>The shape of a post that was once posted without details: h3 headings only, all numbered, no topics or tags, a blank author role, no body images.</summary>
    public static readonly string NumberedH3Page = Page(
        string.Concat(Enumerable.Range(1, 10).Select(n => $"<h3>{n}. Step number {n}</h3><p>{Words(60)}</p>")) + "<h3>Your turn</h3><p>" + Words(30) + "</p>",
        flight: FlightScript(NumberedHeader()), authors: ["Unity Team"]);
}
