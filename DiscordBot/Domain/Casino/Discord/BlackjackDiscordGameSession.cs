using System.Configuration;
using Discord.WebSocket;
using DiscordBot.Domain;

public class BlackjackDiscordGameSession : DiscordGameSession<Blackjack>
{
    public BlackjackDiscordGameSession(Blackjack game, int maxSeats, DiscordSocketClient client, SocketUser user, IGuild guild)
        : base(game, maxSeats, client, user, guild)
    { }

    private string GetCurrentPlayerName()
    {
        if (Game.CurrentPlayer == null) return "Dealer";
        return GetPlayerName((DiscordGamePlayer)Game.CurrentPlayer);
    }

    private string GenerateTurnDescription()
    {
        var description = "";

        description += $"**Hands:**\n";
        foreach (var p in Players)
        {
            var playerBet = p.Bet;
            var playerCards = string.Join(" ", Game.GameData[p].PlayerCards.Select(c => c.GetDisplayName()));
            description += $"* **{GetPlayerName(p)}**: {playerCards} (Value: {Game.GetPlayerValue(p)})";
            if (Game.IsPlayerBusted(p)) description += $" **- BUSTED!**";
            if (Game.IsPlayerBlackjack(p)) description += " **- BLACKJACK!**";
            if (Game.GameData[p].Actions.LastOrDefault() == BlackjackPlayerAction.Stand) description += $" **- STANDING**";
            // List each player action
            description += "\n";
            description += $"-# *Bet: {playerBet}*";
            if (Game.GameData[p].Actions.Count > 0)
                description += $" - Actions: {string.Join(", ", Game.GameData[p].Actions.Select(a => a.ToString()))}";

            description += "\n";
        }

        description += "\n";

        var isDealerTurn = CurrentPlayer == null;
        var dealerCards = isDealerTurn
            ? string.Join(" ", Game.DealerCards.Select(c => c.GetDisplayName()))
            // Only show the first card's value until it's the dealer's turn
            : $"{Game.DealerCards.First().GetDisplayName()} ?";

        var dealerValue = isDealerTurn
            ? Game.GetDealerValue().ToString()
            // Only show the first card's value until it's the dealer's turn
            : $"{Game.DealerCards.First().Value}?";

        description += $"* **Dealer**: {dealerCards} (Value: {dealerValue})";
        if (Game.IsDealerBusted()) description += " **- BUSTED!**";
        if (Game.IsDealerBlackjack()) description += " **- BLACKJACK!**";
        if (Game.DealerActions.LastOrDefault() == BlackjackPlayerAction.Stand) description += " **- STANDING**";

        description += "\n";
        // List dealer actions
        if (Game.DealerActions.Count > 0)
            description += $"-# {string.Join(", ", Game.DealerActions.Select(a => a.ToString()))}";
        else
            description += "\n";

        description += "\n";

        return description;
    }

    protected override Embed GenerateInProgressEmbed()
    {
        var description = GenerateTurnDescription();

        return new EmbedBuilder()
            .WithTitle($"Game of {Game.Emoji} {GameName}")
            .WithDescription(description)
            .AddField("CurrentPlayer", GetCurrentPlayerName(), true)
            .Build();
    }

    protected override Embed GenerateFinishedEmbed()
    {
        var description = GenerateTurnDescription();
        description += GenerateResultsDescription();

        return new EmbedBuilder()
            .WithTitle($"{Game.Emoji} {GameName} Finished")
            .WithDescription(description)
            .Build();
    }

    public override Embed GenerateRules()
    {
        return base.GenerateRules()
        .ToEmbedBuilder()
        .WithDescription("**Objective:** Get as close to 21 as possible without going over, while beating the dealer's hand.\nType `/casino blackjack` to start playing!")
            .AddField("🎯 **Card Values**",
                "• **Number cards (2-10):** Face value\n" +
                "• **Face cards (J, Q, K):** 10 points\n" +
                "• **Ace:** 11 or 1 (whichever is better)", false)
            .AddField("🎮 **How to Play**",
                "1. Place your bet using the betting buttons\n" +
                "2. You and the dealer each get 2 cards\n" +
                "3. Your cards are shown, dealer shows only 1 card\n" +
                "4. Choose your action: **Hit**, **Stand**, or **Double Down**", false)
            .AddField("🔥 **Actions**",
                "• **Hit:** Take another card\n" +
                "• **Stand:** Keep your current hand and pass the turn to the dealer\n" +
                "• **Double Down:** Double your bet, take exactly 1 more card, then stand", false)
            .AddField("🏆 **Winning Conditions**",
                "• **Blackjack:** 21 with first 2 cards (Ace + 10-value card)\n" +
                "• **Regular Win:** Your total is closer to 21 than dealer's\n" +
                "• **Dealer Bust:** Dealer goes over 21\n" +
                "• **Push/Tie:** Same total as dealer", false)
            .AddField("💥 **Losing Conditions**",
                "• **Bust:** Your total goes over 21\n" +
                "• **Dealer Wins:** Dealer's total is closer to 21 than yours", false)
            .AddField("💰 **Payouts**",
                "• **Win/Dealer Bust:** 2x your bet\n" +
                "• **Push/Tie:** Get your bet back\n" +
                "• **Bust/Loss:** Lose your bet", false)
            .AddField("⚡ **Special Rules**",
                "• If you hit exactly 21 (not blackjack), the dealer automatically plays\n" +
                "• Dealer hits until 17 or more, after which they stand\n" +
                "• Dealer hits on a \"soft 17\" (17 with an Ace counted as 11)\n" +
                "• Games expire after 5 minutes of inactivity", false)
        .Build();
    }

}