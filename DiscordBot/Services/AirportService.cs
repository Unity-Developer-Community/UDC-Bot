using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Discord.WebSocket;
using DiscordBot.Settings;
using DiscordBot.Utils;
using Newtonsoft.Json;

namespace DiscordBot.Services;

public class AirportService
{
    private readonly DiscordSocketClient _client;
    private readonly ILoggingService _loggingService;
    
    #region Amadeus

    private string _flightApiKey;
    private string _flightSecret;
    DateTime _amadeusTokenExpiration = DateTime.Now;
    string _amadeusToken = string.Empty;
    
    private string _baseRoute = "https://test.api.amadeus.com/v2/";
    private string _findCheapestRoute = "shopping/flight-offers";
    private string _cheapestRouteParam =
        "?originLocationCode={0}&destinationLocationCode={1}&departureDate={2}&adults=1&nonStop=false&max=5&currencyCode=USD";

    #region Return Results
    
    public class AmadeusRoot
    {
        public List<FlightInfo> data { get; set; }
    }
    
    public class FlightInfo
    {
        public string type { get; set; }
        public string id { get; set; }
        public string source { get; set; }
        public bool instantTicketingRequired { get; set; }
        public bool nonHomogeneous { get; set; }
        public bool oneWay { get; set; }
        public string lastTicketingDate { get; set; }
        public int numberOfBookableSeats { get; set; }
        public List<Itinerary> itineraries { get; set; }
        public Price price { get; set; }
        public PricingOptions pricingOptions { get; set; }
        public List<string> validatingAirlineCodes { get; set; }
        // public List<TravelerPricing> travelerPricings { get; set; }
    }
    
    public class PricingOptions
    {
        public List<string> fareType { get; set; }
        public bool includedCheckedBagsOnly { get; set; }
    }
    
    public class Price
    {
        public string currency { get; set; }
        public string total { get; set; }
        public string @base { get; set; } 
        public List<Fee> fees { get; set; }
        public string grandTotal { get; set; }

        public double GrandTotalNumber()
        {
            return double.TryParse(grandTotal, out double result) ? result : double.MinValue;
        }
    }
    
    public class Fee
    {
        public string amount { get; set; }
        public string type { get; set; }
    }
    
    public class Itinerary
    {
        public string duration { get; set; }
        public List<Segment> segments { get; set; }
    }
    
    public class Segment
    {
        public FlightDetails departure { get; set; }
        public FlightDetails arrival { get; set; }
        public string carrierCode { get; set; }
        public string number { get; set; }
        // public Aircraft aircraft { get; set; }
        // public Operating operating { get; set; }
        public string duration { get; set; }
        public string id { get; set; }
        public int numberOfStops { get; set; }
        public bool blacklistedInEU { get; set; }
    }
    
    public class FlightDetails
    {
        public string iataCode { get; set; }
        public DateTime at { get; set; }
    }
    
    public class AmadeusAuthRoot
    {
        public string type { get; set; }
        public string username { get; set; }
        public string application_name { get; set; }
        public string client_id { get; set; }
        public string token_type { get; set; }
        public string access_token { get; set; }
        public int expires_in { get; set; }
        public string state { get; set; }
        public string scope { get; set; }
    }

    #endregion // Return Results
    
    #endregion // Amadeus

    #region AirLabs
    
    private string _airLabsNearbyCityRoute =
        "https://airlabs.co/api/v9/nearby?lat={0}&lng={1}&distance=100";
    private string _airLabsAPIInclude = "&api_key={0}";
    private string _airLabsAPIRequiredFields = "&_fields=iata_code";
    
    #region Return Results
    
    public class AirLabsAirport
    {
        public string icao_code { get; set; }
        public string country_code { get; set; }
        public string iata_code { get; set; }
        public double lng { get; set; }
        public string city { get; set; }
        public string timezone { get; set; }
        public string name { get; set; }
        public string city_code { get; set; }
        public string slug { get; set; }
        public double lat { get; set; }
        public int popularity { get; set; }
        public double distance { get; set; }
    }

    public class AirLabsCity
    {
        public string country_code { get; set; }
        public double lng { get; set; }
        public string timezone { get; set; }
        public string name { get; set; }
        public string city_code { get; set; }
        public string slug { get; set; }
        public double lat { get; set; }
        public int popularity { get; set; }
        public double distance { get; set; }
    }

    public class AirLabsRoot
    {
        public List<AirLabsAirport> airports { get; set; }
        public List<AirLabsCity> cities { get; set; }
    }

    public class AirLabsSuperRoot
    {
        public AirLabsRoot response { get; set; }
    }

    #endregion // Return Results
    
    #endregion // AirLabs

    public AirportService(DiscordSocketClient client, ILoggingService loggingService, BotSettings botSettings)
    {
        _client = client;
        _loggingService = loggingService;
        _flightApiKey = botSettings.FlightAPIKey;
        _flightSecret = botSettings.FlightAPISecret;
        
        _airLabsAPIInclude = string.Format(_airLabsAPIInclude, botSettings.AirLabAPIKey);
        _airLabsNearbyCityRoute += _airLabsAPIInclude + _airLabsAPIRequiredFields;
    }

    public async Task<AirLabsAirport> GetClosestAirport(double lat, double lng)
    {
        var url = string.Format(_airLabsNearbyCityRoute, lat, lng);

        var result = await SerializeUtil.LoadUrlDeserializeResult<AirLabsSuperRoot>(url);
        
        // Sort by popularity
        result.response.airports.Sort((a, b) => b.popularity.CompareTo(a.popularity));
        // Return first Airport that has a IATA code
        return result.response.airports.FirstOrDefault(a => !string.IsNullOrEmpty(a.iata_code));
    }
    
    public async Task<string> GetFlightTickets(string from, string to)
    {
        
        return null;
    }

    #region Utility Methods

    public async Task<bool> GetValidationToken()
    {
        if (_amadeusTokenExpiration > DateTime.Now)
            return true;
        
        var url = "https://test.api.amadeus.com/v1/security/oauth2/token";
        var data = "grant_type=client_credentials&client_id=" + _flightApiKey + "&client_secret=" + _flightSecret;
        
        HttpClient client = new HttpClient();
        var response = await client.PostAsync(url, new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"));

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadAsStringAsync();
            var authRoot = JsonConvert.DeserializeObject<AmadeusAuthRoot>(result);
            if (authRoot != null)
            {
                _amadeusToken = authRoot.access_token;
                _amadeusTokenExpiration = DateTime.Now.AddSeconds(authRoot.expires_in - 1);
                return true;
            }
        }
        return false;
    }
    
    public async Task<List<FlightInfo>> GetFlightInfo(string from, string to, int daysFromNow = 2)
    {
        if (!await GetValidationToken())
            return null;
        
        var url = _baseRoute + _findCheapestRoute + string.Format(_cheapestRouteParam, from, to, DateTime.Now.AddDays(daysFromNow).ToString("yyyy-MM-dd"));
        
        HttpClient client = new HttpClient();
        HttpRequestHeaders headers = client.DefaultRequestHeaders;
        headers.Add("Authorization", "Bearer " + _amadeusToken);
        
        var response = await client.GetAsync(url);
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadAsStringAsync();
            var root = JsonConvert.DeserializeObject<AmadeusRoot>(result);
            if (root != null)
            {
                root.data.Sort((a, b) => b.price.GrandTotalNumber().CompareTo(a.price.GrandTotalNumber()));
                return root.data;
            }
        }
        return null;
    }

    #endregion // Utility Methods
}