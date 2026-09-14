namespace DiscordBot.Domain.Dice;

/// <summary>The individual faces produced by rolling a <see cref="DiceExpression"/>, plus its total.</summary>
public sealed class DiceExpressionResult
{
    /// <param name="expression">The expression that was rolled.</param>
    /// <param name="dice">Faces rolled, one array per term of <paramref name="expression"/>.</param>
    public DiceExpressionResult(DiceExpression expression, int[][] dice)
    {
        Expression = expression;
        Dice = dice;
        Total = expression.Modifier + dice.SelectMany(faces => faces).Sum();

        if (expression.IsSingleDie && dice.Length == 1 && dice[0].Length == 1)
        {
            var face = dice[0][0];
            if (face == 1 || face == expression.Terms[0].Sides)
            {
                IsNatural = true;
                NaturalFace = face;
            }
        }
    }

    public DiceExpression Expression { get; }

    /// <summary>Faces rolled, one array per term of <see cref="Expression"/>.</summary>
    public IReadOnlyList<int[]> Dice { get; }

    public int Total { get; }

    /// <summary>True when a single die came up as either 1 or its highest face.</summary>
    public bool IsNatural { get; }

    /// <summary>The face that made <see cref="IsNatural"/> true, otherwise 0.</summary>
    public int NaturalFace { get; }
}
