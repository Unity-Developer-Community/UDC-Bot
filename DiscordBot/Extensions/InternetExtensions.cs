using System.Net;
using System.Net.Http;

namespace DiscordBot.Extensions;

public static class InternetExtensions
{
    private static readonly HttpClient _httpClient = new();

    public static async Task<string> GetHttpContents(string uri)
    {
        try
        {
            return await _httpClient.GetStringAsync(uri);
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"Error trying to load HTTP content.\rER: {e.Message}", Discord.LogSeverity.Warning);
            return string.Empty;
        }
    }
}