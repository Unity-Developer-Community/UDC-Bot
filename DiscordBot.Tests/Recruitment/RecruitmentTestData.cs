using DiscordBot.Services.Recruitment.Policy;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Settings.Options;

namespace DiscordBot.Tests.Recruitment;

internal static class RecruitmentTestData
{
    public static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    public static RecruitmentOptions Options() => new()
    {
        Enabled = true,
        Mode = RecruitmentMode.Enforce,
        Forums = new()
        {
            PaidRecruiting = new() { ChannelId = 101 }, PaidForHire = new() { ChannelId = 102 },
            HobbyRecruiting = new() { ChannelId = 103 }, HobbyForHire = new() { ChannelId = 104 }
        },
        FeedChannelId = 105,
        EnforceGuidelineTimeouts = true, EnforceLifecycleClosures = true, EnforceListingLimits = true
    };

    public static PostRecord Post(ulong id = 10, ForumKind forum = ForumKind.PaidRecruiting,
        DateTimeOffset? created = null, bool accepted = false) => new()
    {
        ThreadId = id, AuthorId = 123, ParentChannelId = (ulong)forum + 101, Forum = forum,
        CreatedAtUtc = created ?? Now, FirstSeenAtUtc = created ?? Now,
        AcceptedAtUtc = accepted ? created ?? Now : null,
        Acknowledgement = AcknowledgementStatus.Passed,
        EnforcementEnrolled = true
    };

    public static StateDocument State(params PostRecord[] posts) => new()
    {
        GuildId = 1, EnrolledAtUtc = Now.AddDays(-90), Posts = posts.ToDictionary(p => p.ThreadId)
    };

    public static PolicyEvaluator Policy(RecruitmentOptions? options = null) =>
        new(options ?? Options(), new FixedTimeProvider(Now));
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
