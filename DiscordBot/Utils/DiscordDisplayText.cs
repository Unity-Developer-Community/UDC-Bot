namespace DiscordBot.Utils;

/// <summary>Literal display text for Discord; truncation precedes character replacement.</summary>
public static class DiscordDisplayText
{
    public static string Literal(string value, int max = 180) => new string(value.Take(max)
        .Select(c => char.IsControl(c) || "`*_~|<>@[]\\".Contains(c) ? ' ' : c).ToArray());
    public static string Timestamp(DateTimeOffset date) => $"<t:{date.ToUnixTimeSeconds()}:F>";
}
