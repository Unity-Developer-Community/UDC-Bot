using HtmlAgilityPack;

namespace DiscordBot.Utils;

public interface IWebClient
{
    Task<string> GetContent(string url, CancellationToken cancellationToken = default);
    Task<HtmlDocument?> GetHtmlDocument(string url, CancellationToken cancellationToken = default);
    Task<HtmlNode?> GetHtmlNode(string url, string xpath, CancellationToken cancellationToken = default);
    Task<HtmlNodeCollection?> GetHtmlNodes(string url, string xpath, CancellationToken cancellationToken = default);
    Task<string?> GetHtmlNodeInnerText(string url, string xpath, CancellationToken cancellationToken = default);
    Task<string> GetXMLContent(string url, CancellationToken cancellationToken = default);
    Task<T?> GetObjectFromJson<T>(string url, CancellationToken cancellationToken = default);
    Task<(bool success, T? result)> TryGetObjectFromJson<T>(string url, CancellationToken cancellationToken = default);
}
