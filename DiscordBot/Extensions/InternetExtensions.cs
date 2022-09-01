using System.IO;
using System.Net;

namespace DiscordBot.Extensions;

public static class InternetExtensions
{
    /// <summary>
    /// Loads a webpage and returns the contents as a string, Return an empty string on failure.
    /// </summary>
    public static async Task<string> GetHttpContents(string uri)
    {
        try
        {
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using var response = (HttpWebResponse)await request.GetResponseAsync();
            await using var stream = response.GetResponseStream();
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"Error trying to load HTTP content.\rER: {e.Message}", Discord.LogSeverity.Warning);
            return string.Empty;
        }
    }
}