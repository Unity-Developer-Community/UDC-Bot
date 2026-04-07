using HtmlAgilityPack;

namespace DiscordBot.Services;

public static class UnityDocParser
{
    public static string[][] ConvertJsToArray(string data, bool isManual)
    {
        var list = new List<string[]>();
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
            list.Add(new[] { ps[0].Replace("\"", ""), ps[1].Replace("\"", "") });
        }

        return list.ToArray();
    }
}
