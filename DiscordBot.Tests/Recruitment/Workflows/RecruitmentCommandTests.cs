using DiscordBot.Utils;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentCommandTests
{
    [Theory]
    [InlineData("reset", "Only an open unaccepted attempt")]
    [InlineData("unknown-operation", "Unknown recruitment operation")]
    public async Task AttemptedStaffCommandsStillAuditBeforeRejectingPreconditions(string operation, string response)
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(); await rig.Ack();
        var count = post.Audit.Count;
        Assert.Contains(response, await rig.Staff(operation));
        Assert.Equal(count + 1, post.Audit.Count);
        Assert.StartsWith(operation + ": ", post.Audit[^1].Reason);
        Assert.NotNull(post.AcceptedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1900)]
    [InlineData(6200)]
    public void ReplyChunksKeepCompleteTextAndUnicodeWithinDiscordLimit(int length)
    {
        var text = new string('x', length) + "\n```\n" + new string('a', 1898) + "🦆\n```";
        var chunks = DiscordReplyChunks.Split(text).ToArray();
        Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, chunk =>
        {
            Assert.InRange(chunk.Length, 1, 1900);
            Assert.False(char.IsHighSurrogate(chunk[^1]));
            Assert.False(char.IsLowSurrogate(chunk[0]));
        });
        Assert.Empty(DiscordReplyChunks.Split(""));
    }
}
