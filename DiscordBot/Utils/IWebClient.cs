using HtmlAgilityPack;

namespace DiscordBot.Utils;

public interface IWebClient
{
    Task<string> GetContent(string url);
    Task<HtmlDocument?> GetHtmlDocument(string url);
    Task<HtmlNode?> GetHtmlNode(string url, string xpath);
    Task<HtmlNodeCollection?> GetHtmlNodes(string url, string xpath);
    Task<string?> GetHtmlNodeInnerText(string url, string xpath);
    Task<string> GetXMLContent(string url);
    Task<T?> GetObjectFromJson<T>(string url);
    Task<(bool success, T? result)> TryGetObjectFromJson<T>(string url);
}
