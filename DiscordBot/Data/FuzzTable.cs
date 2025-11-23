// FuzzTable.cs
//
using System;
using System.Text.RegularExpressions;

namespace DiscordBot.Data;

// A simple random string picking engine.
//
// Load it up like a list with string choices that can be picked from.
// Remember recently-picked choices so they don't repeat too soon.
// Individual string choices can also be evaluated with a simple syntax
// to further allow for alternative wordings, such as humanized messages.
//
// Evaluating "(He|She|They) (picked|chose) a (green|red|blue) (ball|block)."
// might return "She picked a red ball."
//
public class FuzzTable
{
    private static Random random = new();
    private static Regex parenContents = null;
    private static TimeSpan timeout = new(10*10000/*x10nanoseconds*/);

	private List<string> choices = new();
	private Queue<string> recent = new();

	public void Clear()
	{
		choices.Clear();
		recent.Clear();
	}

	// Add a string as a valid choice from which to pick.
	// Note that empty strings or whitespace can be added manually as valid choices.
	// Duplicate choices are also allowed for weighting.
	//
	public void Add(string choice)
	{
		choices.Add(choice);
	}

	// Load a file of string choices.
	// Lines starting with a '#' character are ignored, as are blank lines.
	// Each remaining line of the file is trimmed of leading and trailing whitespace.
	// Each line is added as a new choice, and duplicates are allowed for weighting.
	//
	public void Load(string filename)
	{
		foreach (string line in File.ReadLines(filename))
		{
			string choice = line.Trim();
			if (choice.StartsWith('#'))
				continue;
			Add(choice);
		}
	}

	// Pick one of the active choices.
	// This choice is transferred to the MRU so it's not picked again too soon.
	// If the evaluate flag is given, further Evaluate() it as a fuzz string.
	// Returns the chosen results, or the empty string if no choices available.
	//
	public string Pick(bool evaluate=false)
	{
		Recycle();
		if (choices.Count == 0)
			return "";
        int pick = random.Next(0, choices.Count);
		string chosen = choices[pick];
		choices.RemoveAt(pick);
		recent.Enqueue(chosen);
		if (evaluate)
	        return Evaluate(chosen);
		return chosen;
	}

	// When the MRU gets too long, return the oldest MRU choice(s) back
	// to the active list of choices.
	//
	private void Recycle()
	{
		// Caps the MRU at half of total choices.
		while (recent.Count > choices.Count)
		{
			string choice = recent.Dequeue();
			choices.Add(choice);
		}
	}
	
    // Evaluate a single fuzz string.
    // Replace any parenthetical phrase with one of its choices at random.
    // Allows for nesting of choices.  There's currently no way to escape
    // parentheses or vertical bars so strings must not include strays.
    // Returns one permutation from all choice alternatives given.
    // There is no MRU of individual permutations given.
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
			fuzz = parenContents.Replace(fuzz,
				(m) => PickAlternate(m.Groups[1].ToString()));
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

