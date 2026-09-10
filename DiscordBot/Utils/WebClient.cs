using System.Net;
using System.Net.Http;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace DiscordBot.Utils;

public class WebClient : IWebClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public WebClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Returns the content of a URL as a string, or an empty string if the request fails.
    /// </summary>
    public async Task<string> GetContent(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(url, cancellationToken);
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[WebClient] Failed to get content from {url}: {e.Message}", ExtendedLogSeverity.LowWarning);
            return "";
        }
    }

    /// <summary>
    /// Returns the Html document of a url, or null if the request fails.
    /// Internally calls GetContent and parses the result.
    /// </summary>
    public async Task<HtmlDocument?> GetHtmlDocument(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var html = await GetContent(url, cancellationToken);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the Html node of a url and xpath, or null if the request fails.
    /// Internally calls GetHtmlDocument and parses the result with xpath.
    /// </summary>
    public async Task<HtmlNode?> GetHtmlNode(string url, string xpath, CancellationToken cancellationToken = default)
    {
        try
        {
            var doc = await GetHtmlDocument(url, cancellationToken);
            return doc?.DocumentNode.SelectSingleNode(xpath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the Html nodes of a url and xpath, or null if the request fails.
    /// </summary>
    public async Task<HtmlNodeCollection?> GetHtmlNodes(string url, string xpath, CancellationToken cancellationToken = default)
    {
        try
        {
            var doc = await GetHtmlDocument(url, cancellationToken);
            return doc?.DocumentNode.SelectNodes(xpath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the decoded inner text of a url and xpath, or an empty string if the request fails.
    /// </summary>
    public async Task<string?> GetHtmlNodeInnerText(string url, string xpath, CancellationToken cancellationToken = default)
    {
        try
        {
            var node = await GetHtmlNode(url, xpath, cancellationToken);
            return WebUtility.HtmlDecode(node?.InnerText);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Returns the content of a url as a sanitized XML string, or an empty string if the request fails.
    /// </summary>
    public async Task<string> GetXMLContent(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await GetContent(url, cancellationToken);
            // We check if we're dealing with XML and sanitize it, otherwise we just return the content
            if (content.StartsWith("<?xml"))
                content = Utils.SanitizeXml(content);
            return content;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[WebClient] Failed to get content from {url}: {e.Message}", ExtendedLogSeverity.LowWarning);
            return string.Empty;
        }
    }

    /// <summary>
    /// Returns a deserialized object from a JSON string. If the string is empty or can't be deserialized, it returns the default value of the type.
    /// </summary>
    public async Task<T?> GetObjectFromJson<T>(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await GetContent(url, cancellationToken);
            return JsonConvert.DeserializeObject<T>(content) ?? default;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[WebClient] Failed to get content from {url}: {e.Message}", ExtendedLogSeverity.LowWarning);
            return default;
        }
    }

    /// <summary>
    /// Returns a deserialized object from a JSON string, or null if the string is empty or can't be deserialized.
    /// </summary>
    public async Task<(bool success, T? result)> TryGetObjectFromJson<T>(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await GetContent(url, cancellationToken);
            var result = JsonConvert.DeserializeObject<T>(content);
            return (true, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[WebClient] Failed to get content from {url}: {e.Message}", ExtendedLogSeverity.LowWarning);
            return (false, default);
        }
    }
}
