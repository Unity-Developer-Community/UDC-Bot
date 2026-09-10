using DiscordBot.Utils;

namespace DiscordBot.Tests.Utils;

public class MathUtilityTests
{
    [Theory]
    [InlineData(0f, 32f)]
    [InlineData(100f, 212f)]
    [InlineData(-40f, -40f)]
    [InlineData(37f, 98.6f)]
    public void CelsiusToFahrenheit_KnownValues(float celsius, float expectedF)
    {
        Assert.Equal(expectedF, MathUtility.CelsiusToFahrenheit(celsius), 1);
    }

    [Theory]
    [InlineData(32f, 0f)]
    [InlineData(212f, 100f)]
    [InlineData(-40f, -40f)]
    public void FahrenheitToCelsius_KnownValues(float fahrenheit, float expectedC)
    {
        Assert.Equal(expectedC, MathUtility.FahrenheitToCelsius(fahrenheit), 0);
    }

    [Fact]
    public void RoundTrip_CelsiusToFahrenheitAndBack()
    {
        var original = 25f;
        var fahrenheit = MathUtility.CelsiusToFahrenheit(original);
        var backToCelsius = MathUtility.FahrenheitToCelsius(fahrenheit);
        Assert.Equal(original, backToCelsius, 0);
    }
}
