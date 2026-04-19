using DiscordBot.Utils;

namespace DiscordBot.Tests.Utils;

public class StringUtilTests
{
    [Theory]
    [InlineData("$100", true)]
    [InlineData("100$", true)]
    [InlineData("USD 50", true)]
    [InlineData("€200", true)]
    [InlineData("50 GBP", true)]
    [InlineData("100 euros", true)]
    [InlineData("£30", true)]
    [InlineData("hello world", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContainsCurrencySymbol_ReturnsExpected(string? input, bool expected)
    {
        Assert.Equal(expected, input!.ContainsCurrencySymbol());
    }

    [Theory]
    [InlineData("This is a rev-share project", true)]
    [InlineData("Looking for revshare", true)]
    [InlineData("Doing rev share work", true)]
    [InlineData("Revenue sharing model", false)]
    [InlineData("hello world", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContainsRevShare_ReturnsExpected(string? input, bool expected)
    {
        Assert.Equal(expected, input!.ContainsRevShare());
    }

    [Fact]
    public void SanitizeEveryoneHereMentions_SanitizesEveryone()
    {
        var result = "Hey @everyone check this out".SanitizeEveryoneHereMentions();
        Assert.Contains("@\u200beveryone", result);
        Assert.Equal("Hey @\u200beveryone check this out", result);
    }

    [Fact]
    public void SanitizeEveryoneHereMentions_SanitizesHere()
    {
        var result = "Hey @here check this out".SanitizeEveryoneHereMentions();
        Assert.Contains("@\u200bhere", result);
        Assert.Equal("Hey @\u200bhere check this out", result);
    }

    [Fact]
    public void SanitizeEveryoneHereMentions_NoMentions_Unchanged()
    {
        var input = "Hello world";
        Assert.Equal(input, input.SanitizeEveryoneHereMentions());
    }
}
