using DiscordBot.Settings;

namespace DiscordBot.Tests.Settings;

public class BotSettingsValidationTests
{
    private static BotSettings CreateValid() => new()
    {
        Token = "test-token",
        GuildId = 123456,
        Prefix = '!',
        DbConnectionString = "Host=localhost;Database=test",
        ServerRootPath = "/data",
        Channels = new ChannelSettings
        {
            General = new ChannelInfo { Id = 1 },
            Introduction = new ChannelInfo { Id = 2 },
            BotAnnouncement = new ChannelInfo { Id = 3 },
            BotCommands = new ChannelInfo { Id = 4 },
            UnityNews = new ChannelInfo { Id = 5 },
            UnityReleases = new ChannelInfo { Id = 6 },
            Rules = new ChannelInfo { Id = 7 },
            GenericHelp = new ChannelInfo { Id = 9 },
            BirthdayAnnouncement = new ChannelInfo { Id = 10 },
            Meme = new ChannelInfo { Id = 11 },
        }
    };

    [Fact]
    public void ValidSettings_NoErrors()
    {
        var (errors, _) = CreateValid().Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void MissingToken_Error()
    {
        var settings = CreateValid();
        settings.Token = "";
        var (errors, _) = settings.Validate();
        Assert.Contains(errors, e => e.Contains("Token"));
    }

    [Fact]
    public void MissingGuildId_Error()
    {
        var settings = CreateValid();
        settings.GuildId = 0;
        var (errors, _) = settings.Validate();
        Assert.Contains(errors, e => e.Contains("GuildId"));
    }

    [Fact]
    public void MissingPrefix_Error()
    {
        var settings = CreateValid();
        settings.Prefix = '\0';
        var (errors, _) = settings.Validate();
        Assert.Contains(errors, e => e.Contains("Prefix"));
    }

    [Fact]
    public void MissingDbConnectionString_Error()
    {
        var settings = CreateValid();
        settings.DbConnectionString = "";
        var (errors, _) = settings.Validate();
        Assert.Contains(errors, e => e.Contains("DbConnectionString"));
    }

    [Fact]
    public void EmptyServerRootPath_Warning()
    {
        var settings = CreateValid();
        settings.ServerRootPath = "";
        var (_, warnings) = settings.Validate();
        Assert.Contains(warnings, w => w.Contains("ServerRootPath"));
    }

    [Fact]
    public void MissingChannel_Warning()
    {
        var settings = CreateValid();
        settings.Channels.General = new ChannelInfo { Id = 0 };
        var (_, warnings) = settings.Validate();
        Assert.Contains(warnings, w => w.Contains("General"));
    }

    [Fact]
    public void NegativeCasinoStartingTokens_Error()
    {
        var settings = CreateValid();
        settings.Casino = new CasinoSettings { Enabled = true, StartingTokens = -1 };
        var (errors, _) = settings.Validate();
        Assert.Contains(errors, e => e.Contains("StartingTokens"));
    }

    [Fact]
    public void RecruitmentEnabled_MissingForums_WarningsWithoutFatalErrors()
    {
        var settings = CreateValid();
        settings.Recruitment = new RecruitmentSettings { Enabled = true };
        var (errors, warnings) = settings.Validate();
        Assert.Empty(errors);
        Assert.Contains(warnings, w => w.Contains("four named forum IDs"));
        Assert.Contains(warnings, w => w.Contains("FeedChannelId"));
    }

    [Fact]
    public void RecruitmentDisabled_NoRecruitmentWarnings()
    {
        var settings = CreateValid();
        settings.Recruitment = new RecruitmentSettings { Enabled = false };
        var (_, warnings) = settings.Validate();
        Assert.DoesNotContain(warnings, w => w.Contains("Recruitment"));
    }

    [Fact]
    public void UnityHelpEnabled_MissingTagResolved_Warning()
    {
        var settings = CreateValid();
        settings.UnityHelp = new UnityHelpSettings { BabySitterEnabled = true, TagResolved = "" };
        var (_, warnings) = settings.Validate();
        Assert.Contains(warnings, w => w.Contains("TagResolved"));
    }
}
