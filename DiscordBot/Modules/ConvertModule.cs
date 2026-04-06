using Discord.Commands;
using DiscordBot.Attributes;
using DiscordBot.Services;
using DiscordBot.Utils;

namespace DiscordBot.Modules;

[Group("UserModule"), Alias("")]
public class ConvertModule : ModuleBase
{
    public CurrencyService CurrencyService { get; set; }

    [Command("FtoC"), Priority(28)]
    [Summary("Converts a temperature in fahrenheit to celsius. Syntax : !ftoc temperature")]
    public async Task FahrenheitToCelsius(float f)
    {
        await ReplyAsync($"{Context.User.Mention} {f}°F is {MathUtility.FahrenheitToCelsius(f)}°C.");
    }

    [Command("CtoF"), Priority(28)]
    [Summary("Converts a temperature in celsius to fahrenheit. Syntax : !ftoc temperature")]
    public async Task CelsiusToFahrenheit(float c)
    {
        await ReplyAsync($"{Context.User.Mention}  {c}°C is {MathUtility.CelsiusToFahrenheit(c)}°F");
    }

    [Command("Translate"), HideFromHelp]
    [Summary("Translate a message. Syntax : !translate messageId language")]
    public async Task Translate(ulong messageId, string language = "en")
    {
        await Translate((await Context.Channel.GetMessageAsync(messageId)).Content, language);
    }

    [Command("Translate"), HideFromHelp]
    [Summary("Translate a message. Syntax : !translate text language")]
    public async Task Translate(string text, string language = "en")
    {
        var msg = await ReplyAsync($"Here: <https://translate.google.com/#auto/{language}/{text.Replace(" ", "%20")}>");
        await Context.Message.DeleteAfterSeconds(seconds: 1);
        await msg.DeleteAfterSeconds(seconds: 20);
    }

    [Command("CurrencyName"), Priority(29)]
    [Summary("Get the name of a currency. Syntax : !currname USD")]
    [Alias("currname")]
    public async Task CurrencyName(string currency)
    {
        if (Context.HasAnyPingableMention())
            return;
        var name = await CurrencyService.GetCurrencyName(currency);
        if (name == string.Empty)
        {
            await Context.Message.ReplyAsync($"Sorry, I couldn't find the name of the currency **{currency}**.");
            return;
        }
        await Context.Message.ReplyAsync($"The name of the currency **{currency.ToUpper()}** is **{name}**.");
    }

    [Command("Currency"), HideFromHelp]
    [Summary("Converts a currency. Syntax : !currency fromCurrency toCurrency")]
    [Alias("curr")]
    public async Task ConvertCurrency(string from, string to = "usd")
    {
        await ConvertCurrency(1, from, to);
    }

    [Command("Currency"), Priority(29)]
    [Summary("Converts a currency. Syntax : !currency amount fromCurrency toCurrency")]
    [Alias("curr")]
    public async Task ConvertCurrency(double amount, string from, string to = "usd")
    {
        if (Context.HasAnyPingableMention())
        {
            if (!Context.IsReply())
                return;
            if (!Context.IsOnlyReplyingToAuthor())
                return;
        }

        from = from.ToLower();
        to = to.ToLower();

        bool fromValid = await CurrencyService.IsCurrency(from.ToLower());
        bool toValid = await CurrencyService.IsCurrency(to.ToLower());

        if (!fromValid || !toValid)
        {
            await Context.Message.ReplyAsync("One of the currencies provided is invalid.");
            return;
        }

        var response = await CurrencyService.GetConversion(to, from);
        if (Math.Abs(response - (-1)) < 0.01)
        {
            await Context.Message.ReplyAsync("An error occured while converting the currency, the API may be down!");
            return;
        }

        var totalAmount = Math.Round(amount * response, 2);
        await Context.Message.ReplyAsync($"**{amount} {from.ToUpper()}** = **{totalAmount} {to.ToUpper()}**");
    }
}
