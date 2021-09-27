using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using DiscordBot.Extensions;
using DiscordBot.Utils;
using Newtonsoft.Json;

namespace DiscordBot.Modules
{
    // https://openweathermap.org/current#call
    
    // Allows UserModule !help to show commands from this module
    [Group("UserModule"), Alias("")]
    public class WeatherModule : ModuleBase
    {
        #region Weather Results

#pragma warning disable 0649
        // ReSharper disable InconsistentNaming
        public class WeatherCotainer
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
        
        private readonly string _weatherApiKey;

        public WeatherModule(Settings.Deserialized.Settings settings)
        {
            _weatherApiKey = settings.WeatherAPIKey;
        }
        
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
            await ReplyAsync(embed:builder.Build()).DeleteAfterSeconds(seconds: 30);
        }

        [Command("Temperature")]
        [Summary("Attempts to provide the temperature of the city provided.")]
        [Alias("temp"), Priority(20)]
        public async Task Temperature(params string[] city)
        {
            WeatherCotainer.Result res = await GetWeather(city: string.Join(" ", city));
            if (! await IsResultsValid(res))
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
            WeatherCotainer.Result res = await GetWeather(city: string.Join(" ", city));
            if (! await IsResultsValid(res))
                return;
            
            EmbedBuilder builder = new EmbedBuilder()
                .WithTitle($"{res.name} Weather ({res.sys.country})")
                .AddField(
                    $"Weather: **{Math.Round(res.main.Temp, 1)}°C** [Feels like **{Math.Round(res.main.Feels, 1)}°C**]",
                    $"Min: **{Math.Round(res.main.Min, 1)}°C** | Max: **{Math.Round(res.main.Max, 1)}°C**\n")
                .WithThumbnailUrl($"https://openweathermap.org/img/wn/{res.weather[0].Icon}@2x.png")
                .WithFooter(
                    $"{res.clouds.all}% cloud cover with {Math.Round((res.wind.Speed * 60f * 60f) / 1000f, 2)} km/h winds & {res.main.Humidity}% humidity.")
                .WithColor(GetColour(res.main.Temp));

            await ReplyAsync(embed: builder.Build());
        }
        
        [Command("Pollution"), Priority(21)]
        [Summary("Attempts to provide the pollution conditions of the city provided.")]
        public async Task Pollution(params string[] city)
        {
            WeatherCotainer.Result res = await GetWeather(city: string.Join(" ", city));
            if (! await IsResultsValid(res))
                return;

            // We can't really combine the call as having WeatherResults helps with other details
            PollutionContainer.Result polResult =
                await GetPollution(Math.Round(res.coord.Lon, 4), Math.Round(res.coord.Lat, 4));


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

        public async Task<WeatherCotainer.Result> GetWeather(string city, string unit = "metric")
        {
            var query = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_weatherApiKey}&units={unit}";
            return await  GetAndDeserializedObject<WeatherCotainer.Result>(query);
        }
        
        public async Task<PollutionContainer.Result> GetPollution(double lon, double lat)
        {
            var query = $"https://api.openweathermap.org/data/2.5/air_pollution?lat={lat}&lon={lon}&appid={_weatherApiKey}";
            return await GetAndDeserializedObject<PollutionContainer.Result>(query);
        }

        private async Task<T> GetAndDeserializedObject<T>(string query)
        {
            var result = await InternetExtensions.GetHttpContents(query);
            var resultObject = JsonConvert.DeserializeObject<T>(result);
            if (resultObject == null)
            {
                ConsoleLogger.Log($"WeatherModule Failed to Deserialize object", Severity.Error);
            }
            return resultObject;
        }
        
        private async Task<bool> IsResultsValid<T>(T res)
        {
            if (res != null) return true;
            
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
                < 0f => new Color(187, 221, 255),
                < 10f => new Color(187, 255, 255),
                < 20f => new Color(230, 253, 249),
                < 30f => new Color(253, 234, 230),
                < 40f => new Color(255, 102, 102),
                _ => new Color(255, 0, 0)
            };
        }
    }
}