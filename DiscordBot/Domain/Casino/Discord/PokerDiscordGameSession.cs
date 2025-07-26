using Discord.WebSocket;
using DiscordBot.Domain;

public class PokerDiscordGameSession : DiscordGameSession<Poker>
{
    public PokerDiscordGameSession(Poker game, int maxSeats, DiscordSocketClient client, SocketUser user, IGuild guild)
        : base(game, maxSeats, client, user, guild)
    { }

    private string GenerateGameDescription()
    {
        var description = "**Five Card Draw Poker** - Each player gets 5 cards and can discard up to 5 cards once.\n\n";

        description += "**Players:**\n";
        foreach (var p in Players)
        {
            var playerData = Game.GameData[p];
            var status = playerData.HasDiscarded ? "✅ Finished" : (Game.CurrentPlayer == p ? "🎯 Playing" : "⏳ Waiting");

            if (State == GameState.Finished)
            {
                status = string.Join(" ", Game.GameData[p].PlayerCards.OrderByDescending(c => c).Select(c => c.GetDisplayName()));
                var playerHand = Game.GameData[p].FinalHand;
                if (playerHand != null)
                {
                    status += $" - **{playerHand.Description}**";
                }
            }

            description += GeneratePlayerHandDescription(p, status, "");
        }

        if (Game.CurrentPlayer != null)
        {
            description += $"\n**{GetCurrentPlayerName()} selected cards for discard:** \n";
            var currentPlayerData = Game.GameData[Game.CurrentPlayer];
            for (int i = 0; i < currentPlayerData.PlayerCards.Count; i++)
            {
                var isSelected = currentPlayerData.SelectedForDiscard[i];
                description += $"{(isSelected ? "✅" : "❌")} 🃏 ";
            }
            description += "\n*Use the 'Show Hand' button to see your cards privately.*\n";
            description += "*Select cards you want to discard, then click 'Confirm Discard'.*";
        }

        return description;
    }

    protected override Embed GenerateInProgressEmbed()
    {
        var description = GenerateGameDescription();

        var embed = new EmbedBuilder()
            .WithTitle($"🃏 {GameName} Game")
            .WithDescription(description)
            .WithColor(Color.Blue)
            .AddField("Current Player", GetCurrentPlayerWithTimer(), true);

        return embed.Build();
    }

    protected override Embed GenerateFinishedEmbed()
    {
        var description = GenerateGameDescription();
        description += GenerateResultsDescription();

        return new EmbedBuilder()
            .WithTitle($"🃏 {GameName} Finished")
            .WithDescription(description)
            .WithColor(Color.Gold)
            .AddField("Total Pot", $"{GetTotalPot} tokens", true)
            .Build();
    }

    public override Embed GenerateRules()
    {
        return base.GenerateRules()
            .ToEmbedBuilder()
            .WithDescription("**Five Card Draw Poker** - Get the best 5-card poker hand!\nType `/casino game poker 5` to start playing!")
            .AddField("🎯 **Objective**",
                "Get the best possible 5-card poker hand to win the pot!", false)
            .AddField("🎮 **How to Play**",
                "1. Each player is dealt 5 cards\n" +
                "2. Players take turns discarding unwanted cards\n" +
                "3. New cards are drawn to replace discarded ones\n" +
                "4. Best hand wins the entire pot!", false)
            .AddField("🏆 **Hand Rankings** (High to Low)",
                "• **Royal Flush** - A, K, Q, J, 10 (same suit)\n" +
                "• **Straight Flush** - 5 consecutive cards (same suit)\n" +
                "• **Four of a Kind** - 4 cards of same rank\n" +
                "• **Full House** - 3 of a kind + pair\n" +
                "• **Flush** - 5 cards of same suit\n" +
                "• **Straight** - 5 consecutive cards\n" +
                "• **Three of a Kind** - 3 cards of same rank\n" +
                "• **Two Pair** - 2 pairs of different ranks\n" +
                "• **One Pair** - 2 cards of same rank\n" +
                "• **High Card** - No pairs or combinations", false)
            .AddField("💡 **Tips**",
                "• Click 'Show Hand' to see your cards privately\n" +
                "• Select cards to discard by clicking the card buttons\n" +
                "• Click 'Confirm Discard' when ready\n" +
                "• You can discard 0-5 cards", false)
            .WithColor(Color.Purple)
            .Build();
    }
}