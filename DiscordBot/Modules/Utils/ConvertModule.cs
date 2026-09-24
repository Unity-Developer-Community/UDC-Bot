using Discord.Commands;
using DiscordBot.Attributes;

namespace DiscordBot.Modules.Utils;

// Allows UserModule !help to show commands from this module
[Group("UserModule"), Alias("")]
public class ConvertModule : ModuleBase
{
    private const string DefaultCurrency = "usd";
    private const float ConversionError = -1;

    public CurrencyService CurrencyService { get; set; } = null!;

    [Command("Currency"), HideFromHelp]
    [Summary("Converts a currency. Syntax : !curr fromCurrency toCurrency")]
    [Alias("curr")]
    public async Task ConvertCurrency(string from, string to = DefaultCurrency)
    {
        await ConvertCurrency(1, from, to);
    }

    [Command("Currency"), Priority(29)]
    [Summary("Converts a currency. Syntax : !curr amount fromCurrency toCurrency")]
    [Alias("curr")]
    public async Task ConvertCurrency(double amount, string from, string to = DefaultCurrency)
    {
        // A text command can carry mentions, so ignore the ones that would ping someone else.
        if (Context.HasAnyPingableMention())
        {
            if (!Context.IsReply())
                return;
            if (!Context.IsOnlyReplyingToAuthor())
                return;
        }

        from = from.Trim().ToLowerInvariant();
        to = to.Trim().ToLowerInvariant();

        if (!await CurrencyService.IsCurrency(from) || !await CurrencyService.IsCurrency(to))
        {
            await Context.Message.ReplyAsync("One of the currencies provided is invalid.");
            return;
        }

        var rate = await CurrencyService.GetConversion(to, from);
        if (Math.Abs(rate - ConversionError) < 0.01)
        {
            await Context.Message.ReplyAsync("An error occured while converting the currency, the API may be down!");
            return;
        }

        var totalAmount = Math.Round(amount * rate, 2);
        await Context.Message.ReplyAsync($"**{amount} {from.ToUpperInvariant()}** = **{totalAmount} {to.ToUpperInvariant()}**");
    }

    // Ranked below the amount-first overload so an input both can parse (`!curr 100 usd eur`) keeps
    // resolving to it; this overload only wins when it is the sole one that parses.
    [Command("Currency"), HideFromHelp, Priority(28)]
    [Summary("Converts a currency. Syntax : !curr fromCurrency toCurrency amount")]
    [Alias("curr")]
    public async Task ConvertCurrency(string from, string to, double amount)
    {
        await ConvertCurrency(amount, from, to);
    }

    [Command("Currency"), HideFromHelp, Priority(27)]
    [Summary("Converts a currency. Syntax : !curr fromCurrency toCurrency amount")]
    [Alias("curr")]
    public async Task ConvertCurrency(string from, double amount, string to)
    {
        await ConvertCurrency(amount, from, to);
    }
}
