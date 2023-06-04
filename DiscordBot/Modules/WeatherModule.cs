using Discord.Commands;
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
        
    #endregion
        
    #region Weather Results

#pragma warning disable 0649
    // ReSharper disable InconsistentNaming
    public class WeatherContainer
    {
        public class Coord
        {
            public double Lon { get; set; }
            public double Lat { get; set; }
        }

        public class Weather
        {
            public int id { get; set; }
            [JsonProperty("main")] public string Name { get; set; }
            public string Description { get; set; }
            public string Icon { get; set; }
        }

        public class Main
        {
            public float Temp { get; set; }
            [JsonProperty("feels_like")] public double Feels { get; set; }
            [JsonProperty("temp_min")] public double Min { get; set; }
            [JsonProperty("temp_max")] public double Max { get; set; }
            public int Pressure { get; set; }
            public int Humidity { get; set; }
        }

        public class Wind
        {
            public double Speed { get; set; }
            public int Deg { get; set; }
        }

        public class Clouds
        {
            public int all { get; set; }
        }

        public class Rain
        {
            [JsonProperty("1h")] public double Rain1h { get; set; }
            [JsonProperty("3h")] public double Rain3h { get; set; }
        }
        
        public class Snow
        {
            [JsonProperty("1h")] public double Snow1h { get; set; }
            [JsonProperty("3h")] public double Snow3h { get; set; }
        }

        public class Sys
        {
            public int type { get; set; }
            public int id { get; set; }
            public double message { get; set; }
            public string country { get; set; }
            public int sunrise { get; set; }
            public int sunset { get; set; }
        }

        public class Result
        {
            public Coord coord { get; set; }
            public List<Weather> weather { get; set; }
            public string @base { get; set; }
            public Main main { get; set; }
            public int visibility { get; set; }
            public Wind wind { get; set; }
            public Clouds clouds { get; set; }
            public Rain rain { get; set; }
            public Snow snow { get; set; }
            public int dt { get; set; }
            public Sys sys { get; set; }
            public int timezone { get; set; }
            public int id { get; set; }
            public string name { get; set; }
            public int cod { get; set; }
        }
    }

    #endregion
    #region Pollution Results

    public class PollutionContainer
    {
        public class Coord
        {
            public double lon { get; set; }
            public double lat { get; set; }
        }
        public class Main
        {
            public int aqi { get; set; }
        }
        public class Components
        {
            [JsonProperty("co")] public double CarbonMonoxide { get; set; }
            [JsonProperty("no")] public double NitrogenMonoxide { get; set; }
            [JsonProperty("no2")] public double NitrogenDioxide { get; set; }
            [JsonProperty("o3")] public double Ozone { get; set; }
            [JsonProperty("so2")] public double SulphurDioxide { get; set; }
            [JsonProperty("pm2_5")] public double FineParticles { get; set; }
            [JsonProperty("pm10")] public double CoarseParticulate { get; set; }
            [JsonProperty("nh3")] public double Ammonia { get; set; }
        }

        public class List
        {
            public Main main { get; set; }
            public Components components { get; set; }
            public int dt { get; set; }
        }
        public class Result
        {
            public Coord coord { get; set; }
            public List<List> list { get; set; }
        }
    }

    private List<string> AQI_Index = new List<string>()
        {"Invalid", "Good", "Fair", "Moderate", "Poor", "Very Poor"};

    // ReSharper restore InconsistentNaming
#pragma warning restore 0649
    #endregion

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

    [Command("Temperature")]
    [Summary("Attempts to provide the temperature of the city provided.")]
    [Alias("temp"), Priority(20)]
    public async Task Temperature(params string[] city)
    {
        WeatherContainer.Result res = await WeatherService.GetWeather(city: string.Join(" ", city));
        if (!await IsResultsValid(res))
            return;

        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle($"{res.name} Temperature ({res.sys.country})")
            .WithDescription(
                $"Currently: **{Math.Round(res.main.Temp, 1)}°C** [Feels like **{Math.Round(res.main.Feels, 1)}°C**]")
            .WithColor(GetColour(res.main.Temp));

        await ReplyAsync(embed: builder.Build());
    }

    [Command("Weather"), Priority(20)]
    [Summary("Attempts to provide the weather of the city provided.")]
    public async Task CurentWeather(params string[] city)
    {
        WeatherContainer.Result res = await WeatherService.GetWeather(city: string.Join(" ", city));
        if (!await IsResultsValid(res))
            return;

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
        // extraInfo += $"Local time: **{DateTime.UtcNow.AddSeconds(res.timezone):hh\\:mmtt}**";
        

        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle($"{res.name} Weather ({res.sys.country}) [{DateTime.UtcNow.AddSeconds(res.timezone):hh\\:mmtt}]")
            .AddField(
                $"Weather: **{Math.Round(res.main.Temp, 1)}°C** [Feels like **{Math.Round(res.main.Feels, 1)}°C**]",
                $"{extraInfo}\n")
            .WithThumbnailUrl($"https://openweathermap.org/img/wn/{res.weather[0].Icon}@2x.png")
            .WithFooter(
                $"{res.clouds.all}% cloud cover with {GetWindDirection((float)res.wind.Deg)} {Math.Round((res.wind.Speed * 60f * 60f) / 1000f, 2)} km/h winds & {res.main.Humidity}% humidity.")
            .WithColor(GetColour(res.main.Temp));

        await ReplyAsync(embed: builder.Build());
    }

    private string GetWindDirection(float windDeg)
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

    [Command("Pollution"), Priority(21)]
    [Summary("Attempts to provide the pollution conditions of the city provided.")]
    public async Task Pollution(params string[] city)
    {
        WeatherContainer.Result res = await WeatherService.GetWeather(city: string.Join(" ", city));
        if (!await IsResultsValid(res))
            return;

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
            .WithTitle($"{res.name} Pollution ({res.sys.country})")
            .AddField($"Air Quality: **{AQI_Index[polResult.list[0].main.aqi]}** [Pollutants {combined:F2}μg/m3]\n", desc);

        await ReplyAsync(embed: builder.Build());
    }
    
    // Time
    [Command("Time"), Alias("timezone"), Priority(22)]
    [Summary("Attempts to provide the time of the city/location provided.")]
    public async Task Time(params string[] city)
    {
        WeatherContainer.Result res = await WeatherService.GetWeather(city: string.Join(" ", city));
        if (!await IsResultsValid(res))
            return;

        var timezone = res.timezone / 3600;
        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle($"{res.name} Time ({res.sys.country})")
            // Timestamp is UTC, so we need to add the timezone offset to get the local time in format "Sunday, June 04, 2023 11:01:09"
            .WithDescription($"{DateTime.UtcNow.AddSeconds(res.timezone):dddd, MMMM dd, yyyy hh:mm:ss}")
            .AddField("Timezone", $"UTC {(timezone > 0 ? "+" : "")}{timezone}:00");

        await ReplyAsync(embed: builder.Build());
    }
    
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
    
    #endregion Utility Methods
}
