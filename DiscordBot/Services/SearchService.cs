using System.Net;
using HtmlAgilityPack;

namespace DiscordBot.Services;

public class SearchService
{
    public record SearchResult(string Title, string Url);

    public record DocSearchResult(string PageName, string Title, string BaseUrl, string? Description = null);

    public List<SearchResult> SearchDuckDuckGo(string query, uint maxResults = 3, string site = "")
    {
        maxResults = maxResults <= 5 ? maxResults : 5;
        var searchQuery = "https://duckduckgo.com/html/?q=" + query.Replace(' ', '+');
        if (site != string.Empty) searchQuery += "+site:" + site;

        var doc = new HtmlWeb().Load(searchQuery);
        var results = new List<SearchResult>();

        var nodes = doc.DocumentNode.SelectNodes("/html/body/div[1]/div[3]/div/div/div[*]/div/h2/a");
        if (nodes == null) return results;

        foreach (var row in nodes)
        {
            if (results.Count >= maxResults) break;

            row.Attributes["href"].Value = row.Attributes["href"].Value
                .Replace("//duckduckgo.com/l/?uddg=", string.Empty);

            if (row.Attributes["href"].Value.Contains("duckduckgo.com") ||
                row.Attributes["href"].Value.Contains("duck.co"))
                continue;

            var url = WebUtility.UrlDecode(row.Attributes["href"].Value);
            int andCount = url.Count(c => c == '&');
            url = url[..url.LastIndexOf('&')];

            var title = row.InnerText.Length > 60 ? $"{row.InnerText[..60]}.." : row.InnerText;
            results.Add(new SearchResult(title, url + (andCount > 1 ? "~" : string.Empty)));
        }

        return results;
    }

    public DocSearchResult? FindBestMatch(string query, string[][] database, string baseUrl)
    {
        var minimumScore = double.MaxValue;
        string[] mostSimilarPage = null;

        foreach (var p in database)
        {
            var curScore = CalculateScore(p[1], query);
            if (curScore < minimumScore)
            {
                minimumScore = curScore;
                mostSimilarPage = p;
            }
        }

        if (mostSimilarPage == null) return null;
        return new DocSearchResult(mostSimilarPage[0], mostSimilarPage[1], baseUrl);
    }

    public string? FetchPageDescription(string url, string descriptionXPath, string? nextSiblingFilter = null)
    {
        var doc = new HtmlWeb().Load(url);
        var node = doc.DocumentNode.SelectSingleNode(descriptionXPath);
        if (node == null) return null;

        if (nextSiblingFilter != null)
            node = node.SelectSingleNode(nextSiblingFilter);

        node?.Descendants()
            .Where(n => n.GetAttributeValue("class", "").Contains("tooltip"))
            .ToList()
            .ForEach(n => n.Remove());

        var text = node?.InnerText;
        if (text != null && text.Length > 500)
            text = $"{text[..500]}..";

        return text;
    }

    public string? FetchManualLink(string url)
    {
        var doc = new HtmlWeb().Load(url);
        var manualLink = doc.DocumentNode.SelectSingleNode("//a[contains(@class, 'switch-link')]");
        if (manualLink == null || !manualLink.Attributes.Contains("title")) return null;

        var text = manualLink.GetAttributes("title").First().Value;
        var linkUrl = "https://docs.unity3d.com/" + manualLink.GetAttributeValue("href", "");
        return $"[{text}]({linkUrl})";
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
