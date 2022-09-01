using System.Text;
using System.Text.RegularExpressions;

namespace DiscordBot.Utils;

public static class Utils
{
    public static string FormatTime(uint seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalSeconds == 0) return "0 seconds";

        var parts = new List<string>();

        int days = span.Days;
        // if days is over a year
        if (days >= 365)
        {
            int years = days / 365;
            parts.Add($"{years} year{(years > 1 ? "s" : "")}");
            days %= 365;
        }

        if (days > 0) parts.Add($"{days} day{(days > 1 ? "s" : "")}");

        if (span.Hours > 0) parts.Add($"{span.Hours} hour{(span.Hours > 1 ? "s" : "")}");

        if (span.Minutes > 0) parts.Add($"{span.Minutes} minute{(span.Minutes > 1 ? "s" : "")}");

        if (span.Seconds > 0) parts.Add($"{span.Seconds} second{(span.Seconds > 1 ? "s" : "")}");

        var finishedTime = string.Empty;
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                if (i == parts.Count - 1)
                    finishedTime += " and ";
                else
                    finishedTime += ", ";
            }

            finishedTime += parts[i];
        }

        return finishedTime;
    }

    /// <summary>
    ///     Sanitize XML, from https://seattlesoftware.wordpress.com/2008/09/11/hexadecimal-value-0-is-an-invalid-character/
    /// </summary>
    /// <param name="xml"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static string SanitizeXml(string xml)
    {
        if (xml == null) throw new ArgumentNullException("xml");

        var buffer = new StringBuilder(xml.Length);

        foreach (var c in xml)
            if (IsLegalXmlChar(c))
                buffer.Append(c);

        return buffer.ToString();
    }

    /// <summary>
    ///     Whether a given character is allowed by XML 1.0.
    /// </summary>
    public static bool IsLegalXmlChar(int character) =>
        character == 0x9 /* == '\t' == 9   */ ||
        character == 0xA /* == '\n' == 10  */ ||
        character == 0xD /* == '\r' == 13  */ ||
        character >= 0x20 && character <= 0xD7FF ||
        character >= 0xE000 && character <= 0xFFFD ||
        character >= 0x10000 && character <= 0x10FFFF;

    public static ThreadArchiveDuration GetMaxThreadDuration(ThreadArchiveDuration wantedDuration, IGuild guild)
    {
        var maxDuration = ThreadArchiveDuration.OneDay;
        if (guild.PremiumTier >= PremiumTier.Tier2) maxDuration = ThreadArchiveDuration.OneWeek;
        else if (guild.PremiumTier >= PremiumTier.Tier1) maxDuration = ThreadArchiveDuration.ThreeDays;

        if (wantedDuration > maxDuration) return maxDuration;
        return wantedDuration;
    }
        
    // Returns a datetime from a string using common date terms, ie; '1 year 40 days', '30 minutes 10 seconds', '10m 1d 400s', '1d 10h'
    public static DateTime ParseTimeFromString(string time)
    {
        var timeSpan = TimeSpan.Zero;
        var timeSpanRegex = new Regex(@"(?<value>\d+) *(?<unit>[^\d\W]+)");
        var matches = timeSpanRegex.Matches(time);
        foreach (Match match in matches)
        {
            var value = int.Parse(match.Groups["value"].Value);
            var unit = match.Groups["unit"].Value;
            switch (unit)
            {
                case "s":
                case "sec":
                case "second":
                case "seconds":
                    timeSpan += TimeSpan.FromSeconds(value);
                    break;
                case "m":
                case "min":
                case "minute":
                case "minutes":
                    timeSpan += TimeSpan.FromMinutes(value);
                    break;
                case "h":
                case "hour":
                case "hours":
                    timeSpan += TimeSpan.FromHours(value);
                    break;
                case "d":
                case "day":
                case "days":
                    timeSpan += TimeSpan.FromDays(value);
                    break;
                case "w":
                case "week":
                case "weeks":
                    timeSpan += TimeSpan.FromDays(value * 7);
                    break;
                case "mo":
                case "month":
                case "months":
                    timeSpan += TimeSpan.FromDays(value * 30);
                    break;
                case "y":
                case "year":
                case "years":
                    timeSpan += TimeSpan.FromDays(value * 365);
                    break;
            }
        }
        return DateTime.Now + timeSpan;
    }
        
    public static string MessageLinkBack(ulong guildId, ulong channelId, ulong messageId)
    {
        return $"https://discordapp.com/channels/{guildId}/{channelId}/{messageId}";
    }
}