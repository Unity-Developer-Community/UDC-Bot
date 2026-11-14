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
            Recruitment = new ChannelInfo { Id = 8 },
            GenericHelp = new ChannelInfo { Id = 9 },
            BirthdayAnnouncement = new ChannelInfo { Id = 10 },
            Meme = new ChannelInfo { Id = 11 },
            UnityDiscussion = new ChannelInfo { Id = 12 },
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
    public void RecruitmentEnabled_MissingTags_Warnings()
    {
        var settings = CreateValid();
        settings.Recruitment = new RecruitmentSettings { Enabled = true };
        var (_, warnings) = settings.Validate();
        Assert.Contains(warnings, w => w.Contains("TagLookingToHire"));
        Assert.Contains(warnings, w => w.Contains("TagLookingForWork"));
        Assert.Contains(warnings, w => w.Contains("TagUnpaidCollab"));
        Assert.Contains(warnings, w => w.Contains("TagPositionFilled"));
    }

    [Fact]
    public void RecruitmentDisabled_NoTagWarnings()
    {
        var settings = CreateValid();
        settings.Recruitment = new RecruitmentSettings { Enabled = false };
        var (_, warnings) = settings.Validate();
        Assert.DoesNotContain(warnings, w => w.Contains("TagLooking"));
    }

    [Fact]
    public void UnityHelpEnabled_MissingTagResolved_Warning()
    {
        var settings = CreateValid();
        settings.UnityHelp = new UnityHelpSettings { BabySitterEnabled = true, TagResolved = "" };
        var (_, warnings) = settings.Validate();
        Assert.Contains(warnings, w => w.Contains("TagResolved"));
    }

    [Fact]
    public void IntroductionWatchEnabled_ZeroDelay_Warning()
    {
        var settings = CreateValid();
        settings.IntroductionWatch = new IntroductionWatchSettings { Enabled = true, DelayMinutes = 0 };
        var (_, warnings) = settings.Validate();
        Assert.Contains(warnings, w => w.Contains("IntroductionWatch.DelayMinutes"));
    }

    [Fact]
    public void IntroductionWatchDisabled_ZeroDelay_NoWarning()
    {
        var settings = CreateValid();
        settings.IntroductionWatch = new IntroductionWatchSettings { Enabled = false, DelayMinutes = 0 };
        var (_, warnings) = settings.Validate();
        Assert.DoesNotContain(warnings, w => w.Contains("IntroductionWatch"));
    }

    [Fact]
    public void ImageSpamGuard_Defaults_NoWarnings()
    {
        var settings = CreateValid();
        settings.ImageSpamGuard = new ImageSpamGuardSettings { Enabled = true };
        var (_, warnings) = settings.Validate();
        Assert.DoesNotContain(warnings, w => w.Contains("ImageSpamGuard"));
        Assert.Equal((true, 4), (settings.ImageSpamGuard.SingleImage.Enabled, settings.ImageSpamGuard.SingleImage.Channels));
        Assert.Equal((true, 2), (settings.ImageSpamGuard.FourImages.Enabled, settings.ImageSpamGuard.FourImages.Channels));
        Assert.Equal((true, 3), (settings.ImageSpamGuard.OtherCount.Enabled, settings.ImageSpamGuard.OtherCount.Channels));
        Assert.Equal((true, 6), (settings.ImageSpamGuard.Mixed.Enabled, settings.ImageSpamGuard.Mixed.Channels));
        Assert.Equal(15, settings.ImageSpamGuard.WindowMinutes);
        Assert.Equal(24, settings.ImageSpamGuard.TimeoutHours);
    }

    [Theory]
    [InlineData(0, 24, "WindowMinutes")]
    [InlineData(15, 0, "TimeoutHours")]
    [InlineData(15, 673, "TimeoutHours")]
    public void ImageSpamGuard_InvalidValues_Warning(int windowMinutes, int timeoutHours, string expected)
    {
        var settings = CreateValid();
        settings.ImageSpamGuard = new ImageSpamGuardSettings
        {
            Enabled = true, WindowMinutes = windowMinutes, TimeoutHours = timeoutHours
        };
        var (_, warnings) = settings.Validate();
        Assert.Contains(warnings, w => w.Contains($"ImageSpamGuard.{expected}"));
    }

    [Theory]
    [InlineData(nameof(ImageSpamGuardSettings.SingleImage))]
    [InlineData(nameof(ImageSpamGuardSettings.FourImages))]
    [InlineData(nameof(ImageSpamGuardSettings.OtherCount))]
    [InlineData(nameof(ImageSpamGuardSettings.Mixed))]
    public void ImageSpamGuard_EnabledRuleWithoutChannels_Warning(string rule)
    {
        var settings = CreateValid();
        settings.ImageSpamGuard = new ImageSpamGuardSettings { Enabled = true };
        var ruleSettings = (ImageSpamRuleSettings)typeof(ImageSpamGuardSettings).GetProperty(rule)!.GetValue(settings.ImageSpamGuard)!;
        ruleSettings.Channels = 0;
        var (_, warnings) = settings.Validate();
        Assert.Contains(warnings, w => w.Contains($"ImageSpamGuard.{rule}.Channels"));

        ruleSettings.Enabled = false;
        (_, warnings) = settings.Validate();
        Assert.DoesNotContain(warnings, w => w.Contains("ImageSpamGuard"));
    }

    [Fact]
    public void ImageSpamGuard_PartialRuleJson_KeepsDefaultChannels()
    {
        // Newtonsoft fills the existing rule objects, so a config that only turns a rule off keeps its default.
        var guard = Newtonsoft.Json.JsonConvert.DeserializeObject<ImageSpamGuardSettings>(
            """{ "Enabled": true, "SingleImage": { "Enabled": false }, "Mixed": { "Channels": 8 } }""")!;
        Assert.Equal((false, 4), (guard.SingleImage.Enabled, guard.SingleImage.Channels));
        Assert.Equal((true, 8), (guard.Mixed.Enabled, guard.Mixed.Channels));
        Assert.Equal((true, 2), (guard.FourImages.Enabled, guard.FourImages.Channels));
    }

    [Fact]
    public void ImageSpamGuard_Disabled_InvalidValuesIgnored()
    {
        var settings = CreateValid();
        settings.ImageSpamGuard = new ImageSpamGuardSettings { Enabled = false, WindowMinutes = 0 };
        var (_, warnings) = settings.Validate();
        Assert.DoesNotContain(warnings, w => w.Contains("ImageSpamGuard"));
    }

    [Fact]
    public void FeatureSummary_ListsOnAndOff()
    {
        var settings = CreateValid();
        settings.Casino.Enabled = true;
        settings.Birthday.Enabled = false;

        var summary = settings.FeatureSummary();

        Assert.StartsWith("On: casino, username cards, blog discussion cards. Off: birthdays, image spam guard,", summary);
        Assert.EndsWith("display-name cards.", summary);
    }

    [Fact]
    public void BlogDiscussionCard_NoChannel_Warns()
    {
        foreach (var channel in new ChannelInfo?[] { null, new ChannelInfo { Id = 0 } })
        {
            var settings = CreateValid();
            settings.Channels.UnityDiscussion = channel;
            var (errors, warnings) = settings.Validate();
            Assert.Empty(errors);
            Assert.Contains(warnings, w => w.Contains("UnityDiscussion is not configured"));
        }
    }

    [Fact]
    public void BlogDiscussionCard_Disabled_NoChannelIsFine()
    {
        var settings = CreateValid();
        settings.Channels.UnityDiscussion = null;
        settings.UnityNews.BlogDiscussionCard = false;

        var (_, warnings) = settings.Validate();

        Assert.DoesNotContain(warnings, w => w.Contains("UnityDiscussion"));
        Assert.Contains("blog discussion cards", settings.FeatureSummary().Split("Off:")[1]);
    }

    [Fact]
    public void ValidSettings_NoWarnings()
    {
        var (_, warnings) = CreateValid().Validate();
        Assert.DoesNotContain(warnings, w => w.Contains("UnityDiscussion"));
        Assert.True(new UnityNewsSettings().BlogDiscussionCard);
    }
}
