using DiscordBot.Domain.Dice;

namespace DiscordBot.Modules.Fun;

/// <summary>Builds the reply text for a dice roll.</summary>
public static class FunRollFormatter
{
    /// <summary>Words the reply uses for each number of dice, indexed by dice count up to <see cref="DiceExpression.MaxDicePerTerm"/>.</summary>
    private static readonly string[] DiceCountWords =
    [
        "no", "a", "a pair of", "three", "four", "five",
        "six", "seven", "eight", "nine", "ten",
    ];

    public static string Format(string userName, DiceExpressionResult result)
    {
        var expression = result.Expression;

        if (expression.IsSimple)
        {
            var term = expression.Terms[0];

            if (term.Count == 1)
            {
                // Keeps the wording the original text command used for a plain roll.
                return result.IsNatural
                    ? $"**{userName}** rolled a D{term.Sides} and got a natural **{result.Total}**!"
                    : $"**{userName}** rolled a D{term.Sides} and got **{result.Total}**!";
            }

            var faces = result.Dice[0].Select(face => face.ToString()).ToArray();
            return $"**{userName}** rolled {DiceCountWords[term.Count]} D{term.Sides} showing {faces.ToCommaList()} " +
                   $"for a total of **{result.Total}**!";
        }

        // Expressions holding several terms or a flat modifier spell out every die so the total is checkable.
        var breakdown = new List<string>(expression.Terms.Count);
        for (var i = 0; i < expression.Terms.Count; i++)
        {
            var dice = $"[{string.Join(", ", result.Dice[i])}]";
            if (result.IsNatural)
                dice += " (natural)";

            breakdown.Add(dice);
        }

        var modifier = expression.Modifier switch
        {
            > 0 => $" + {expression.Modifier}",
            < 0 => $" - {Math.Abs(expression.Modifier)}",
            _ => string.Empty,
        };

        return $"**{userName}** rolled {expression} showing {string.Join(" + ", breakdown)}{modifier} " +
               $"for a total of **{result.Total}**!";
    }
}
