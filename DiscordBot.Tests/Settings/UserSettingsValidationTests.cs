using DiscordBot.Settings;

namespace DiscordBot.Tests.Settings;

public class UserSettingsValidationTests
{
    [Fact]
    public void DefaultSettings_NoWarnings()
    {
        var settings = new UserSettings();
        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void XpMinGreaterThanMax_Warning()
    {
        var settings = new UserSettings { XpMinPerMessage = 50, XpMaxPerMessage = 10 };
        var warnings = settings.Validate();
        Assert.Contains(warnings, w => w.Contains("XpMinPerMessage"));
    }

    [Fact]
    public void CooldownMinGreaterThanMax_Warning()
    {
        var settings = new UserSettings { XpMinCooldown = 200, XpMaxCooldown = 60 };
        var warnings = settings.Validate();
        Assert.Contains(warnings, w => w.Contains("XpMinCooldown"));
    }

    [Fact]
    public void ThanksCooldownZeroOrNegative_Warning()
    {
        var settings = new UserSettings { ThanksCooldown = 0 };
        var warnings = settings.Validate();
        Assert.Contains(warnings, w => w.Contains("ThanksCooldown"));
    }

    [Fact]
    public void EmptyThanksList_Warning()
    {
        var settings = new UserSettings { Thanks = [] };
        var warnings = settings.Validate();
        Assert.Contains(warnings, w => w.Contains("Thanks list is empty"));
    }
}
