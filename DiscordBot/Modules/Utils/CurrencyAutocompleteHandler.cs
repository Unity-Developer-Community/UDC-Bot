using Discord.Interactions;

namespace DiscordBot.Modules.Utils;

/// <summary>Suggests currency codes for the options of <see cref="ConvertSlashModule"/>.</summary>
public class CurrencyAutocompleteHandler : AutocompleteHandler
{
    /// <summary>Discord rejects an autocomplete response holding more than 25 choices.</summary>
    private const int MaxSuggestions = 25;

    public CurrencyService CurrencyService { get; set; } = null!;

    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter, IServiceProvider services)
    {
        var input = autocompleteInteraction.Data.Current.Value as string ?? string.Empty;
        var currencies = await CurrencyService.GetCurrenciesAsync();

        return AutocompletionResult.FromSuccess(BuildSuggestions(currencies, input));
    }

    /// <summary>
    /// Matches currencies whose code starts with <paramref name="input"/>, or whose name contains it, so
    /// that both <c>sek</c> and <c>krona</c> find the Swedish krona.
    /// </summary>
    public static IReadOnlyList<AutocompleteResult> BuildSuggestions(
        IReadOnlyDictionary<string, string> currencies, string? input)
    {
        var query = input?.Trim() ?? string.Empty;

        var codeMatches = new List<AutocompleteResult>();
        var nameMatches = new List<AutocompleteResult>();

        foreach (var (code, name) in currencies.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var result = new AutocompleteResult($"{code.ToUpperInvariant()} - {name}", code);

            if (code.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                codeMatches.Add(result);
            else if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                nameMatches.Add(result);
        }

        return codeMatches.Concat(nameMatches).Take(MaxSuggestions).ToList();
    }
}
