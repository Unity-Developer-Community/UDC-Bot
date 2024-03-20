using DiscordBot.Utils;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Services;

public class CurrencyService
{
    private const string ServiceName = "CurrencyService";
    
    #region Configuration

    private const int ApiVersion = 1;
    private const string TargetDate = "latest";
    private const string ValidCurrenciesEndpoint = "currencies.min.json";
    private const string ExchangeRatesEndpoint = "currencies";
    
    private class Currency
    {
        public string Name { get; set; }
        public string Short { get; set; }
    }

    #endregion // Configuration
    
    private readonly Dictionary<string, Currency> _currencies = new();

    private static readonly string ApiUrl = $"https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@{TargetDate}/v{ApiVersion}/";

    public async Task<float> GetConversion(string toCurrency, string fromCurrency = "usd")
    {
        toCurrency = toCurrency.ToLower();
        fromCurrency = fromCurrency.ToLower();
        
        var url = $"{ApiUrl}{ExchangeRatesEndpoint}/{fromCurrency.ToLower()}.min.json";
        
        // Check if success
        var (success, response) = await WebUtil.TryGetObjectFromJson<JObject>(url);
        if (!success)
            return -1;
        
        // json[fromCurrency][toCurrency]
        var value = response.SelectToken($"{fromCurrency}.{toCurrency}");
        if (value == null)
            return -1;
        
        return value.Value<float>();
    }
    
    #region Public Methods

    public async Task<string> GetCurrencyName(string currency)
    {
        currency = currency.ToLower();
        if (!await IsCurrency(currency))
            return string.Empty;
        return _currencies[currency].Name;
    }

    // Checks if a provided currency is valid, it also checks is we have a list of currencies to check against and rebuilds it if not. (If the API was down when bot started)
    public async Task<bool> IsCurrency(string currency)
    {
        if (_currencies.Count  <= 1)
            await BuildCurrencyList();
        return _currencies.ContainsKey(currency);
    }

    #endregion // Public Methods

    #region Private Methods

    private async Task BuildCurrencyList()
    {
        var url = ApiUrl + ValidCurrenciesEndpoint;
        var currencies = await WebUtil.GetObjectFromJson<Dictionary<string, string>>(url);
        
        // Json is weird format of `Code: Name` each in dependant ie; {"1inch":"1inch Network","aave":"Aave"}
        foreach (var currency in currencies)
        {
            _currencies.Add(currency.Key, new Currency
            {
                Name = currency.Value!.ToString(),
                Short = currency.Key
            });
        }
        
        LoggingService.LogToConsole($"[{ServiceName}] Built currency list with {_currencies.Count} currencies.", ExtendedLogSeverity.Positive);
    }

    #endregion // Private Methods

}