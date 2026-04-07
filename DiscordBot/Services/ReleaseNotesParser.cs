using HtmlAgilityPack;

namespace DiscordBot.Services;

public class ReleaseNotesParser
{
    private const int MaxFeedLengthBuffer = 400;

    public List<string> Parse(string summaryHtml)
    {
        var htmlDoc = new HtmlDocument();
        summaryHtml = summaryHtml.Replace("&#x2192;", "->");
        htmlDoc.LoadHtml(summaryHtml);

        var summaryNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='release-notes']");
        if (summaryNode == null)
            return new List<string> { "No release notes found" };

        var knownIssueNode = FindH3Sibling(summaryNode, "Known Issues");
        var entriesSinceNode = summaryNode.ChildNodes
            .FirstOrDefault(x => x.Name == "h3" && x.InnerText.Contains("Entries since"));

        var featuresNode = FindH4Sibling(summaryNode, "Features");
        var improvementsNode = FindH4Sibling(summaryNode, "Improvements");
        var apiChangesNode = FindH4Sibling(summaryNode, "API Changes");
        var changesNode = FindH4Sibling(summaryNode, "Changes");
        var fixesNode = FindH4Sibling(summaryNode, "Fixes");
        var packagesUpdatedNode = summaryNode.ChildNodes
            .FirstOrDefault(x => x.Name == "h4" && x.InnerText.ToLower().Contains("package changes"))
            ?.NextSibling?.NextSibling?.NextSibling;

        var summary = "**Summary**\n";
        summary += GetNodeLiCountString("Known Issues", knownIssueNode?.NextSibling);

        if (entriesSinceNode != null)
            summary += $"__{entriesSinceNode.InnerText}__\n\n";

        summary += GetNodeLiCountString("Features", featuresNode?.NextSibling);
        summary += GetNodeLiCountString("Improvements", improvementsNode?.NextSibling);
        summary += GetNodeLiCountString("API Changes", apiChangesNode?.NextSibling);
        summary += GetNodeLiCountString("Changes", changesNode?.NextSibling);
        summary += GetNodeLiCountString("Fixes", fixesNode?.NextSibling);
        summary += GetNodeLiCountString("Packages Updated", packagesUpdatedNode?.NextSibling);

        var releaseNotes = new List<string>
        {
            BuildSection("Packages Updated", packagesUpdatedNode, summary),
            BuildSection("Features", featuresNode),
            BuildSection("Improvements", improvementsNode, "", 1000),
            BuildSection("API Changes", apiChangesNode),
            BuildSection("Changes", changesNode),
            BuildSection("Fixes", fixesNode, ""),
            BuildSection("Known Issues", knownIssueNode, "", 1200)
        };

        return releaseNotes;
    }

    private static HtmlNode FindH3Sibling(HtmlNode parent, string text)
    {
        return parent.ChildNodes
            .FirstOrDefault(x => x.Name == "h3" && x.InnerText.Contains(text))
            ?.NextSibling;
    }

    private static HtmlNode FindH4Sibling(HtmlNode parent, string text)
    {
        return parent.ChildNodes
            .FirstOrDefault(x => x.Name == "h4" && x.InnerText == text)
            ?.NextSibling;
    }

    private string BuildSection(string title, HtmlNode node, string contents = "",
        int maxLength = Constants.MaxLengthChannelMessage - MaxFeedLengthBuffer)
    {
        if (node == null)
            return string.Empty;

        var summary = $"{(contents.Length > 0 ? $"{contents}\n" : string.Empty)}**{node.PreviousSibling.InnerText}**\n";

        bool needsExtraProcessing = title is "Fixes" or "Known Issues" or "API Changes";

        foreach (var feature in node.NextSibling.ChildNodes.Where(x => x.Name == "li"))
        {
            var extraText = string.Empty;
            if (needsExtraProcessing)
            {
                var nodeContents = feature.ChildNodes[0];
                nodeContents.InnerHtml = nodeContents.InnerHtml.Replace("\n", " ");

                var linkNode = nodeContents.SelectSingleNode("a");
                if (linkNode != null)
                {
                    nodeContents = nodeContents.RemoveChild(linkNode);
                    feature.InnerHtml = feature.InnerHtml.Replace("()", "");
                    extraText = $" ([{linkNode.InnerText}](<{linkNode.Attributes["href"].Value}>))";
                }
            }

            summary += $"- {feature.InnerText}{extraText}\n";
            if (summary.Length > maxLength)
            {
                var lastLine = summary[..maxLength].LastIndexOf('\n');
                summary = summary[..lastLine] + $"\n{title} truncated...\n";
                return summary;
            }
        }

        return summary;
    }

    private static string GetNodeLiCountString(string title, HtmlNode node)
    {
        if (node == null)
            return string.Empty;

        var count = node.ChildNodes.Count(x => x.Name == "li");
        return $"{title}: {count}\n";
    }
}
