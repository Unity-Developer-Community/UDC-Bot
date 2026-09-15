using DiscordBot.Modules.Utils;

namespace DiscordBot.Tests.Modules.Utils;

public class CurrencyAutocompleteHandlerTests
{
    private static readonly Dictionary<string, string> Currencies = new()
    {
        ["usd"] = "United States Dollar",
        ["eur"] = "Euro",
        ["sek"] = "Swedish Krona",
        ["jpy"] = "Japanese Yen",
    };

    [Fact]
    public void BuildSuggestions_EmptyInput_ReturnsEveryCurrency()
    {
        var suggestions = CurrencyAutocompleteHandler.BuildSuggestions(Currencies, "");

        Assert.Equal(4, suggestions.Count);
        Assert.All(suggestions, suggestion => Assert.NotNull(suggestion.Value));
    }

    [Fact]
    public void BuildSuggestions_NullInput_ReturnsEveryCurrency()
    {
        Assert.Equal(4, CurrencyAutocompleteHandler.BuildSuggestions(Currencies, null).Count);
    }

    [Theory]
    [InlineData("sek")]
    [InlineData("SEK")]
    [InlineData("  sek  ")]
    public void BuildSuggestions_MatchesCodePrefixCaseInsensitively(string input)
    {
        var suggestion = Assert.Single(CurrencyAutocompleteHandler.BuildSuggestions(Currencies, input));

        Assert.Equal("sek", suggestion.Value);
        Assert.Equal("SEK - Swedish Krona", suggestion.Name);
    }

    [Fact]
    public void BuildSuggestions_MatchesByNameWhenCodeDoesNotMatch()
    {
        var suggestion = Assert.Single(CurrencyAutocompleteHandler.BuildSuggestions(Currencies, "krona"));

        Assert.Equal("sek", suggestion.Value);
    }

    [Fact]
    public void BuildSuggestions_PrefersCodeMatchesOverNameMatches()
    {
        // "aa" only matches because its name contains "bb"; "bb" matches on its code.
        var currencies = new Dictionary<string, string>
        {
            ["aa"] = "Contains bb inside",
            ["bb"] = "Bee",
        };

        var suggestions = CurrencyAutocompleteHandler.BuildSuggestions(currencies, "bb");

        Assert.Equal(new[] { "bb", "aa" }, suggestions.Select(suggestion => suggestion.Value));
    }

    [Fact]
    public void BuildSuggestions_NoMatch_ReturnsEmpty()
    {
        Assert.Empty(CurrencyAutocompleteHandler.BuildSuggestions(Currencies, "zzz"));
    }

    [Fact]
    public void BuildSuggestions_OrdersByCode()
    {
        var suggestions = CurrencyAutocompleteHandler.BuildSuggestions(Currencies, "");

        Assert.Equal(new[] { "eur", "jpy", "sek", "usd" }, suggestions.Select(s => s.Value));
    }

    [Fact]
    public void BuildSuggestions_IsCappedAtDiscordLimit()
    {
        var many = Enumerable.Range(0, 60).ToDictionary(i => $"c{i:00}", i => $"Currency {i}");
        var suggestions = CurrencyAutocompleteHandler.BuildSuggestions(many, "");

        Assert.Equal(25, suggestions.Count);
    }
}
