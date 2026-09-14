using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DiscordBot.Domain.Dice;

/// <summary>A single <c>NdM</c> dice term, for example <c>3d6</c>.</summary>
public sealed record DiceTerm(int Count, int Sides);

/// <summary>
/// A parsed dice expression made of one or more equally-sided dice terms plus a net flat modifier,
/// for example <c>2d6+4</c> or <c>1d20+1d4-1</c>.
/// </summary>
public sealed class DiceExpression
{
    public const int MinSides = 2;
    public const int MaxSides = 1000;
    public const int MaxDicePerTerm = 10;
    public const int MaxTerms = 10;
    public const int MaxModifier = 1000;

    private readonly DiceTerm[] _terms;

    private DiceExpression(DiceTerm[] terms, int modifier)
    {
        _terms = terms;
        Modifier = modifier;
    }

    public IReadOnlyList<DiceTerm> Terms => _terms;
    public int Modifier { get; }
    public int DiceCount => _terms.Sum(term => term.Count);
    public bool HasModifier => Modifier != 0;

    /// <summary>A single dice term with no modifier, the shape that keeps the original reply wording.</summary>
    public bool IsSimple => _terms.Length == 1 && !HasModifier;

    public bool IsSingleDie => DiceCount == 1;

    /// <summary>Normalised form of the expression, for example <c>2d6+4</c> or <c>1d20</c>.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var term in _terms)
        {
            if (builder.Length > 0)
                builder.Append('+');
            builder.Append(term.Count).Append('d').Append(term.Sides);
        }

        if (Modifier > 0)
            builder.Append('+').Append(Modifier);
        else if (Modifier < 0)
            builder.Append(Modifier);

        return builder.ToString();
    }

    /// <summary>Rolls every term and applies the flat modifier.</summary>
    public DiceExpressionResult Roll(Random random)
    {
        var dice = new int[_terms.Length][];

        for (var t = 0; t < _terms.Length; t++)
        {
            var term = _terms[t];
            var faces = new int[term.Count];
            for (var i = 0; i < term.Count; i++)
                faces[i] = random.Next(1, term.Sides + 1);

            dice[t] = faces;
        }

        return new DiceExpressionResult(this, dice);
    }

    public static bool TryParse(string? text, [NotNullWhen(true)] out DiceExpression? expression) =>
        TryParse(text, out expression, out _);

    /// <summary>
    /// Parses dice notation. Dice terms are <c>NdM</c>, <c>dM</c> or a bare count of sides; a bare number
    /// beside other terms is a flat modifier, so <c>2d6+4</c> is two d6 plus four.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out DiceExpression? expression, out string error)
    {
        expression = null;
        error = string.Empty;

        var input = new string((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (input.Length == 0)
        {
            error = "No dice specified.";
            return false;
        }

        // Legacy shape: a lone number is the number of sides of a single die, so "20" means 1d20.
        if (input.All(char.IsAsciiDigit))
        {
            if (!int.TryParse(input, out var sides) || sides < MinSides || sides > MaxSides)
            {
                error = $"A number of sides must be between {MinSides} and {MaxSides}.";
                return false;
            }

            expression = new DiceExpression([new DiceTerm(1, sides)], 0);
            return true;
        }

        var terms = new List<DiceTerm>();
        var modifier = 0;
        var sign = 1;
        var lastWasOperator = false;
        var index = 0;

        while (index < input.Length)
        {
            var c = input[index];
            if (c is '+' or '-')
            {
                if (lastWasOperator)
                {
                    error = "Operators must be separated by dice or a number.";
                    return false;
                }

                sign = c == '-' ? -1 : 1;
                lastWasOperator = true;
                index++;
                continue;
            }

            var start = index;
            while (index < input.Length && input[index] is not ('+' or '-'))
                index++;

            var token = input[start..index];
            if (!TryParseTerm(token, out var term, out var flat, out error))
                return false;

            if (term is not null)
                terms.Add(term);
            else
                modifier += sign * flat;

            sign = 1;
            lastWasOperator = false;
        }

        if (lastWasOperator)
        {
            error = "The expression ends with an operator.";
            return false;
        }

        if (terms.Count == 0)
        {
            error = "The expression needs at least one dice term, for example 2d6 or d20.";
            return false;
        }

        if (terms.Count > MaxTerms)
        {
            error = $"An expression can hold at most {MaxTerms} dice terms.";
            return false;
        }

        if (Math.Abs((long)modifier) > MaxModifier)
        {
            error = $"The flat modifier must be within +/-{MaxModifier}.";
            return false;
        }

        expression = new DiceExpression(terms.ToArray(), modifier);
        return true;
    }

    private static bool TryParseTerm(string token, out DiceTerm? term, out int flat, out string error)
    {
        term = null;
        flat = 0;
        error = string.Empty;

        var separator = token.IndexOfAny(['d', 'D']);
        if (separator < 0)
        {
            if (!int.TryParse(token, out flat))
            {
                error = $"'{token}' is not a valid number of sides or modifier.";
                return false;
            }

            return true;
        }

        var countText = token[..separator];
        var sidesText = token[(separator + 1)..];

        var count = 1;
        if (countText.Length > 0 && !int.TryParse(countText, out count))
        {
            error = $"'{countText}' is not a valid number of dice.";
            return false;
        }

        if (sidesText.Length == 0)
        {
            error = "Expected a number of sides after 'd', for example 2d6.";
            return false;
        }

        if (!int.TryParse(sidesText, out var sides))
        {
            error = $"'{sidesText}' is not a valid number of sides.";
            return false;
        }

        if (count < 1 || count > MaxDicePerTerm)
        {
            error = $"Number of dice must be between 1 and {MaxDicePerTerm}.";
            return false;
        }

        if (sides < MinSides || sides > MaxSides)
        {
            error = $"Number of sides must be between {MinSides} and {MaxSides}.";
            return false;
        }

        term = new DiceTerm(count, sides);
        return true;
    }
}
