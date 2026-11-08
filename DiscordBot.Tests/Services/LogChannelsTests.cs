using DiscordBot.Services.Logging;
using DiscordBot.Settings;

namespace DiscordBot.Tests.Services;

public class LogChannelsTests
{
    private const ulong Announce = 1, Member = 2, Audit = 3, Startup = 4, Moderation = 5;

    private static ChannelInfo Info(ulong id) => new() { Id = id };

    private static ChannelSettings Channels(ulong member = 0, ulong audit = 0, ulong startup = 0, ulong moderation = 0) => new()
    {
        BotAnnouncement = Info(Announce),
        MemberLog = member != 0 ? Info(member) : null,
        MessageAudit = audit != 0 ? Info(audit) : null,
        Startup = startup != 0 ? Info(startup) : null,
        ModerationLog = moderation != 0 ? Info(moderation) : null,
    };

    private static readonly Func<ulong, bool> AllExist = _ => true;

    [Theory]
    [InlineData(LogTopic.General)]
    [InlineData(LogTopic.MessageAudit)]
    [InlineData(LogTopic.MemberLog)]
    [InlineData(LogTopic.Startup)]
    [InlineData(LogTopic.Moderation)]
    public void ResolveId_FallsBackToBotAnnouncement_WhenTopicUnset(LogTopic topic) =>
        Assert.Equal(Announce, LogChannels.ResolveId(Channels(), topic, AllExist));

    [Theory]
    [InlineData(LogTopic.MessageAudit, Audit)]
    [InlineData(LogTopic.MemberLog, Member)]
    [InlineData(LogTopic.Startup, Startup)]
    [InlineData(LogTopic.Moderation, Moderation)]
    public void ResolveId_UsesTopicChannel_WhenConfigured(LogTopic topic, ulong expected) =>
        Assert.Equal(expected, LogChannels.ResolveId(Channels(Member, Audit, Startup, Moderation), topic, AllExist));

    [Fact]
    public void ResolveId_General_IgnoresTopicChannels() =>
        Assert.Equal(Announce, LogChannels.ResolveId(Channels(Member, Audit, Startup, Moderation), LogTopic.General, AllExist));

    [Fact]
    public void ResolveId_FallsBack_WhenTopicChannelIsNotFound() =>
        Assert.Equal(Announce, LogChannels.ResolveId(Channels(audit: Audit), LogTopic.MessageAudit, id => id != Audit));

    [Fact]
    public void ResolveId_ReturnsZero_WithoutAnyChannel() =>
        Assert.Equal(0ul, LogChannels.ResolveId(new ChannelSettings(), LogTopic.Startup, AllExist));

    [Fact]
    public void IsLogChannel_CoversBotAnnouncementAndEveryConfiguredTopic()
    {
        var channels = Channels(Member, Audit, Startup, Moderation);
        foreach (var id in new[] { Announce, Member, Audit, Startup, Moderation })
            Assert.True(LogChannels.IsLogChannel(channels, id));
        Assert.False(LogChannels.IsLogChannel(channels, 99));
    }

    [Fact]
    public void IsLogChannel_IgnoresUnsetTopicsAndZero()
    {
        var channels = Channels(audit: Audit);
        Assert.False(LogChannels.IsLogChannel(channels, Member));
        Assert.False(LogChannels.IsLogChannel(channels, 0));
    }
}
