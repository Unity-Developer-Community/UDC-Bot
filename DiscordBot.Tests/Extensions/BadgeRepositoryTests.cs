using DiscordBot.Extensions;

namespace DiscordBot.Tests.Extensions;

public class BadgeRepositoryTests
{
    [Fact]
    public void EnsureBadgeDetails_UsesFlattenedBadgeColumns_WhenNavigationPropertyIsMissing()
    {
        var userBadge = new UserBadge
        {
            UserID = "123",
            BadgeId = 42,
            AwardedAt = new DateTime(2024, 01, 01, 12, 0, 0, DateTimeKind.Utc),
            AwardedBy = "456",
            BadgeTitle = "Test Badge",
            BadgeDescription = "A very nice badge.",
            BadgeGroupKey = "udcjam",
            BadgeIsPublic = true
        };

        var badge = userBadge.EnsureBadgeDetails();

        Assert.NotNull(badge);
        Assert.Equal("Test Badge", badge.Title);
        Assert.Equal("A very nice badge.", badge.Description);
        Assert.Equal("udcjam", badge.GroupKey);
        Assert.True(badge.IsPublic);
        Assert.Same(badge, userBadge.Badge);
    }
}
