using System.Net.Http;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace DiscordBot.Utils;

public static class WebUtil
{
    /// <summary>
    /// Returns the content of a URL as a string, or an empty string if the request fails.
    /// </summary>
    public static async Task<string> GetContent(string url)
    {
        using var client = new HttpClient();
        try
        {
            var response = await client.GetAsync(url);
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[WebUtil] Failed to get content from {url}: {e.Message}", ExtendedLogSeverity.LowWarning);
            return "";
        }
    }
    
    /// <summary>
    /// Returns the content of a url as a sanitized XML string, or an empty string if the request fails.
    /// </summary>
    public static async Task<string> GetXMLContent(string url)
    {
        try
        {
            var content = await GetContent(url);
            // We check if we're dealing with XML and sanitize it, otherwise we just return the content
            if (content.StartsWith("<?xml"))
                content = Utils.SanitizeXml(content);
            return content;
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[WebUtil] Failed to get content from {url}: {e.Message}", ExtendedLogSeverity.LowWarning);
            return string.Empty;
        }
    }
    
    /// <summary>
    /// Returns a deserialized object from a JSON string. If the string is empty or can't be deserialized, it returns the default value of the type.
    /// </summary>
    public static async Task<T> GetObjectFromJson<T>(string url)
    {
        try
        {
            var content = await GetContent(url);
            return JsonConvert.DeserializeObject<T>(content) ?? default;
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[WebUtil] Failed to get content from {url}: {e.Message}", ExtendedLogSeverity.LowWarning);
            return default;
        }
    }
    
    /// <summary>
    /// Returns a deserialized object from a JSON string, or null if the string is empty or can't be deserialized.
    /// </summary>
    public static async Task<(bool success, T result)> TryGetObjectFromJson<T>(string url)
    {
        try
        {
            var content = await GetContent(url);
            var result = JsonConvert.DeserializeObject<T>(content);
            return (true, result);
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[WebUtil] Failed to get content from {url}: {e.Message}", ExtendedLogSeverity.LowWarning);
            return (false, default);
        }
    }
}