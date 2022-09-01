using Discord.Commands;
using DiscordBot.Services;
using DiscordBot.Settings;

namespace DiscordBot.Modules;

// Allows UserModule !help to show commands from this module
[Group("UserModule"), Alias("")]
public class AirportModule : ModuleBase
{
    #region Dependency Injection

    public AirportService AirportService { get; set; }
    public BotSettings Settings { get; set; }
    // Needed to locate cities lon/lat easier
    public WeatherService WeatherService { get; set; }

    #endregion // Dependency Injection

    #region API Results
    
    public class FlightResults
    {
        public string iata { get; set; }
        public string fs { get; set; }
        public string name { get; set; }
    }

    public class FlightRoot
    {
        public List<FlightResults> data { get; set; }
    }

    #endregion // API Results

    #region Commands

    [Command("Fly")]
    [Summary("Fly to a city")]
    public async Task FlyTo(string from, string to)
    {
        // Make sure command is in Bot-Commands or OffTopic
        if (Context.Channel.Id != Settings.BotCommandsChannel.Id && Context.Channel.Id != Settings.GeneralChannel.Id)
        {
            await ReplyAsync($"Command can only be used in <#{Settings.BotCommandsChannel.Id}> or <#{Settings.GeneralChannel.Id}>.").DeleteAfterSeconds(5f);
            await Context.Message.DeleteAfterSeconds(2f);
            return;
        }
        
        EmbedBuilder embed = new EmbedBuilder();
        embed.Title = "Flight Finder";

        embed.Description = "Finding cities";
        var msg = await ReplyAsync(string.Empty, false, embed.Build());
        
        // Use Weather API to get lon/lat of cities
        var fromCity = await GetCity(from, embed, msg);
        if (fromCity == null)
            return;
        var toCity = await GetCity(to, embed, msg);
        if (toCity == null)
            return;
        
        // Find closest Airport using AirLabs API
        embed.Description = "Finding airports";
        await msg.ModifyAsync(x => x.Embed = embed.Build());

        var fromAirport = await GetAirport(fromCity, embed, msg);
        if (fromAirport == null)
            return;
        var toAirport = await GetAirport(toCity, embed, msg);
        if (toAirport == null)
            return;
        
        // Find cheapest flight using GetFlightInfo
        embed.Description = $"Searching {fromAirport.name} to {toAirport.name}";
        await msg.ModifyAsync(x => x.Embed = embed.Build());
        
        var daysUntilTuesday = (int)DateTime.Now.DayOfWeek - 2;
        if (daysUntilTuesday < 0)
            daysUntilTuesday += 7;

        var flights = await AirportService.GetFlightInfo(fromAirport.iata_code, toAirport.iata_code, daysUntilTuesday);
        if (flights == null)
        {
            embed.Description += $"\nNo flights found, sorry.";
            await msg.ModifyAsync(x => x.Embed = embed.Build());
            await msg.DeleteAfterSeconds(30f);
            return;
        }

        var flight = flights[0];
        
        var itinerary = flight.itineraries.First();
        var numberOfStops = itinerary.segments.Count - 1;
        var departTime = itinerary.segments.First().departure;
        var arriveTime = itinerary.segments.Last().arrival;

        var totalDuration = itinerary.duration.Replace("PT", string.Empty);

        embed.Title += $" - {fromAirport.iata_code} to {toAirport.iata_code} | {flight.price.total} {flight.price.currency}";
        embed.ThumbnailUrl = $"https://countryflagsapi.com/png/{toAirport.country_code}";
        embed.Description = $"{fromAirport.name} to {toAirport.name}";
        embed.Description += $"\nDuration: {totalDuration}, with {(numberOfStops > 1 ? "at least" : string.Empty)} {numberOfStops} stop{(numberOfStops != 1 ? "s" : string.Empty)}.";
        // embed.Description +=
        //     $"\nSeats remaining: {flight.numberOfBookableSeats}, Bags: {(flight.pricingOptions.includedCheckedBagsOnly ? "Y" : "N")}, OneWay: {(flight.oneWay ? "Y" : "N")}";
        embed.Description += $"\nDepart: {departTime.at:dd/MM/yy HH:MM}, Arrive: {arriveTime.at:dd/MM/yy HH:MM}";
        
        // string price = $"Base: {flight.price.@base}";
        // foreach (var fee in flight.price.fees)
        // {
        //     if (float.Parse(fee.amount) > 0)
        //     {
        //         price += $"\n{fee.type}: {fee.amount}";
        //     }
        // }
        // embed.AddField($"Total Price ({flight.price.grandTotal} {flight.price.currency})", price);

        await msg.ModifyAsync(x => x.Embed = embed.Build());
    }

    #endregion // Commands
    
    #region Utility Methods
    
    private async Task<WeatherModule.WeatherContainer.Result> GetCity(string city, EmbedBuilder embed, IUserMessage msg)
    {
        var cityResult = await WeatherService.GetWeather(city);
        if (cityResult == null)
        {
            embed.Description += $"\n{city} could not be found.";
            await msg.ModifyAsync(x => x.Embed = embed.Build());
            await msg.DeleteAfterSeconds(10f);
            return null;
        }
        return cityResult;
    }
    
    private async Task<AirportService.AirLabsAirport> GetAirport(WeatherModule.WeatherContainer.Result weather, EmbedBuilder embed, IUserMessage msg)
    {
        var airportResult = await AirportService.GetClosestAirport(weather.coord.Lat, weather.coord.Lon);
        if (airportResult == null)
        {
            embed.Description += $"\nAirport near {weather.name} ({weather.sys.country}) could not be found.";
            await msg.ModifyAsync(x => x.Embed = embed.Build());
            await msg.DeleteAfterSeconds(10f);
            return null;
        }
        return airportResult;
    }

    #endregion // Utility Methods
    
}