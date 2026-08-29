namespace DiscordBot.Settings;

public static class BotEnvironmentVariables
{
    public const string Prefix = "UDCBOT_";
    public const string DiscordToken = Prefix + "DiscordConnection__Token";
    public const string DatabaseConnectionString = Prefix + "Database__ConnectionString";
    public const string WeatherApiKey = Prefix + "Weather__ApiKey";
    public const string FlightApiKey = Prefix + "Airport__FlightApiKey";
    public const string FlightApiSecret = Prefix + "Airport__FlightApiSecret";
    public const string AirLabsApiKey = Prefix + "Airport__AirLabsApiKey";
}
