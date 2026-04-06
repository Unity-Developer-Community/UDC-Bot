using System.Net;
using System.Text;
using Discord.Commands;
using DiscordBot.Services;
using DiscordBot.Settings;
using DiscordBot.Attributes;
using HtmlAgilityPack;

namespace DiscordBot.Modules;

[Group("UserModule"), Alias("")]
public class SearchModule : ModuleBase
{
    public ILoggingService LoggingService { get; set; }
    public BotSettings Settings { get; set; }
    public UpdateService UpdateService { get; set; }

    [Command("Search"), Priority(25)]
    [Summary("Searches DuckDuckGo for results. Syntax: !search c# lambda help")]
    [Alias("s", "ddg")]
    public async Task SearchResults(params string[] messages)
    {
        StringBuilder sb = new();
        foreach (var msg in messages)
            sb.Append(msg).Append(" ");
        await SearchResults(sb.ToString());
    }

    [Command("Search"), HideFromHelp]
    [Summary("Searches DuckDuckGo for web results. Syntax : !search \"query\" resNum site")]
    [Alias("s", "ddg")]
    public async Task SearchResults(string query, uint resNum = 3, string site = "")
    {
        resNum = resNum <= 5 ? resNum : 5;
        var searchQuery = "https://duckduckgo.com/html/?q=" + query.Replace(' ', '+');

        if (site != string.Empty) searchQuery += "+site:" + site;

        var doc = new HtmlWeb().Load(searchQuery);
        var counter = 1;

        EmbedBuilder embedBuilder = new();
        embedBuilder.Title = $"Q: {WebUtility.UrlDecode(query)}";
        string resultTitle = string.Empty;

        foreach (var row in doc.DocumentNode.SelectNodes("/html/body/div[1]/div[3]/div/div/div[*]/div/h2/a"))
        {
            if (counter > resNum) break;

            row.Attributes["href"].Value = row.Attributes["href"].Value.Replace("//duckduckgo.com/l/?uddg=", string.Empty);

            if (counter <= resNum && IsValidResult(row))
            {
                var url = WebUtility.UrlDecode(row.Attributes["href"].Value);

                int andCount = url.Count(c => c == '&');
                url = url.Substring(0, url.LastIndexOf('&'));

                resultTitle += $"{counter}. {(row.InnerText.Length > 60 ? $"{row.InnerText[..60]}.." : row.InnerText)}" + $" [__Read More..__{(andCount > 1 ? "~" : string.Empty)}]({url})\n";

                counter++;
            }
        }

        embedBuilder.AddField("Search Query", searchQuery);
        embedBuilder.AddField("Results", resultTitle, inline: false);

        embedBuilder.Color = new Color(81, 50, 169);
        embedBuilder.Footer = new EmbedFooterBuilder().WithText("Results sourced from DuckDuckGo.");

        var embed = embedBuilder.Build();
        await ReplyAsync(embed: embed);
    }

    bool IsValidResult(HtmlNode node)
    {
        return (!node.Attributes["href"].Value.Contains("duckduckgo.com") &&
                !node.Attributes["href"].Value.Contains("duck.co"));
    }

    [Command("Manual"), Priority(8)]
    [Summary("Searches Unity3D manual for results. Syntax : !manual \"query\"")]
    public async Task SearchManual(params string[] queries)
    {
        var minimumScore = double.MaxValue;
        string[] mostSimilarPage = null;
        var pages = await UpdateService.GetManualDatabase();
        var query = string.Join(" ", queries);
        foreach (var p in pages)
        {
            var curScore = CalculateScore(p[1], query);
            if (!(curScore < minimumScore)) continue;

            minimumScore = curScore;
            mostSimilarPage = p;
        }

        if (mostSimilarPage != null)
        {
            EmbedBuilder embedBuilder = new();
            embedBuilder.Title = $"Found {mostSimilarPage[0]}";
            embedBuilder.Description = $"**{mostSimilarPage[1]}** - [Read More..](https://docs.unity3d.com/Manual/{mostSimilarPage[0]}.html)";
            embedBuilder.Color = new Color(81, 50, 169);
            embedBuilder.Footer = new EmbedFooterBuilder().WithText("Results sourced from Unity3D Docs.");
            var message = await ReplyAsync(embed: embedBuilder.Build());

            var doc = new HtmlWeb().Load($"https://docs.unity3d.com/Manual/{mostSimilarPage[0]}.html");
            var descriptionNode = doc.DocumentNode.SelectSingleNode("//h1");
            if (descriptionNode == null) return;
            descriptionNode = descriptionNode.SelectSingleNode("following-sibling::p");
            descriptionNode.Descendants().Where(n => n.GetAttributeValue("class", "").Contains("tooltip")).ToList().ForEach(n => n.Remove());
            var description = descriptionNode.InnerText;

            embedBuilder.WithDescription($"**Description:** {(description.Length > 500 ? $"{description[..500]}.." : description)}\n" + $"[Read More..](https://docs.unity3d.com/Manual/{mostSimilarPage[0]}.html)");
            await message.ModifyAsync(msg => msg.Embed = embedBuilder.Build());
        }
        else
            await ReplyAsync("No Results Found.").DeleteAfterSeconds(seconds: 10);
    }

    [Command("Doc"), Priority(9)]
    [Summary("Searches Unity3D API for results. Syntax : !api \"query\"")]
    [Alias("ref", "reference", "api", "docs")]
    public async Task SearchApi(params string[] queries)
    {
        var minimumScore = double.MaxValue;
        string[] mostSimilarPage = null;
        var pages = await UpdateService.GetApiDatabase();
        var query = string.Join(" ", queries);
        foreach (var p in pages)
        {
            var curScore = CalculateScore(p[1], query);
            if (!(curScore < minimumScore)) continue;

            minimumScore = curScore;
            mostSimilarPage = p;
        }

        if (mostSimilarPage != null)
        {
            EmbedBuilder embedBuilder = new();
            embedBuilder.Title = $"Found {mostSimilarPage[0]}";
            embedBuilder.Description = $"**{mostSimilarPage[1]}** - [Read More..](https://docs.unity3d.com/ScriptReference/{mostSimilarPage[0]}.html)";
            embedBuilder.Color = new Color(81, 50, 169);
            embedBuilder.Footer = new EmbedFooterBuilder().WithText("Results sourced from Unity3D Docs.");
            var message = await ReplyAsync(embed: embedBuilder.Build());

            var doc = new HtmlWeb().Load($"https://docs.unity3d.com/ScriptReference/{mostSimilarPage[0]}.html");
            var descriptionNode = doc.DocumentNode.SelectSingleNode("//h3[contains(text(), 'Description')]");

            string descriptionString = "";
            string manualLinkString = "";
            if (descriptionNode != null)
            {
                var description = descriptionNode.SelectSingleNode("following-sibling::p").InnerText;
                descriptionString =
                    $"**Description:** {(description.Length > 500 ? $"{description[..500]}.." : description)}\n" +
                    $"[Read More..](https://docs.unity3d.com/ScriptReference/{mostSimilarPage[0]}.html)";
            }

            var manualLink = doc.DocumentNode.SelectSingleNode("//a[contains(@class, 'switch-link')]");
            if (manualLink != null && manualLink.Attributes.Contains("title"))
            {
                var manualLinkText = manualLink.GetAttributes("title").First().Value;
                var manualLinkUrl = "https://docs.unity3d.com/" + manualLink.GetAttributeValue("href", "");
                manualLinkString = $"\n**Manual:** [{manualLinkText}]({manualLinkUrl})";
            }

            embedBuilder.WithDescription(descriptionString + manualLinkString);
            await message.ModifyAsync(msg => msg.Embed = embedBuilder.Build());
        }
        else
            await ReplyAsync("No Results Found.").DeleteAfterSeconds(seconds: 10);
    }

    [Command("Wiki"), Priority(26)]
    [Summary("Searches Wikipedia. Syntax : !wiki \"query\"")]
    [Alias("wikipedia")]
    public async Task SearchWikipedia([Remainder] string query)
    {
        var article = await UpdateService.DownloadWikipediaArticle(query);

        if (article.url == null)
        {
            await ReplyAsync($"No Articles for \"{query}\" were found.");
            return;
        }

        await ReplyAsync(embed: GetWikipediaEmbed(article.name, article.extract, article.url));
    }

    private Embed GetWikipediaEmbed(string subject, string articleExtract, string articleUrl)
    {
        var builder = new EmbedBuilder()
            .WithTitle($"Wikipedia | {subject}")
            .WithDescription($"{articleExtract}")
            .WithUrl(articleUrl)
            .WithColor(new Color(0x33CC00));
        return builder.Build();
    }

    private double CalculateScore(string s1, string s2)
    {
        double curScore = 0;
        var i = 0;

        foreach (var q in s1.Split(' '))
        {
            foreach (var x in s2.Split(' '))
            {
                i++;
                if (x.Equals(q))
                    curScore -= 50;
                else
                    curScore += x.CalculateLevenshteinDistance(q);
            }
        }

        curScore /= i;
        return curScore;
    }
}
