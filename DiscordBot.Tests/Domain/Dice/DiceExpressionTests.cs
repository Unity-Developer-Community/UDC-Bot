using DiscordBot.Domain.Dice;

namespace DiscordBot.Tests.Domain.Dice;

public class DiceExpressionTests
{
    private static DiceExpression Parse(string text)
    {
        Assert.True(DiceExpression.TryParse(text, out var expression), $"Expected '{text}' to parse.");
        return expression!;
    }

    private static int SumFaces(DiceExpressionResult result) =>
        result.Dice.SelectMany(faces => faces).Sum();

    [Theory]
    [InlineData("2d6", 2, 6, 0)]
    [InlineData("d20", 1, 20, 0)]
    [InlineData("1d20", 1, 20, 0)]
    [InlineData("10d1000", 10, 1000, 0)]
    [InlineData("2d6+4", 2, 6, 4)]
    [InlineData("1d20-2", 1, 20, -2)]
    public void TryParse_SingleTermExpressions(string text, int count, int sides, int modifier)
    {
        var expression = Parse(text);

        var term = Assert.Single(expression.Terms);
        Assert.Equal(count, term.Count);
        Assert.Equal(sides, term.Sides);
        Assert.Equal(modifier, expression.Modifier);
    }

    [Fact]
    public void TryParse_MultipleTerms_SumsModifiers()
    {
        var expression = Parse("1d20+1d4-1");

        Assert.Equal(2, expression.Terms.Count);
        Assert.Equal(new DiceTerm(1, 20), expression.Terms[0]);
        Assert.Equal(new DiceTerm(1, 4), expression.Terms[1]);
        Assert.Equal(-1, expression.Modifier);
    }

    [Theory]
    [InlineData("20", 20)]
    [InlineData("6", 6)]
    public void TryParse_BareNumber_IsLegacySingleDie(string text, int sides)
    {
        var expression = Parse(text);

        var term = Assert.Single(expression.Terms);
        Assert.Equal(1, term.Count);
        Assert.Equal(sides, term.Sides);
        Assert.True(expression.IsSimple);
    }

    [Theory]
    [InlineData("2d6 + 4", "2d6+4")]
    [InlineData("2D6+4", "2d6+4")]
    [InlineData("  2d6+4  ", "2d6+4")]
    public void TryParse_IsWhitespaceAndCaseInsensitive(string text, string expected)
    {
        Assert.Equal(expected, Parse(text).ToString());
    }

    [Theory]
    [InlineData("2d6", "2d6")]
    [InlineData("1d20", "1d20")]
    [InlineData("2d6+4", "2d6+4")]
    [InlineData("2d6-1", "2d6-1")]
    [InlineData("1d20+1d4-1", "1d20+1d4-1")]
    public void ToString_NormalisesExpression(string text, string expected)
    {
        Assert.Equal(expected, Parse(text).ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("1d1")]
    [InlineData("0d6")]
    [InlineData("11d6")]
    [InlineData("d")]
    [InlineData("2d")]
    [InlineData("2d6+")]
    [InlineData("2d6++4")]
    [InlineData("2d6+2000")]
    [InlineData("4+4")]
    [InlineData("2x6")]
    [InlineData("2d6d4")]
    public void TryParse_InvalidInput_ReturnsFalse(string? text)
    {
        Assert.False(DiceExpression.TryParse(text, out var expression));
        Assert.Null(expression);
    }

    [Fact]
    public void TryParse_InvalidInput_ExplainsWhy()
    {
        Assert.False(DiceExpression.TryParse("4+4", out _, out var error));
        Assert.Contains("at least one dice term", error);
    }

    [Fact]
    public void Roll_AppliesModifierToTotal()
    {
        var expression = Parse("2d6+4");
        var result = expression.Roll(new Random(1));

        var faces = Assert.Single(result.Dice);
        Assert.Equal(2, faces.Length);
        Assert.Equal(faces.Sum() + 4, result.Total);
        Assert.Equal(2, result.Expression.DiceCount);
    }

    [Fact]
    public void Roll_ReturnsOneFaceArrayPerTerm()
    {
        var result = Parse("2d6+1d4").Roll(new Random(7));

        Assert.Equal(2, result.Dice.Count);
        Assert.Equal(2, result.Dice[0].Length);
        Assert.Single(result.Dice[1]);
    }

    [Fact]
    public void Roll_MultipleTerms_CombinesEveryTerm()
    {
        var expression = Parse("1d20+1d4-1");
        var result = expression.Roll(new Random(2));

        Assert.Equal(2, result.Dice.Count);
        Assert.Single(result.Dice[0]);
        Assert.Single(result.Dice[1]);
        Assert.Equal(SumFaces(result) - 1, result.Total);
    }

    [Fact]
    public void Roll_FacesStayWithinSides()
    {
        var expression = Parse("10d1000");
        var random = new Random(3);

        for (var i = 0; i < 100; i++)
        {
            var result = expression.Roll(random);
            foreach (var face in result.Dice.SelectMany(faces => faces))
            {
                Assert.InRange(face, 1, 1000);
            }
        }
    }

    [Fact]
    public void Roll_MultiDie_IsNeverNatural()
    {
        var expression = Parse("2d6");
        var result = expression.Roll(new Random(4));

        Assert.False(result.IsNatural);
        Assert.Equal(0, result.NaturalFace);
    }

    [Fact]
    public void Roll_MultiTerm_IsNeverNatural()
    {
        var expression = Parse("1d20+1d4");
        var result = expression.Roll(new Random(5));

        Assert.False(result.IsNatural);
        Assert.Equal(0, result.NaturalFace);
    }

    [Fact]
    public void Roll_SingleDie_MarksNaturalOnOneOrHighestFaceOnly()
    {
        var expression = Parse("d20");
        var random = new Random(6);
        var naturals = 0;

        // A d20 is natural 10% of the time, so 5000 rolls essentially guarantee at least one.
        for (var i = 0; i < 5000; i++)
        {
            var result = expression.Roll(random);
            var face = result.Dice[0][0];

            Assert.Equal(face == 1 || face == 20, result.IsNatural);
            Assert.Equal(result.IsNatural ? face : 0, result.NaturalFace);

            if (result.IsNatural)
                naturals++;
        }

        Assert.True(naturals > 0, "Expected at least one natural roll across 5000 attempts.");
    }

    [Fact]
    public void IsSimple_OnlyForSingleTermWithoutModifier()
    {
        Assert.True(Parse("3d6").IsSimple);
        Assert.False(Parse("3d6+1").IsSimple);
        Assert.False(Parse("3d6+1d4").IsSimple);
    }
}
