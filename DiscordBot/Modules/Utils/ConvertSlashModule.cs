using Discord.Interactions;
using DiscordBot.Utils;

namespace DiscordBot.Modules.Utils;

public class ConvertSlashModule : InteractionModuleBase<SocketInteractionContext>
{
    private const string DefaultCurrency = "usd";
    private const float ConversionError = -1;

    public CurrencyService CurrencyService { get; set; } = null!;

    [SlashCommand("ftoc", "Convert a temperature from fahrenheit to celsius")]
    public async Task FahrenheitToCelsius(
        [Summary("fahrenheit", "Temperature in degrees fahrenheit")] double fahrenheit)
    {
        var celsius = MathUtility.FahrenheitToCelsius((float)fahrenheit);
        if (!float.IsFinite(celsius))
        {
            await Context.Interaction.RespondAsync("❌ That temperature is out of range.", ephemeral: true);
            return;
        }

        await Context.Interaction.RespondAsync($"{fahrenheit}°F is {celsius}°C.");
    }

    [SlashCommand("ctof", "Convert a temperature from celsius to fahrenheit")]
    public async Task CelsiusToFahrenheit(
        [Summary("celsius", "Temperature in degrees celsius")] double celsius)
    {
        var fahrenheit = MathUtility.CelsiusToFahrenheit((float)celsius);
        if (!float.IsFinite(fahrenheit))
        {
            await Context.Interaction.RespondAsync("❌ That temperature is out of range.", ephemeral: true);
            return;
        }

        await Context.Interaction.RespondAsync($"{celsius}°C is {fahrenheit}°F.");
    }

    [SlashCommand("curr", "Convert an amount from one currency to another")]
    public async Task ConvertCurrency(
        [Summary("from", "Currency code to convert from, for example USD")]
        [Autocomplete(typeof(CurrencyAutocompleteHandler))] string from,
        [Summary("amount", "Amount to convert (default: 1)")] double amount = 1,
        [Summary("to", "Currency code to convert to (default: USD)")]
        [Autocomplete(typeof(CurrencyAutocompleteHandler))] string to = DefaultCurrency)
    {
        from = from.Trim().ToLowerInvariant();
        to = to.Trim().ToLowerInvariant();

        if (!await CurrencyService.IsCurrency(from) || !await CurrencyService.IsCurrency(to))
        {
            await Context.Interaction.RespondAsync("❌ One of the currencies provided is invalid.", ephemeral: true);
            return;
        }

        var rate = await CurrencyService.GetConversion(to, from);
        if (Math.Abs(rate - ConversionError) < 0.01)
        {
            await Context.Interaction.RespondAsync("❌ An error occured while converting the currency, the API may be down!",
                ephemeral: true);
            return;
        }

        var totalAmount = Math.Round(amount * rate, 2);
        await Context.Interaction.RespondAsync(
            $"**{amount} {from.ToUpperInvariant()}** = **{totalAmount} {to.ToUpperInvariant()}**");
    }
}
