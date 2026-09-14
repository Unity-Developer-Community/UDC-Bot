using System.Text;

namespace DiscordBot.Modules.Profiles;

/// <summary>Builds the monospace body of a leaderboard embed.</summary>
public static class RankEmbedFormatter
{
    /// <summary>Name padding uses an en quad so it lines up inside the embed's monospace block.</summary>
    private const char PaddingCharacter = '\u2000';

    /// <summary>
    /// Formats one line per row, padding rank and name so the values line up. Returns an empty string for
    /// no rows, since the rank width is derived from the count with a logarithm.
    /// </summary>
    public static string BuildDescription(IReadOnlyList<(string Name, int Value)> rows, string labelName)
    {
        if (rows.Count == 0)
            return string.Empty;

        var rankWidth = (int)Math.Floor(Math.Log10(rows.Count)) + 1;
        var nameWidth = rows.Max(row => row.Name.Length);
        var builder = new StringBuilder();

        for (var i = 0; i < rows.Count; i++)
        {
            var rank = (i + 1).ToString().PadLeft(rankWidth);
            var name = rows[i].Name.PadRight(nameWidth, PaddingCharacter);
            builder.Append($"`{rank}.` **`{name}`** `{labelName}: {rows[i].Value}`\n");
        }

        return builder.ToString();
    }
}
