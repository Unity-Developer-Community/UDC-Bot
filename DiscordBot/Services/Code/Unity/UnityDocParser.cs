using System.IO;
using DiscordBot.Domain;
using HtmlAgilityPack;

namespace DiscordBot.Services.Code.Unity;

public static class UnityDocParser
{
    /// <summary>Starts the search data that follows the page list in both doc indexes; the parser only needs the text before it.</summary>
    public const string InfoMarker = "info =";

    /// <summary>
    /// The text before <paramref name="marker"/>, reading no further than that, or all of it when the
    /// marker is missing. The doc indexes are about 15 MB each, but the page list is the first 0.2–3.4 MB.
    /// </summary>
    public static async Task<string> ReadUntilAsync(TextReader reader, string marker, CancellationToken token = default, int bufferSize = 16 * 1024)
    {
        var text = new System.Text.StringBuilder();
        var buffer = new char[bufferSize];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            // The marker can straddle two reads.
            var searchFrom = Math.Max(0, text.Length - marker.Length + 1);
            text.Append(buffer, 0, read);
            var index = text.ToString(searchFrom, text.Length - searchFrom).IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
                return text.ToString(0, searchFrom + index);
        }
        return text.ToString();
    }

    public static DocEntry[] ConvertJsToArray(string data, bool isManual)
    {
        var list = new List<DocEntry>();
        string pagesInput;

        if (isManual)
        {
            pagesInput = data.Split("info = [")[0].Split("pages=")[1];
            pagesInput = pagesInput[2..^2];
        }
        else
        {
            pagesInput = data.Split("info =")[0];
            pagesInput = pagesInput[63..^2];
        }

        foreach (var s in pagesInput.Split("],["))
        {
            var ps = s.Split(",");
            list.Add(new DocEntry(ps[0].Replace("\"", ""), ps[1].Replace("\"", "")));
        }

        return list.ToArray();
    }
}
