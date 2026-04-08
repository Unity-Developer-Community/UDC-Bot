using Discord.WebSocket;
using DiscordBot.Settings;
using DiscordBot.Utils;

namespace DiscordBot.Services.Utils.Weather;

public class WeatherService
{
    private const string ServiceName = "FeedService";

    private readonly ILoggingService _loggingService;
    private readonly string _weatherApiKey;

    public WeatherService(ILoggingService loggingService, BotSettings settings)
    {
        _loggingService = loggingService;
        _weatherApiKey = settings.ApiKeys.Weather;

        if (string.IsNullOrWhiteSpace(_weatherApiKey))
        {
            _loggingService.LogAction($"[{ServiceName}] Error: Weather API Key is not set.", ExtendedLogSeverity.Warning);
        }
    }


    public async Task<WeatherContainer.Result?> GetWeather(string city, string unit = "metric")
    {
        var query = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_weatherApiKey}&units={unit}";
        return await SerializeUtil.LoadUrlDeserializeResult<WeatherContainer.Result>(query);
    }

    public async Task<PollutionContainer.Result?> GetPollution(double lon, double lat)
    {
        var query = $"https://api.openweathermap.org/data/2.5/air_pollution?lat={lat}&lon={lon}&appid={_weatherApiKey}";
        return await SerializeUtil.LoadUrlDeserializeResult<PollutionContainer.Result>(query);
    }

    public async Task<(bool exists, WeatherContainer.Result? result)> CityExists(string city)
    {
        var res = await GetWeather(city: city);
        var exists = !object.Equals(res, default(WeatherContainer.Result));
        return (exists, res);
    }

}