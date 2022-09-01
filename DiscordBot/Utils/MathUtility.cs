namespace DiscordBot.Utils;

public static class MathUtility
{
    public static float CelsiusToFahrenheit(float value)
    {
        return (float)Math.Round(value * 1.8f + 32, 2);
    }

    public static float FahrenheitToCelsius(float value)
    {
        return (float)Math.Round((value - 32) * 0.555555f, 2);
    }
}