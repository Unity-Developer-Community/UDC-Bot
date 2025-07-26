using Discord.WebSocket;
using DiscordBot.Domain;

public class PokerDiscordGameSession : DiscordGameSession<Poker>
{
    public PokerDiscordGameSession(Poker game, int maxSeats, DiscordSocketClient client, SocketUser user, IGuild guild)
        : base(game, maxSeats, client, user, guild)
    { }

    private string GetCurrentPlayerName()
    {
        if (Game.CurrentPlayer == null) return "No one (all players finished)";
        return GetPlayerName((DiscordGamePlayer)Game.CurrentPlayer);
    }

    private string GenerateGameDescription()
    {
        var description = "**Five Card Draw Poker** - Each player gets 5 cards and can discard up to 5 cards once.\n\n";

        if (Game.State == GameState.InProgress)
        {
            description += $"**Current Turn:** {GetCurrentPlayerName()}\n\n";

            description += "**Players:**\n";
            foreach (var p in Players)
            {
                var playerData = Game.GameData[p];
                var status = playerData.HasDiscarded ? "✅ Finished" : (Game.CurrentPlayer == p ? "🎯 Playing" : "⏳ Waiting");
                description += $"• {GetPlayerName(p)}: {status} (Bet: {p.Bet})\n";
            }

            if (Game.CurrentPlayer != null)
            {
                description += "\n*Use the 'Show Hand' button to see your cards privately.*\n";
                description += "*Select cards you want to discard, then click 'Confirm Discard'.*";
            }
        }

        return description;
    }

    private new string GenerateResultsDescription()
    {
        var description = "\n**Final Results:**\n";

        // Evaluate all hands and sort by rank
        var playerHands = Players.Select(p =>
        {
            var hand = Game.GameData[p].FinalHand ?? PokerHelper.EvaluateHand(Game.GameData[p].PlayerCards);
            return (player: (GamePlayer)p, hand);
        }).OrderByDescending(ph => ph.hand.Rank).ToList();

        var winners = PokerHelper.DetermineWinners(playerHands);

        foreach (var (player, hand) in playerHands)
        {
            var result = Game.GetPlayerGameResult(player);
            var payout = Game.CalculatePayout(player, GetTotalPot);
            var resultEmoji = result switch
            {
                GamePlayerResult.Won => "🏆",
                GamePlayerResult.Lost => "❌",
                _ => "❓"
            };

            var cards = string.Join(" ", Game.GameData[(DiscordGamePlayer)player].PlayerCards.Select(c => c.GetDisplayName()));
            description += $"{resultEmoji} **{GetPlayerName((DiscordGamePlayer)player)}**: {hand.Description}\n";
            description += $"   *Cards: {cards}* (Payout: {payout:+0;-0;0})\n\n";
        }

        return description;
    }

    protected override Embed GenerateInProgressEmbed()
    {
        var description = GenerateGameDescription();

        var embed = new EmbedBuilder()
            .WithTitle($"🃏 {GameName} Game")
            .WithDescription(description)
            .WithColor(Color.Blue);

        if (Game.State == GameState.InProgress)
        {
            embed.AddField("Total Pot", $"{GetTotalPot} tokens", true);
            
            if (Game.CurrentPlayer != null)
            {
                embed.AddField("Current Player", GetCurrentPlayerName(), true);
            }
        }

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

    /// <summary>
    /// Override to provide custom buttons for poker gameplay
    /// </summary>
    public MessageComponent GeneratePokerInProgressButtons()
    {
        var components = new ComponentBuilder();
        
        // Add Show Hand button
        components.WithButton(new ButtonBuilder
        {
            CustomId = $"show_hand:{Id}",
            Label = "Show Hand",
            Style = ButtonStyle.Secondary,
            Emote = new Emoji("👁️")
        });

        // Add card selection buttons in a row
        var cardRow = new ActionRowBuilder();
        for (int i = 1; i <= 5; i++)
        {
            cardRow.WithButton(new ButtonBuilder
            {
                CustomId = $"action:{Id}:SelectCard{i}",
                Label = $"Card {i}",
                Style = ButtonStyle.Primary,
                Emote = new Emoji("🃏")
            });
        }
        components.AddRow(cardRow);

        // Add confirm discard button
        components.WithButton(new ButtonBuilder
        {
            CustomId = $"action:{Id}:ConfirmDiscard",
            Label = "Confirm Discard",
            Style = ButtonStyle.Success,
            Emote = new Emoji("✅")
        });

        return components.Build();
    }

    /// <summary>
    /// Hide the base method to use custom poker buttons when in progress
    /// </summary>
    public new async Task<(Embed, MessageComponent)> GenerateEmbedAndButtons()
    {
        var embed = await GenerateEmbed();
        
        // Use custom buttons for in-progress poker games
        if (Game.State == GameState.InProgress)
        {
            var buttons = GeneratePokerInProgressButtons();
            return (embed, buttons);
        }
        
        // Use default buttons for other states
        var (_, defaultButtons) = await base.GenerateEmbedAndButtons();
        return (embed, defaultButtons);
    }

    private async Task<Embed> GenerateEmbed()
    {
        return Game.State switch
        {
            GameState.NotStarted => await GenerateNotStartedEmbed(),
            GameState.InProgress => GenerateInProgressEmbed(),
            GameState.Finished => GenerateFinishedEmbed(),
            GameState.Abandoned => GenerateAbandonedEmbed(),
            _ => throw new InvalidOperationException("Unknown game state")
        };
    }

    private async Task<Embed> GenerateNotStartedEmbed()
    {
        var challenger = await Guild.GetUserAsync(User.Id);

        return new EmbedBuilder()
            .WithTitle($"{Game.Emoji} {GameName} Game Session")
            .WithDescription($"Welcome to {GameName}! Click the buttons below to take actions.")
            .WithAuthor($"Game started by {challenger.DisplayName}")
            .WithColor(Color.Green)
            .AddField("Players", GeneratePlayersList(), true)
            .AddField("Seats Available", $"{PlayerCount}/{MaxSeats}", true)
            .AddField("Total Pot", $"{GetTotalPot}")
            .WithFooter($"Game started by {challenger.DisplayName} • Minimum {Game.MinPlayers} players ready required to start the game.")
            .Build();
    }

    private Embed GenerateAbandonedEmbed()
    {
        return new EmbedBuilder()
            .WithTitle($"{Game.Emoji} {GameName} Abandoned")
            .Build();
    }

    private string GeneratePlayersList()
    {
        if (Players.Count == 0) return "None";
        var lines = Players.Select(p =>
            $"{GetPlayerName(p)} {(p.IsReady ? '✅' : '❌')} (Bet: {p.Bet})"
        );
        return string.Join("\n", lines);
    }
}