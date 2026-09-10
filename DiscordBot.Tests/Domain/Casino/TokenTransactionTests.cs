using DiscordBot.Domain;

namespace DiscordBot.Tests.Domain.Casino;

public class TokenTransactionTests
{
    private static TokenTransaction Create() => new() { UserID = "123" };

    [Theory]
    [InlineData("TokenInitialisation", TransactionKind.TokenInitialisation)]
    [InlineData("DailyReward", TransactionKind.DailyReward)]
    [InlineData("Gift", TransactionKind.Gift)]
    [InlineData("Game", TransactionKind.Game)]
    [InlineData("Admin", TransactionKind.Admin)]
    public void Kind_Get_ValidString_ReturnsCorrectEnum(string type, TransactionKind expected)
    {
        var tx = Create();
        tx.TransactionType = type;
        Assert.Equal(expected, tx.Kind);
    }

    [Fact]
    public void Kind_Get_InvalidString_FallsBackToAdmin()
    {
        var tx = Create();
        tx.TransactionType = "SomethingInvalid";
        Assert.Equal(TransactionKind.Admin, tx.Kind);
    }

    [Fact]
    public void Kind_Set_UpdatesTransactionType()
    {
        var tx = Create();
        tx.Kind = TransactionKind.Gift;
        Assert.Equal("Gift", tx.TransactionType);
    }

    [Fact]
    public void Description_Set_ValidJson_ParsedToDict()
    {
        var tx = Create();
        tx.Description = """{"game":"blackjack","result":"win"}""";
        Assert.NotNull(tx.Details);
        Assert.Equal("blackjack", tx.Details!["game"]);
        Assert.Equal("win", tx.Details["result"]);
    }

    [Fact]
    public void Description_Set_PlainText_FallbackDict()
    {
        var tx = Create();
        tx.Description = "some plain text";
        Assert.NotNull(tx.Details);
        Assert.Equal("some plain text", tx.Details!["text"]);
    }

    [Fact]
    public void Description_Set_NullOrEmpty_EmptyDict()
    {
        var tx = Create();
        tx.Description = null;
        Assert.NotNull(tx.Details);
        Assert.Empty(tx.Details!);

        tx.Description = "";
        Assert.Empty(tx.Details!);
    }

    [Fact]
    public void Description_Get_SerializesToJson()
    {
        var tx = Create();
        tx.Details = new Dictionary<string, string> { ["key"] = "val" };
        var json = tx.Description;
        Assert.Contains("\"key\"", json);
        Assert.Contains("\"val\"", json);
    }

    [Fact]
    public void Description_Get_EmptyDetails_ReturnsNull()
    {
        var tx = Create();
        tx.Details = new Dictionary<string, string>();
        Assert.Null(tx.Description);
    }

    [Fact]
    public void Description_Get_NullDetails_ReturnsNull()
    {
        var tx = Create();
        tx.Details = null;
        Assert.Null(tx.Description);
    }
}
