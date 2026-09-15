using DiscordBot.Domain.Dice;
using DiscordBot.Modules.Fun;

namespace DiscordBot.Tests.Modules.Fun;

public class FunRollFormatterTests
{
    private const string User = "Pierre";

    private static DiceExpressionResult Result(string expression, int[][] dice)
    {
        Assert.True(DiceExpression.TryParse(expression, out var parsed), $"Expected '{expression}' to parse.");
        return new DiceExpressionResult(parsed!, dice);
    }

    [Fact]
    public void SingleDie_UsesLegacyWording()
    {
        var result = Result("1d6", [[4]]);

        Assert.Equal("**Pierre** rolled a D6 and got **4**!", FunRollFormatter.Format(User, result));
    }

    [Theory]
    [InlineData(20, 20)]
    [InlineData(1, 1)]
    public void SingleDie_Natural_UsesLegacyWording(int face, int total)
    {
        var result = Result("1d20", [[face]]);

        Assert.Equal($"**Pierre** rolled a D20 and got a natural **{total}**!", FunRollFormatter.Format(User, result));
    }

    [Fact]
    public void MultipleDice_ListsEveryFaceWithCountWord()
    {
        var result = Result("2d6", [[2, 5]]);

        Assert.Equal("**Pierre** rolled a pair of D6 showing 2 and 5 for a total of **7**!",
            FunRollFormatter.Format(User, result));
    }

    [Fact]
    public void MultipleDice_JoinsFacesWithConjunction()
    {
        var result = Result("6d6", [[1, 2, 3, 4, 5, 6]]);

        Assert.Equal("**Pierre** rolled six D6 showing 1, 2, 3, 4, 5, and 6 for a total of **21**!",
            FunRollFormatter.Format(User, result));
    }

    [Fact]
    public void DiceWithModifier_ShowsFacesAndModifier()
    {
        var result = Result("2d6+4", [[2, 5]]);

        Assert.Equal("**Pierre** rolled 2d6+4 showing [2, 5] + 4 for a total of **11**!",
            FunRollFormatter.Format(User, result));
    }

    [Fact]
    public void MultipleTermsAndNegativeModifier_ShowsEachTerm()
    {
        var result = Result("1d20+1d4-1", [[15], [3]]);

        Assert.Equal("**Pierre** rolled 1d20+1d4-1 showing [15] + [3] - 1 for a total of **17**!",
            FunRollFormatter.Format(User, result));
    }

    [Fact]
    public void NaturalDieWithModifier_MarksTheDieNotTheTotal()
    {
        var result = Result("1d20+5", [[20]]);

        Assert.Equal("**Pierre** rolled 1d20+5 showing [20] (natural) + 5 for a total of **25**!",
            FunRollFormatter.Format(User, result));
    }

    [Fact]
    public void NonNaturalDieWithModifier_DoesNotMentionNatural()
    {
        var result = Result("1d20+5", [[7]]);

        Assert.Equal("**Pierre** rolled 1d20+5 showing [7] + 5 for a total of **12**!",
            FunRollFormatter.Format(User, result));
    }

    [Fact]
    public void MultipleTermsWithModifier_ShowsEveryTerm()
    {
        var result = Result("2d6+1d4-2", [[2, 5], [3]]);

        Assert.Equal("**Pierre** rolled 2d6+1d4-2 showing [2, 5] + [3] - 2 for a total of **8**!",
            FunRollFormatter.Format(User, result));
    }

    [Fact]
    public void Format_DisplaysTotalConsistentWithFaces()
    {
        var result = Result("2d6+4", [[1, 1]]);

        Assert.Equal(6, result.Total);
        Assert.Contains("for a total of **6**!", FunRollFormatter.Format(User, result));
    }

    [Fact]
    public void Format_UsesEveryDiceCountWord()
    {
        for (var count = 2; count <= DiceExpression.MaxDicePerTerm; count++)
        {
            var faces = Enumerable.Repeat(1, count).ToArray();
            var result = Result($"{count}d6", [faces]);

            var message = FunRollFormatter.Format(User, result);

            Assert.Contains("showing", message);
            Assert.Contains($"for a total of **{count}**!", message);
        }
    }
}
