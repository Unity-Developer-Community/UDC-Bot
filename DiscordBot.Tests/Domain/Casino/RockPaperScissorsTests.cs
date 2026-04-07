using DiscordBot.Domain;

namespace DiscordBot.Tests.Domain.Casino;

public class RockPaperScissorsTests
{
    private static (RockPaperScissors game, GamePlayer p1, GamePlayer p2) CreateGame()
    {
        var game = new RockPaperScissors();
        var p1 = new GamePlayer { Bet = 100 };
        var p2 = new GamePlayer { Bet = 100 };
        game.StartGame([p1, p2]);
        return (game, p1, p2);
    }

    [Theory]
    [InlineData(RockPaperScissorsPlayerAction.Rock, RockPaperScissorsPlayerAction.Scissors, GamePlayerResult.Won)]
    [InlineData(RockPaperScissorsPlayerAction.Paper, RockPaperScissorsPlayerAction.Rock, GamePlayerResult.Won)]
    [InlineData(RockPaperScissorsPlayerAction.Scissors, RockPaperScissorsPlayerAction.Paper, GamePlayerResult.Won)]
    [InlineData(RockPaperScissorsPlayerAction.Scissors, RockPaperScissorsPlayerAction.Rock, GamePlayerResult.Lost)]
    [InlineData(RockPaperScissorsPlayerAction.Rock, RockPaperScissorsPlayerAction.Paper, GamePlayerResult.Lost)]
    [InlineData(RockPaperScissorsPlayerAction.Paper, RockPaperScissorsPlayerAction.Scissors, GamePlayerResult.Lost)]
    [InlineData(RockPaperScissorsPlayerAction.Rock, RockPaperScissorsPlayerAction.Rock, GamePlayerResult.Tie)]
    [InlineData(RockPaperScissorsPlayerAction.Paper, RockPaperScissorsPlayerAction.Paper, GamePlayerResult.Tie)]
    [InlineData(RockPaperScissorsPlayerAction.Scissors, RockPaperScissorsPlayerAction.Scissors, GamePlayerResult.Tie)]
    public void AllCombinations_CorrectResult(RockPaperScissorsPlayerAction p1Choice, RockPaperScissorsPlayerAction p2Choice, GamePlayerResult expectedP1Result)
    {
        var (game, p1, p2) = CreateGame();
        game.DoPlayerAction(p1, p1Choice);
        game.DoPlayerAction(p2, p2Choice);
        Assert.Equal(expectedP1Result, game.GetPlayerGameResult(p1));
    }

    [Fact]
    public void NoChoice_ReturnsNoResult()
    {
        var (game, p1, _) = CreateGame();
        Assert.Equal(GamePlayerResult.NoResult, game.GetPlayerGameResult(p1));
    }

    [Fact]
    public void Payout_WinnerGainsTotalPot()
    {
        var (game, p1, p2) = CreateGame();
        game.DoPlayerAction(p1, RockPaperScissorsPlayerAction.Rock);
        game.DoPlayerAction(p2, RockPaperScissorsPlayerAction.Scissors);
        var results = game.EndGame();
        var p1Payout = results.First(r => r.player == p1).payout;
        Assert.Equal(100, p1Payout); // wins 200 total pot - 100 bet = 100 net
    }

    [Fact]
    public void Payout_LoserLosesBet()
    {
        var (game, p1, p2) = CreateGame();
        game.DoPlayerAction(p1, RockPaperScissorsPlayerAction.Rock);
        game.DoPlayerAction(p2, RockPaperScissorsPlayerAction.Scissors);
        var results = game.EndGame();
        var p2Payout = results.First(r => r.player == p2).payout;
        Assert.Equal(-100, p2Payout);
    }

    [Fact]
    public void Payout_TieIsZero()
    {
        var (game, p1, p2) = CreateGame();
        game.DoPlayerAction(p1, RockPaperScissorsPlayerAction.Rock);
        game.DoPlayerAction(p2, RockPaperScissorsPlayerAction.Rock);
        var results = game.EndGame();
        Assert.All(results, r => Assert.Equal(0, r.payout));
    }

    [Fact]
    public void DoPlayerAction_AlreadyChosen_Throws()
    {
        var (game, p1, _) = CreateGame();
        game.DoPlayerAction(p1, RockPaperScissorsPlayerAction.Rock);
        Assert.Throws<InvalidOperationException>(() => game.DoPlayerAction(p1, RockPaperScissorsPlayerAction.Paper));
    }

    [Fact]
    public void ShouldFinish_BothChosen_True()
    {
        var (game, p1, p2) = CreateGame();
        game.DoPlayerAction(p1, RockPaperScissorsPlayerAction.Rock);
        game.DoPlayerAction(p2, RockPaperScissorsPlayerAction.Scissors);
        Assert.True(game.ShouldFinish());
    }

    [Fact]
    public void ShouldFinish_OneChosen_False()
    {
        var (game, p1, _) = CreateGame();
        game.DoPlayerAction(p1, RockPaperScissorsPlayerAction.Rock);
        Assert.False(game.ShouldFinish());
    }
}
