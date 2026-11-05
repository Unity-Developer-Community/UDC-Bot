namespace CasinoWeb.Hubs;

/// <summary>Marks a hub method that plays a game: it is refused while the casino is closed and counts toward the rate limit.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class GameActionAttribute : Attribute;
