using Discord;
using DiscordBot.Domain;
using DiscordBot.Services.Fun.Casino;

namespace DiscordBot.Tests.Services;

public class SlotsMessageBuilderTests
{
    private static SlotResult ResultFor(bool respin = false, params SlotSymbol[] line)
    {
        var stops = line.Select((symbol, reel) => SlotMachine.Reels[reel].ToList().IndexOf(symbol)).ToArray();
        var (multiplier, isJackpot) = SlotMachine.Evaluate(line);
        return new SlotResult(stops, line, multiplier, isJackpot, false, respin);
    }

    private static SlotsPlay Play(SlotResult result, long payout, int bet = 10, byte[]? gif = null, SlotsHold? hold = null) => new()
    {
        Status = SlotsStatus.Ok,
        Result = result,
        Bet = bet,
        Payout = payout,
        Net = payout - (result.IsRespin ? 0 : bet),
        Balance = 1_234,
        PotHundredths = 567_800,
        Gif = gif,
        Hold = hold
    };

    private static readonly SlotResult Loss = ResultFor(false, SlotSymbol.Lemon, SlotSymbol.Bell, SlotSymbol.Grape);
    private static readonly SlotResult Sevens = ResultFor(false, SlotSymbol.Seven, SlotSymbol.Seven, SlotSymbol.Seven);
    private static readonly SlotResult Jackpot = ResultFor(false, SlotSymbol.Diamond, SlotSymbol.Diamond, SlotSymbol.Diamond);

    private static List<ButtonComponent> Buttons(MessageComponent components) =>
        components.Components.OfType<ActionRowComponent>().SelectMany(row => row.Components).OfType<ButtonComponent>().ToList();

    #region Private result (AC1)

    [Fact]
    public void ResultText_ShowsSymbolsOutcomeBalanceAndJackpot()
    {
        var text = SlotsMessageBuilder.ResultText(Play(Sevens, 1_000));

        Assert.Contains("🍋", SlotsMessageBuilder.ResultText(Play(Loss, 0)));
        Assert.Contains("7️⃣ 7️⃣ 7️⃣", text);
        Assert.Contains("1,000", text);
        Assert.Contains("100×", text);
        Assert.Contains("1,234", text);
        Assert.Contains("5,678", text); // 567,800 hundredths
    }

    [Fact]
    public void ResultText_OfALoss_NamesTheLostBet()
    {
        var text = SlotsMessageBuilder.ResultText(Play(Loss, 0, bet: 25));

        Assert.Contains("No win", text);
        Assert.Contains("25", text);
    }

    [Fact]
    public void ResultText_OfTheJackpot_SaysSoInWords()
    {
        Assert.Contains("JACKPOT", SlotsMessageBuilder.ResultText(Play(Jackpot, 5_000)));
    }

    [Fact]
    public void ResultEmbed_WithAGif_ReferencesTheAttachment()
    {
        var embed = SlotsMessageBuilder.BuildResultEmbed(Play(Loss, 0, gif: [1]));

        Assert.Equal("attachment://spin.gif", embed.Image?.Url);
    }

    [Fact]
    public void ResultEmbed_WithoutAGif_FallsBackToTheTextResult()
    {
        var embed = SlotsMessageBuilder.BuildResultEmbed(Play(Sevens, 1_000, gif: null));

        Assert.Null(embed.Image);
        Assert.Contains("7️⃣ 7️⃣ 7️⃣", embed.Description);
        Assert.Contains("1,000", embed.Description);
        Assert.Contains("animation could not be shown", embed.Description);
    }

    #endregion

    #region Buttons (AC9)

    [Fact]
    public void Components_WithoutAHold_OfferOnlySpinAgainAtTheSameBet()
    {
        var button = Assert.Single(Buttons(SlotsMessageBuilder.BuildComponents(25, null)));

        Assert.Equal("slots_spin:25", button.CustomId);
        Assert.Contains("25", button.Label);
    }

    [Fact]
    public void Components_WithAHold_ShowAToggleForEachReelAndTheRespin()
    {
        var hold = new SlotsHold(7, 10, [false, true, false]);

        var buttons = Buttons(SlotsMessageBuilder.BuildComponents(10, hold));

        Assert.Equal(5, buttons.Count);
        Assert.Equal(["slots_hold:7:0", "slots_hold:7:1", "slots_hold:7:2", "slots_respin:7", "slots_spin:10"], buttons.Select(b => b.CustomId));
    }

    [Fact]
    public void Components_HeldAndNotHeldReelsDifferByLabelNotOnlyByColor()
    {
        var hold = new SlotsHold(7, 10, [false, true, false]);

        var buttons = Buttons(SlotsMessageBuilder.BuildComponents(10, hold));

        Assert.Contains("Held", buttons[1].Label);
        Assert.DoesNotContain("Held", buttons[0].Label);
        Assert.Contains("Hold", buttons[0].Label);
    }

    #endregion

    #region Public post (AC7, AC8)

    [Fact]
    public void PublicText_OfABigWin_NamesThePlayerAndTheWin()
    {
        var text = SlotsMessageBuilder.PublicText(Play(Sevens, 1_000), "<@42>");

        Assert.Contains("<@42>", text);
        Assert.Contains("1,000", text);
        Assert.Contains("100×", text);
    }

    [Fact]
    public void PublicText_OfTheJackpot_AnnouncesIt()
    {
        var text = SlotsMessageBuilder.PublicText(Play(Jackpot, 5_000), "<@42>");

        Assert.Contains("JACKPOT", text);
        Assert.Contains("5,000", text);
    }

    [Fact]
    public void PublicEmbed_CarriesTheGif()
    {
        Assert.Equal("attachment://spin.gif", SlotsMessageBuilder.BuildPublicEmbed(Play(Jackpot, 5_000, gif: [1])).Image?.Url);
        Assert.Null(SlotsMessageBuilder.BuildPublicEmbed(Play(Jackpot, 5_000, gif: null)).Image);
    }

    #endregion

    #region Refusals and info (AC3, AC10)

    [Theory]
    [InlineData(SlotsStatus.InvalidBet, "1")]
    [InlineData(SlotsStatus.InsufficientTokens, "enough tokens")]
    [InlineData(SlotsStatus.Busy, "still being processed")]
    [InlineData(SlotsStatus.NoHold, "no longer available")]
    public void RefusalText_ExplainsEachRefusal(SlotsStatus status, string expected)
    {
        Assert.Contains(expected, SlotsMessageBuilder.RefusalText(status));
    }

    [Fact]
    public void InfoEmbed_ListsTheWholePaytableTheLimitsAndThePot()
    {
        var embed = SlotsMessageBuilder.BuildInfoEmbed(250_000);
        var text = string.Join("\n", embed.Fields.Select(f => f.Value)) + embed.Description;

        foreach (var entry in SlotMachine.Paytable)
            Assert.Contains(entry.Pattern, text);
        Assert.Contains("2,500", text);
        Assert.Contains($"{SlotMachine.MinBet}–{SlotMachine.MaxBet}", text);
        Assert.Contains("respin", text);
    }

    #endregion
}
