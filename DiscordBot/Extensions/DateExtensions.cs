namespace DiscordBot.Extensions;

public static class DateExtensions
{
    public static long ToUnixTimestamp(this DateTime date)
    {
        return (long)(date.ToUniversalTime().Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
    }
}