using System.IO;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Services;

public class CurrencyService
{
    private readonly Dictionary<string, Rate> _rates;

    private const string ApiKey = "&apiKey=5d733a03e0274dff3e7f";
    private const string ApiBaseUrl = "https://free.currconv.com/api/v7/";
    private const string ApiCurrencyConvert = "convert?q=USD_{0}&compact=ultra";
    private const string ApiValidCurrency = "currencies?";
    
    // Dictionary of currencies as upper case. EUR, AUD, USD, BTC, etc.
    private readonly Dictionary<string, bool> _validCurrencies = new Dictionary<string, bool>();
    
    public CurrencyService()
    {
        _rates = new Dictionary<string, Rate>();
    }

    public async Task<Rate> GetRate(string currency)
    {
        if (!HasValidRate(currency))
        {
            try
            {
                var rate = await GetNewRate(currency);
                if (rate != null)
                    _rates[currency] = rate;
            }
            catch (Exception e)
            {
                if (_rates.ContainsKey(currency))
                    return _rates[currency];
            }
        }
        
        return _rates.ContainsKey(currency) ? _rates[currency] : null;
    }
    
    #region Public Methods
    
    // Checks if a provided currency is valid, it also checks is we have a list of currencies to check against and rebuilds it if not. (If the API was down when bot started)
    public async Task<bool> IsCurrency(string currency)
    {
        if (_validCurrencies.Count == 0)
            await BuildCurrencyList();
        return _validCurrencies.ContainsKey(currency);
    }

    #endregion // Public Methods

    #region Private Methods

    private async Task BuildCurrencyList()
    {
        var url = ApiBaseUrl + ApiValidCurrency + ApiKey;
        var response = await new HttpClient().GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();
        var currencies = JObject.Parse(json);
        currencies = (JObject)currencies["results"];
        
        foreach (var currency in currencies.Children())
        {
            var currencyName = currency.Path.Split('.')[1];
            _validCurrencies.Add(currencyName.ToUpper(), true);
        }
    }

    private async Task<string> GetResponse(string url)
    {
        string jsonString = string.Empty;

        using var client = new HttpClient();
        
        var response = await client.GetAsync(url);
        if (response.IsSuccessStatusCode)
        {
            jsonString = await response.Content.ReadAsStringAsync();
        }

        return jsonString;
    }

    #endregion // Private Methods

    private async Task<Rate> GetNewRate(string currency)
    {
        var result = await GetResponse(ApiBaseUrl + string.Format(ApiCurrencyConvert, currency) + ApiKey);
        
        if (string.IsNullOrEmpty(result) || result.Equals("{}")) return null;

        var rate = new Rate
        {
            Value = (double)JObject.Parse(result)[$"USD_{currency}"],
            Expires = DateTime.Now.AddHours(6)
        };

        return rate;
    }

    private bool HasValidRate(string currency) => _rates.ContainsKey(currency) && !IsCurrencyExpired(_rates[currency]);
    
    public static bool IsCurrencyExpired(Rate rate) => rate.Expires < DateTime.Today;
    
}

public class Rate
{
    public DateTime Expires { get; set; }
    public double Value { get; set; }

    public Rate()
    {
        Value = -1;
        Expires = DateTime.MinValue;
    }
    
    public bool IsStale() => Expires < DateTime.Today;
}