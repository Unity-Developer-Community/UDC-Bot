using DiscordBot.Extensions;

namespace DiscordBot.Tests.Extensions;

public class StringExtensionsTests
{
    [Theory]
    [InlineData(null, 5, null)]
    [InlineData("", 5, "")]
    [InlineData("abc", 5, "abc")]
    [InlineData("abcde", 5, "abcde")]
    [InlineData("abcdef", 5, "abcde")]
    public void Truncate_ReturnsExpected(string? input, int max, string? expected)
    {
        Assert.Equal(expected, input!.Truncate(max));
    }

    [Fact]
    public void CalculateLevenshteinDistance_IdenticalStrings_ReturnsZero()
    {
        Assert.Equal(0, "kitten".CalculateLevenshteinDistance("kitten"));
    }

    [Fact]
    public void CalculateLevenshteinDistance_EmptySource_ReturnsTargetLength()
    {
        Assert.Equal(5, "".CalculateLevenshteinDistance("hello"));
    }

    [Fact]
    public void CalculateLevenshteinDistance_EmptyTarget_ReturnsSourceLength()
    {
        Assert.Equal(5, "hello".CalculateLevenshteinDistance(""));
    }

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("saturday", "sunday", 3)]
    public void CalculateLevenshteinDistance_KnownPairs(string a, string b, int expected)
    {
        Assert.Equal(expected, a.CalculateLevenshteinDistance(b));
    }

    [Fact]
    public void MessageSplit_ShortString_ReturnsSingleElement()
    {
        var result = "hello\nworld".MessageSplit(100);
        Assert.Single(result);
    }

    [Fact]
    public void MessageSplit_LongString_SplitsAtNewlines()
    {
        var input = string.Join("\n", Enumerable.Range(1, 50).Select(i => new string('x', 50)));
        var result = input.MessageSplit(200);
        Assert.True(result.Count > 1);
    }

    [Fact]
    public void EscapeDiscordMarkup_EscapesSpecialChars()
    {
        var result = "hello *world* ~test~ __under__ `code`".EscapeDiscordMarkup();
        Assert.Contains(@"\*", result);
        Assert.Contains(@"\~", result);
        Assert.Contains(@"\_", result);
        Assert.Contains(@"\`", result);
    }

    [Fact]
    public void EscapeDiscordMarkup_NoSpecialChars_Unchanged()
    {
        Assert.Equal("hello world", "hello world".EscapeDiscordMarkup());
    }

    [Theory]
    [InlineData("HELLO WORLD!", true)]
    [InlineData("ABC 123", false)]
    [InlineData("Hello", false)]
    [InlineData("ALL CAPS!!!", true)]
    public void IsAllCaps_ReturnsExpected(string input, bool expected)
    {
        Assert.Equal(expected, input.IsAllCaps());
    }

    [Theory]
    [InlineData("hello", "Hello")]
    [InlineData("", "")]
    [InlineData("A", "A")]
    [InlineData("abc", "Abc")]
    public void ToCapitalizeFirstLetter_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, input.ToCapitalizeFirstLetter());
    }

    [Fact]
    public void ToCapitalizeFirstLetter_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ((string)null!).ToCapitalizeFirstLetter());
    }

    [Fact]
    public void ToCommaList_SingleItem()
    {
        Assert.Equal("apples", new[] { "apples" }.ToCommaList());
    }

    [Fact]
    public void ToCommaList_TwoItems()
    {
        Assert.Equal("apples and bananas", new[] { "apples", "bananas" }.ToCommaList());
    }

    [Fact]
    public void ToCommaList_ThreeItems()
    {
        Assert.Equal("apples, bananas, and cherries", new[] { "apples", "bananas", "cherries" }.ToCommaList());
    }

    [Fact]
    public void ToCommaList_CustomConjunction()
    {
        Assert.Equal("apples or bananas", new[] { "apples", "bananas" }.ToCommaList("or"));
    }

    [Fact]
    public void GetSha256_KnownInput_ProducesConsistentHash()
    {
        var hash1 = "test".GetSha256();
        var hash2 = "test".GetSha256();
        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
    }

    [Fact]
    public void GetSha256_DifferentInputs_ProduceDifferentHashes()
    {
        Assert.NotEqual("abc".GetSha256(), "def".GetSha256());
    }
}
