using System.Text.RegularExpressions;

namespace DiscordBot.Utils;

public static class StringUtil
{
    private static readonly Regex CurrencyRegex =
        new Regex(@"(?:\$\s*\d+|\d+\s*\$|\d*\s*(?:USD|£|pounds|€|EUR|euro|euros|GBP))", RegexOptions.IgnoreCase);
    private static readonly Regex RevShareRegex = new Regex(@"\b(?:rev-share|revshare|rev share)\b");
    
    // a string extension that checks if the contents of the string contains a limited selection of currency symbols/words
    public static bool ContainsCurrencySymbol(this string str)
    {
        return !string.IsNullOrWhiteSpace(str) && CurrencyRegex.IsMatch(str);
    }
    
    public static bool ContainsRevShare(this string str)
    {
        return !string.IsNullOrWhiteSpace(str) && RevShareRegex.IsMatch(str);
    }
}