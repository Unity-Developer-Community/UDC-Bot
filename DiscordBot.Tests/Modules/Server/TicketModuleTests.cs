using DiscordBot.Modules.Server;

namespace DiscordBot.Tests.Modules.Server;

public class TicketModuleTests
{
    private const ulong ClosedCategoryId = 10;
    private const ulong OtherCategoryId = 20;

    private static IEnumerable<ulong?> Channels(int inClosed, int elsewhere = 5) =>
        Enumerable.Repeat<ulong?>(ClosedCategoryId, inClosed)
            .Concat(Enumerable.Repeat<ulong?>(OtherCategoryId, elsewhere))
            .Append(null);

    [Fact]
    public void IsCategoryFull_BelowLimit_IsFalse() =>
        Assert.False(TicketModule.IsCategoryFull(Channels(TicketModule.MaxChannelsPerCategory - 1), ClosedCategoryId));

    [Fact]
    public void IsCategoryFull_AtLimit_IsTrue() =>
        Assert.True(TicketModule.IsCategoryFull(Channels(TicketModule.MaxChannelsPerCategory), ClosedCategoryId));

    [Fact]
    public void IsCategoryFull_CountsOnlyThatCategory() =>
        Assert.False(TicketModule.IsCategoryFull(Channels(1, elsewhere: 100), ClosedCategoryId));
}
