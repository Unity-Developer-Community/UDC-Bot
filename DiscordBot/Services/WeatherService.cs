using Discord.WebSocket;
using DiscordBot.Modules;
using DiscordBot.Settings;
using DiscordBot.Utils;

namespace DiscordBot.Services;

public class WeatherService
{
    
    private readonly DiscordSocketClient _client;
    private readonly ILoggingService _loggingService;
    private readonly string _weatherApiKey;

    public WeatherService(DiscordSocketClient client, ILoggingService loggingService, BotSettings settings)
    {
        _client = client;
        _loggingService = loggingService;
        _weatherApiKey = settings.WeatherAPIKey;
    }
    
    
    public async Task<WeatherModule.WeatherContainer.Result> GetWeather(string city, string unit = "metric")
    {
        var query = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_weatherApiKey}&units={unit}";
        return await SerializeUtil.LoadUrlDeserializeResult<WeatherModule.WeatherContainer.Result>(query);
    }

    public async Task<WeatherModule.PollutionContainer.Result> GetPollution(double lon, double lat)
    {
        var query = $"https://api.openweathermap.org/data/2.5/air_pollution?lat={lat}&lon={lon}&appid={_weatherApiKey}";
        return await SerializeUtil.LoadUrlDeserializeResult<WeatherModule.PollutionContainer.Result>(query);
    }

}