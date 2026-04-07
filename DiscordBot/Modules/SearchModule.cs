using System.Net;
using System.Text;
using Discord.Commands;
using DiscordBot.Services;
using DiscordBot.Settings;
using DiscordBot.Attributes;

namespace DiscordBot.Modules;

[Group("UserModule"), Alias("")]
public class SearchModule : ModuleBase
{
    public ILoggingService LoggingService { get; set; } = null!;
    public BotSettings Settings { get; set; } = null!;
    public UpdateService UpdateService { get; set; } = null!;
    public SearchService SearchService { get; set; } = null!;

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
        var results = SearchService.SearchDuckDuckGo(query, resNum, site);

        var resultTitle = string.Empty;
        for (int i = 0; i < results.Count; i++)
        {
            resultTitle += $"{i + 1}. {results[i].Title} [__Read More__]({results[i].Url})\n";
        }

        var searchQuery = "https://duckduckgo.com/html/?q=" + query.Replace(' ', '+');
        if (site != string.Empty) searchQuery += "+site:" + site;

        EmbedBuilder embedBuilder = new();
        embedBuilder.Title = $"Q: {WebUtility.UrlDecode(query)}";
        embedBuilder.AddField("Search Query", searchQuery);
        embedBuilder.AddField("Results", resultTitle.Length > 0 ? resultTitle : "No results found.", inline: false);
        embedBuilder.Color = new Color(81, 50, 169);
        embedBuilder.Footer = new EmbedFooterBuilder().WithText("Results sourced from DuckDuckGo.");

        await ReplyAsync(embed: embedBuilder.Build());
    }

    [Command("Manual"), Priority(8)]
    [Summary("Searches Unity3D manual for results. Syntax : !manual \"query\"")]
    public async Task SearchManual(params string[] queries)
    {
        var pages = await UpdateService.GetManualDatabase();
        var query = string.Join(" ", queries);
        var match = SearchService.FindBestMatch(query, pages!, "https://docs.unity3d.com/Manual");

        if (match != null)
        {
            var url = $"{match.BaseUrl}/{match.PageName}.html";

            EmbedBuilder embedBuilder = new();
            embedBuilder.Title = $"Found {match.PageName}";
            embedBuilder.Description = $"**{match.Title}** - [Read More..]({url})";
            embedBuilder.Color = new Color(81, 50, 169);
            embedBuilder.Footer = new EmbedFooterBuilder().WithText("Results sourced from Unity3D Docs.");
            var message = await ReplyAsync(embed: embedBuilder.Build());

            var description = SearchService.FetchPageDescription(url, "//h1", "following-sibling::p");
            if (description != null)
            {
                embedBuilder.WithDescription($"**Description:** {description}\n[Read More..]({url})");
                await message.ModifyAsync(msg => msg.Embed = embedBuilder.Build());
            }
        }
        else
            await ReplyAsync("No Results Found.").DeleteAfterSeconds(seconds: 10)!;
    }

    [Command("Doc"), Priority(9)]
    [Summary("Searches Unity3D API for results. Syntax : !api \"query\"")]
    [Alias("ref", "reference", "api", "docs")]
    public async Task SearchApi(params string[] queries)
    {
        var pages = await UpdateService.GetApiDatabase();
        var query = string.Join(" ", queries);
        var match = SearchService.FindBestMatch(query, pages!, "https://docs.unity3d.com/ScriptReference");

        if (match != null)
        {
            var url = $"{match.BaseUrl}/{match.PageName}.html";

            EmbedBuilder embedBuilder = new();
            embedBuilder.Title = $"Found {match.PageName}";
            embedBuilder.Description = $"**{match.Title}** - [Read More..]({url})";
            embedBuilder.Color = new Color(81, 50, 169);
            embedBuilder.Footer = new EmbedFooterBuilder().WithText("Results sourced from Unity3D Docs.");
            var message = await ReplyAsync(embed: embedBuilder.Build());

            var description = SearchService.FetchPageDescription(url, "//h3[contains(text(), 'Description')]", "following-sibling::p");
            var manualLink = SearchService.FetchManualLink(url);

            string descriptionString = description != null
                ? $"**Description:** {description}\n[Read More..]({url})"
                : string.Empty;
            string manualLinkString = manualLink != null
                ? $"\n**Manual:** {manualLink}"
                : string.Empty;

            embedBuilder.WithDescription(descriptionString + manualLinkString);
            await message.ModifyAsync(msg => msg.Embed = embedBuilder.Build());
        }
        else
            await ReplyAsync("No Results Found.").DeleteAfterSeconds(seconds: 10)!;
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

        await ReplyAsync(embed: GetWikipediaEmbed(article.name!, article.extract!, article.url!));
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
}
