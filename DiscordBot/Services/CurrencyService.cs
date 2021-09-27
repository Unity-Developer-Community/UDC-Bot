using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Services
{
    public class CurrencyService
    {
        private readonly Dictionary<string, Rate> _rates;

        public CurrencyService()
        {
            _rates = new Dictionary<string, Rate>();
        }

        public async Task<double> GetRate(string currency)
        {
            if (!HasValidRate(currency))
            {
                var rate = await GetNewRate(currency);
                if (rate == null) return -1;
                _rates[currency] = rate;
            }

            return _rates[currency].Value;
        }

        private static async Task<Rate> GetNewRate(string currency)
        {
            // Url to exchange rate from USD
            var apiKey = "5d733a03e0274dff3e7f";
            var rateUrl = $"https://free.currencyconverterapi.com/api/v6/convert?q=USD_{currency}&compact=ultra&apiKey={apiKey}";

            string rateJson;
            using (var wc = new WebClient())
            {
                // Download json string from url
                rateJson = await wc.DownloadStringTaskAsync(rateUrl);
            }

            if (string.IsNullOrEmpty(rateJson) || rateJson.Equals("{}")) return null;

            var rate = new Rate
            {
                Value = (double)JObject.Parse(rateJson)[$"USD_{currency}"],
                LastUpdated = DateTime.Today
            };

            return rate;
        }

        private bool HasValidRate(string currency) => _rates.ContainsKey(currency) && !IsExpired(_rates[currency]);

        private bool IsExpired(Rate rate) => rate.LastUpdated.Date != DateTime.Today;
    }

    public class Rate
    {
        public DateTime LastUpdated { get; set; }
        public double Value { get; set; }

        public Rate()
        {
            Value = -1;
            LastUpdated = DateTime.MinValue;
        }
    }
}