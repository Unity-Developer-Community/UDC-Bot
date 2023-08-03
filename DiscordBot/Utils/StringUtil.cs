using System.Text.RegularExpressions;

namespace DiscordBot.Utils;

public static class StringUtil
{
    private static readonly Regex CurrencyRegex =
        new (@"(?:\$\s*\d+|\d+\s*\$|\d*\s*(?:USD|£|pounds|€|EUR|euro|euros|GBP|円|YEN))", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    private static readonly Regex RevShareRegex = new (@"\b(?:rev-share|revshare|rev share)\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    
    // a string extension that checks if the contents of the string contains a limited selection of currency symbols/words
    public static bool ContainsCurrencySymbol(this string str)
    {
        return !string.IsNullOrWhiteSpace(str) && CurrencyRegex.IsMatch(str);
    }
    
    public static bool ContainsRevShare(this string str)
    {
        return !string.IsNullOrWhiteSpace(str) && RevShareRegex.IsMatch(str);
    }

    public static string MessageSelfDestructIn(int secondsFromNow)
    {
        var time = DateTime.Now.ToUnixTimestamp() + secondsFromNow;
        return $"Self-delete: **<t:{time}:R>**";
    }
}