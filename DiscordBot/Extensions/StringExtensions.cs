using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DiscordBot.Properties;

namespace DiscordBot.Extensions;

public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    public static List<string> MessageSplit(this string str, int maxLength = 1990)
    {
        var list = str.Split('\n').ToList();
        var ret = new List<string>();

        var currentString = string.Empty;
        foreach (var s in list)
            if (currentString.Length + s.Length < maxLength)
                currentString += s + "\n";
            else
            {
                ret.Add(currentString);
                currentString = s + "\n";
            }

        if (!string.IsNullOrEmpty(currentString))
            ret.Add(currentString);

        return ret;
    }

    public static List<string> MessageSplitToSize(this string str,
        int maxLength = Constants.MaxLengthChannelMessage)
    {
        var container = new List<string>();
        if (str.Length < Constants.MaxLengthChannelMessage)
        {
            container.Add(str);
            return container;
        }

        var cuts = (str.Length / Constants.MaxLengthChannelMessage) + 1;
        var indexOfLine = 0;
        for (var cut = 1; cut <= cuts; cut++)
        {
            string page;
            page = cut == cuts ? str.Substring(indexOfLine) : str.Substring(indexOfLine, Constants.MaxLengthChannelMessage);

            indexOfLine = page.LastIndexOf("\n", StringComparison.Ordinal) + 1;
            container.Add(cut == cuts ? page : page.Remove(indexOfLine - 1));
        }
        return container;
    }

    /// <summary>
    ///     Adds a backslash behind each special character used by Discord to make a message appear plain-text.
    /// </summary>
    /// <param name="content"></param>
    /// <returns></returns>
    public static string EscapeDiscordMarkup(this string content) => Regex.Replace(content, @"([\\~\\_\`\*\`])", "\\$1");

    public static int CalculateLevenshteinDistance(this string source1, string source2) //O(n*m)
    {
        var source1Length = source1.Length;
        var source2Length = source2.Length;

        var matrix = new int[source1Length + 1, source2Length + 1];

        // First calculation, if one entry is empty return full length
        if (source1Length == 0)
            return source2Length;

        if (source2Length == 0)
            return source1Length;

        // Initialization of matrix with row size source1Length and columns size source2Length
        for (var i = 0; i <= source1Length; matrix[i, 0] = i++) ;
        for (var j = 0; j <= source2Length; matrix[0, j] = j++) ;

        // Calculate rows and collumns distances
        for (var i = 1; i <= source1Length; i++)
        {
            for (var j = 1; j <= source2Length; j++)
            {
                var cost = source2[j - 1] == source1[i - 1] ? 0 : 1;

                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        // return result
        return matrix[source1Length, source2Length];
    }

    public static string AsCodeBlock(this string code, string language = "cs") => Resources.DiscordCodeBlock.Replace("{code}", code).Replace("{language}", language);

    public static string GetSha256(this string input)
    {
        var hash = new SHA256CryptoServiceProvider();
        // Convert the input string to a byte array and compute the hash.
        var data = hash.ComputeHash(Encoding.UTF8.GetBytes(input));

        // Create a new Stringbuilder to collect the bytes
        // and create a string.
        var sb = new StringBuilder();

        // Loop through each byte of the hashed data
        // and format each one as a hexadecimal string.
        for (var i = 0; i < data.Length; i++) sb.Append(data[i].ToString("x2"));

        // Return the hexadecimal string.
        return sb.ToString();
    }
    
    /// <summary>
    /// Returns true if the string contains only upper case characters, including spaces and all punctuation ie; "I NEED HELP!?!?!?!#$?!" will return true 
    /// </summary>
    public static bool IsAllCaps(this string str)
    {
        return Regex.IsMatch(str, @"^[A-Z\s\p{P}]+$");
    }
    
    public static string ToCapitalizeFirstLetter(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return string.Empty;
        return char.ToUpper(str[0]) + str[1..];
    }

    /// <summary>
    /// Joins a list of strings with commas into a natural phrase:
    ///   "apples"
    ///   "apples and bananas"
    ///   "apples, bananas, and cherries"
    /// </summary>
    /// <param name="nouns">array or list of element phrases to be listed</param>
    /// <param name="conj">final conjunction; defaults to "and" if not given</param>
	public static string ToCommaList(this string[] nouns, string conj=null)
	{
		if (conj == null)
			conj = "and";
		var sb = new StringBuilder();
		for (int i = 0; i < nouns.Length; i++)
		{
			if (i > 0)
			{
				if (nouns.Length > 2)
					sb.Append(",");
                sb.Append(" ");
				if (i == nouns.Length-1)
					sb.Append(conj).Append(" ");
			}
			sb.Append(nouns[i]);
		}
		return sb.ToString();
	}
}
