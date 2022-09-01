using System.Net;
using Discord.Commands;
using DiscordBot.Settings;
using Newtonsoft.Json;

// ReSharper disable all UnusedMember.Local
namespace DiscordBot.Modules;

public class TimeModule : ModuleBase
{
    #region Dependency Injection
        
    public BotSettings Settings { get; set; }
        
    #endregion

#pragma warning disable 0649
    private class Response
    {
        public class Geo
        {
            public string country;
            public string state;
            public string city;
            public long latitute;
            public long longitude;
        }
        public Geo geo;
        public string timezone;
        public decimal timezone_offset;
        public string date;
        public string date_time;
        public string date_time_txt;
        public string date_time_wti;
        public string date_time_ymd;
        public string date_time_unix;
        public string time_24;
        public string time_12;
        public uint week;
        public uint month;
        public uint year;
        public string year_abbr;
        public bool is_dst;
        public int dst_savings;
    }
#pragma warning restore 0649

    [Command("time"), Alias("timezone"), Summary("Find the locale time of a location.")]
    public async Task Time(params string[] location)
    {
        try
        {
            using (var wc = new WebClient())
            {
                var res = await wc.DownloadStringTaskAsync($"https://api.ipgeolocation.io/timezone?apiKey={Settings.IPGeolocationAPIKey}&location={string.Join(" ", location)}");
                var response = JsonConvert.DeserializeObject<Response>(res);

                var embedBuilder = new EmbedBuilder();

                List<string> geo = new List<string>();
                if (!String.IsNullOrEmpty(response.geo.city)) geo.Add(response.geo.city);
                if (!String.IsNullOrEmpty(response.geo.state)) geo.Add(response.geo.state);
                if (!String.IsNullOrEmpty(response.geo.country)) geo.Add(response.geo.country);
                embedBuilder.WithTitle($"Time in {String.Join(", ", geo)}");

                embedBuilder.WithDescription($"{response.date_time_txt}");
                embedBuilder.AddField("Timezone", $"{response.timezone} ({response.timezone_offset:+#.#;-#.#;+0})", true);

                var dst = $"{(response.is_dst ? "Yes" : "No")}";
                if (response.is_dst) dst += $" ({response.dst_savings:+#.#;-#.#;+0})";
                embedBuilder.AddField("DST", dst, true);

                var embed = embedBuilder.Build();
                await ReplyAsync(embed: embed);
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            await ReplyAsync("Provided location was not found or the API is down.");
        }
    }
}