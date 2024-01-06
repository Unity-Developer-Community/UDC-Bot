using Discord.Commands;
using DiscordBot.Attributes;
using DiscordBot.Modules.Weather;
using DiscordBot.Services;
using Newtonsoft.Json;

namespace DiscordBot.Modules;
// https://openweathermap.org/current#call

// Allows UserModule !help to show commands from this module
[Group("UserModule"), Alias("")]
public class WeatherModule : ModuleBase
{
    #region Dependency Injection
    
    public WeatherService WeatherService { get; set; }
    public UserExtendedService UserExtendedService { get; set; }
        
    #endregion
    
    private List<string> AQI_Index = new List<string>()
        {"Invalid", "Good", "Fair", "Moderate", "Poor", "Very Poor"};

    [Command("WeatherHelp")]
    [Summary("How to use the weather module.")]
    [Priority(100)]
    public async Task WeatherHelp()
    {
        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle("Weather Module Help")
            .WithDescription(
                "If the city isn't correct you will need to include the correct [city codes](https://www.iso.org/obp/ui/#search).\n**Example Usage**: *!Weather Wellington, UK*");
        await Context.Message.DeleteAsync();
        await ReplyAsync(embed: builder.Build()).DeleteAfterSeconds(seconds: 30);
    }

    #region Temperature
    
    private async Task<EmbedBuilder> TemperatureEmbed(string city, string replaceCityWith = "")
    {
        WeatherContainer.Result res = await WeatherService.GetWeather(city: city);
        if (!await IsResultsValid(res))
            return null;

        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle($"{(replaceCityWith.Length == 0 ? res.name : replaceCityWith)} Temperature ({res.sys.country})")
            .WithDescription(
                $"Currently: **{Math.Round(res.main.Temp, 1)}°C** [Feels like **{Math.Round(res.main.Feels, 1)}°C**]")
            .WithColor(GetColour(res.main.Temp));

        return builder;
    }
    
    [Command("Temperature"), HideFromHelp]
    [Summary("Attempts to provide the temperature of the user provided.")]
    [Alias("temp"), Priority(20)]
    public async Task Temperature(IUser user)
    {
        if (!await DoesUserHaveDefaultCity(user))
            return;
        
        var city = await UserExtendedService.GetUserDefaultCity(user);
        var builder = await TemperatureEmbed(city, user.GetUserPreferredName());
        if (builder == null)
            return;
        builder.FooterRequestedBy(Context.User);

        await ReplyAsync(embed: builder.Build());
    }
    
    [Command("Temperature")]
    [Summary("Attempts to provide the temperature of the city provided.")]
    [Alias("temp"), Priority(20)]
    public async Task Temperature(params string[] city)
    {
        var builder = await TemperatureEmbed(string.Join(" ", city));
        if (builder == null)
            return;

        await ReplyAsync(embed: builder.Build());
    }
    
    #endregion // Temperature

    #region Weather
    
    private async Task<EmbedBuilder> WeatherEmbed(string city, string replaceCityWith = "")
    {
        WeatherContainer.Result res = await WeatherService.GetWeather(city: city);
        if (!await IsResultsValid(res))
            return null;

        string extraInfo = string.Empty;
        
        DateTime sunrise = DateTime.UnixEpoch.AddSeconds(res.sys.sunrise)
            .AddSeconds(res.timezone);
        DateTime sunset = DateTime.UnixEpoch.AddSeconds(res.sys.sunset)
            .AddSeconds(res.timezone);
        
        // Sun rise/set
        if (res.sys.sunrise > 0)
            extraInfo += $"Sunrise **{sunrise:hh\\:mmtt}**, ";
        if (res.sys.sunrise > 0)
            extraInfo += $"Sunset **{sunset:hh\\:mmtt}**\n";
        
        if (res.main.Temp > 0 && res.rain != null)
        {
            if (res.rain.Rain3h > 0)
                extraInfo += $"**{Math.Round(res.rain.Rain3h, 1)}mm** *of rain in the last 3 hours*\n";
            else if (res.rain.Rain1h > 0)
                extraInfo += $"**{Math.Round(res.rain.Rain1h, 1)}mm** *of rain in the last hour*\n";
        }
        else if (res.main.Temp <= 0 && res.snow != null)
        {
            if (res.snow.Snow3h > 0)
                extraInfo += $"**{Math.Round(res.snow.Snow3h, 1)}mm** *of snow in the last 3 hours*\n";
            else if (res.snow.Snow1h > 0)
                extraInfo += $"**{Math.Round(res.snow.Snow1h, 1)}mm** *of snow in the last hour*\n";
        }

        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle($"{(replaceCityWith.Length == 0 ? res.name : replaceCityWith)} Weather ({res.sys.country}) [{DateTime.UtcNow.AddSeconds(res.timezone):hh\\:mmtt}]")
            .AddField(
                $"Weather: **{Math.Round(res.main.Temp, 1)}°C** [Feels like **{Math.Round(res.main.Feels, 1)}°C**]",
                $"{extraInfo}\n")
            .WithThumbnailUrl($"https://openweathermap.org/img/wn/{res.weather[0].Icon}@2x.png")
            .WithFooter(
                $"{res.clouds.all}% cloud cover with {GetWindDirection((float)res.wind.Deg)} {Math.Round((res.wind.Speed * 60f * 60f) / 1000f, 2)} km/h winds & {res.main.Humidity}% humidity.")
            .WithColor(GetColour(res.main.Temp));
        
        return builder;
    }
        
    [Command("Weather"), HideFromHelp, Priority(20)]
    [Summary("Attempts to provide the weather of the user provided.")]
    public async Task CurentWeather(IUser user)
    {
        if (!await DoesUserHaveDefaultCity(user))
            return;
        
        var city = await UserExtendedService.GetUserDefaultCity(user);
        var builder = await WeatherEmbed(city, user.GetUserPreferredName());
        if (builder == null)
            return;
        builder.FooterRequestedBy(Context.User);

        await ReplyAsync(embed: builder.Build());
    }

    [Command("Weather"), Priority(20)]
    [Summary("Attempts to provide the weather of the city provided.")]
    public async Task CurentWeather(params string[] city)
    {
        var builder = await WeatherEmbed(string.Join(" ", city));
        if (builder == null)
            return;

        await ReplyAsync(embed: builder.Build());
    }
    
    #endregion // Weather

    #region Pollution

    private async Task<EmbedBuilder> PollutionEmbed(string city, string replaceCityWith = "")
    {
        WeatherContainer.Result res = await WeatherService.GetWeather(city: city);
        if (!await IsResultsValid(res))
            return null;

        // We can't really combine the call as having WeatherResults helps with other details
        PollutionContainer.Result polResult =
            await WeatherService.GetPollution(Math.Round(res.coord.Lon, 4), Math.Round(res.coord.Lat, 4));


        var comp = polResult.list[0].components;
        double combined = comp.CarbonMonoxide + comp.NitrogenMonoxide + comp.NitrogenDioxide + comp.Ozone +
                          comp.SulphurDioxide + comp.FineParticles + comp.CoarseParticulate + comp.Ammonia;

        List<(string, string)> visibleData = new List<(string, string)>()
        {
            ("CO", $"{((comp.CarbonMonoxide / combined) * 100f):F2}%"),
            ("NO", $"{((comp.NitrogenMonoxide / combined) * 100f):F2}%"),
            ("NO2", $"{((comp.NitrogenDioxide / combined) * 100f):F2}%"),
            ("O3", $"{((comp.Ozone / combined) * 100f):F2}%"),
            ("SO2", $"{((comp.SulphurDioxide / combined) * 100f):F2}%"),
            ("PM25", $"{((comp.FineParticles / combined) * 100f):F2}%"),
            ("PM10", $"{((comp.CoarseParticulate / combined) * 100f):F2}%"),
            ("NH3", $"{((comp.Ammonia / combined) * 100f):F2}%"),
        };

        var maxPercentLength = visibleData.Max(x => x.Item2.Length);
        var maxNameLength = visibleData.Max(x => x.Item1.Length);

        var desc = string.Empty;
        for (var i = 0; i < visibleData.Count; i++)
        {
            desc += $"`{visibleData[i].Item1.PadLeft(maxNameLength)} {visibleData[i].Item2.PadLeft(maxPercentLength, '\u2000')}`|";
            if (i == 3)
                desc += "\n";
        }

        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle($"{(replaceCityWith.Length == 0 ? res.name : replaceCityWith)} Pollution ({res.sys.country})")
            .AddField($"Air Quality: **{AQI_Index[polResult.list[0].main.aqi]}** [Pollutants {combined:F2}μg/m3]\n", desc);

        return builder;
    }

    [Command("Pollution"), HideFromHelp, Priority(21)]
    [Summary("Attempts to provide the pollution conditions of the user provided.")]
    public async Task Pollution(IUser user)
    {
        if (!await DoesUserHaveDefaultCity(user))
            return;
        
        var city = await UserExtendedService.GetUserDefaultCity(user);
        var builder = await PollutionEmbed(city, user.GetUserPreferredName());
        if (builder == null)
            return;
        builder.FooterRequestedBy(Context.User);

        await ReplyAsync(embed: builder.Build());
    }
    
    [Command("Pollution"), Priority(21)]
    [Summary("Attempts to provide the pollution conditions of the city provided.")]
    public async Task Pollution(params string[] city)
    {
        var builder = await PollutionEmbed(string.Join(" ", city));
        if (builder == null)
            return;

        await ReplyAsync(embed: builder.Build());
    }
    
    #endregion // Pollution

    #region Time
    
    private async Task<EmbedBuilder> TimeEmbed(string city, string replaceCityWith = "")
    {
        WeatherContainer.Result res = await WeatherService.GetWeather(city: city);
        if (!await IsResultsValid(res))
            return null;

        var timezone = res.timezone / 3600;
        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle($"{(replaceCityWith.Length == 0 ? res.name : replaceCityWith)} Time ({res.sys.country})")
            // Timestamp is UTC, so we need to add the timezone offset to get the local time in format "Sunday, June 04, 2023 11:01:09"
            .WithDescription($"{DateTime.UtcNow.AddSeconds(res.timezone):dddd, MMMM dd, yyyy hh:mm:ss}")
            .AddField("Timezone", $"UTC {(timezone > 0 ? "+" : "")}{timezone}:00");

        return builder;
    }
    
    [Command("Time"), HideFromHelp, Priority(22)]
    [Summary("Attempts to provide the time of the user provided.")]
    public async Task Time(IUser user)
    {
        if (!await DoesUserHaveDefaultCity(user))
            return;
        
        var city = await UserExtendedService.GetUserDefaultCity(user);
        var builder = await TimeEmbed(city, user.GetUserPreferredName());
        if (builder == null)
            return;
        builder.FooterRequestedBy(Context.User);

        await ReplyAsync(embed: builder.Build());
    }
    
    [Command("Time"), Priority(22)]
    [Summary("Attempts to provide the time of the city/location provided.")]
    public async Task Time(params string[] city)
    {
        var builder = await TimeEmbed(string.Join(" ", city));
        if (builder == null)
            return;

        await ReplyAsync(embed: builder.Build());
    }
    
    #endregion // Time
    
    #region Utility Methods

    private async Task<bool> IsResultsValid<T>(T res)
    {
        if (!object.Equals(res, default(T))) return true;

        await ReplyAsync("API Returned no results.");
        return false;
    }
    /// <summary>
    /// Crude fixed colour to temp range.
    /// </summary>
    private Color GetColour(float temp)
    {
        // We could lerp between values, but colour lerping is weird
        return temp switch
        {
            < -10f => new Color(161, 191, 255),
            < 0f => new Color(223, 231, 255),
            < 10f => new Color(243, 246, 255),
            < 20f => new Color(255, 245, 246),
            < 30f => new Color(255, 227, 212),
            < 40f => new Color(255, 186, 117),
            _ => new Color(255, 0, 0)
        };
    }
    
    private async Task<bool> DoesUserHaveDefaultCity(IUser user)
    {
        // If they do, return true
        if (await UserExtendedService.DoesUserHaveDefaultCity(user)) return true;
        
        // Otherwise respond and return false
        await ReplyAsync($"User {user.Username} does not have a default city set.");
        return false;
    }
    
    private static string GetWindDirection(float windDeg)
    {
        if (windDeg < 22.5)
            return "N";
        if (windDeg < 67.5)
            return "NE";
        if (windDeg < 112.5)
            return "E";
        if (windDeg < 157.5)
            return "SE";
        if (windDeg < 202.5)
            return "S";
        if (windDeg < 247.5)
            return "SW";
        if (windDeg < 292.5)
            return "W";
        if (windDeg < 337.5)
            return "NW";
        return "N";
    }
    
    #endregion Utility Methods
}
