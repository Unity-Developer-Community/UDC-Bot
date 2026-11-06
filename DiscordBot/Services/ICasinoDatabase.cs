namespace DiscordBot.Services;

/// <summary>The casino's access to its tables, so the web casino can run the casino services without <see cref="DatabaseService"/>, which owns the schema.</summary>
public interface ICasinoDatabase
{
    ICasinoRepo? CasinoQuery { get; }
}
