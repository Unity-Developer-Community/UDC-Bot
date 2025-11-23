// FuzzTable.cs
//
using System;
using System.Text.RegularExpressions;

public class FuzzTable
{
    private static Random random = new();
    private static Regex parenContents = null;
    private static TimeSpan timeout = new(10*10000/*x10nanoseconds*/);

    //TODO: an instance keeps an array of alternates and an MRU list

    // Evaluate a single fuzz string.
    //   "(He|She|They) (picked|selected) a (green|red|blue) (ball|block)."
    //   "She picked a red ball."
    // Replace any parenthetical phrase with one of its choices at random.
    // Allows for nesting of choices.  There's currently no way to escape
    // parentheses or vertical bars so strings must not include strays.
    // Returns one permutation from all choice alternatives given.
    // Does not remember what choices were given.
    //
    public static string Evaluate(string fuzz)
    {
        if (string.IsNullOrEmpty(fuzz))
            return "";
		if (parenContents == null)
			parenContents =
				new(@"\( ( [^(]*? ) \)",
					RegexOptions.IgnorePatternWhitespace |
					RegexOptions.Compiled,
					timeout);
		string before = null;
		while (fuzz != before)
		{
			before = fuzz;
            try
            {
    			fuzz = parenContents.Replace(fuzz,
    				(m) => PickAlternate(m.Groups[1].ToString()));
            }
            catch (RegexMatchTimeoutException)
            {
                break;
            }
		}
		return fuzz;
    }

    private static string PickAlternate(string fuzz)
    {
        if (string.IsNullOrEmpty(fuzz))
            return "";
        string[] alternates = fuzz.Split('|');
        if (alternates == null || alternates.Length == 0)
            return "";
        int pick = random.Next(0, alternates.Length);
        return alternates[pick];
    }

}
