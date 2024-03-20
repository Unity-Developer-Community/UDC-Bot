using Newtonsoft.Json;

namespace DiscordBot.Modules.Weather;

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

    // ReSharper restore InconsistentNaming
#pragma warning restore 0649
    #endregion