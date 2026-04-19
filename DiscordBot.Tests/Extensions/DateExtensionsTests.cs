using DiscordBot.Extensions;

namespace DiscordBot.Tests.Extensions;

public class DateExtensionsTests
{
    [Fact]
    public void ToUnixTimestamp_Epoch_ReturnsZero()
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(0, epoch.ToUnixTimestamp());
    }

    [Fact]
    public void ToUnixTimestamp_KnownDate_ReturnsExpected()
    {
        var date = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(946684800, date.ToUnixTimestamp());
    }

    [Fact]
    public void ToUnixTimestamp_ReturnsPositiveForRecentDate()
    {
        var result = DateTime.UtcNow.ToUnixTimestamp();
        Assert.True(result > 0);
    }
}
