using Discord.WebSocket;
using DiscordBot.Domain;

public class RockPaperScissorsDiscordGameSession : DiscordGameSession<RockPaperScissors>
{
    public RockPaperScissorsDiscordGameSession(RockPaperScissors game, int maxSeats, DiscordSocketClient client, SocketUser user, IGuild guild, bool isPrivate = false)
        : base(game, maxSeats, client, user, guild, isPrivate)
    { }

    private string GetCurrentPlayerName()
    {
        if (Game.CurrentPlayer == null) return "All players have chosen";
        return GetPlayerName((DiscordGamePlayer)Game.CurrentPlayer);
    }

    private string GenerateGameDescription()
    {
        var description = "**Players:**\n";

        foreach (var p in Players)
        {
            var playerData = Game.GameData[p]; ;
            var playerHand = playerData.HasMadeChoice ? "✅ Choice made" : "⏳ Waiting for choice";

            // If game is finished, show the choices
            if (Game.State == GameState.Finished)
            {
                var choice = Game.GameData[p].Choice;
                playerHand = $"{RockPaperScissors.GetChoiceEmoji(choice.Value)} {choice.Value}";
            }
            description += GeneratePlayerHandDescription(p, playerHand, "");
        }

        description += "\n";

        if (Game.State == GameState.Finished)
        {
            // Show who beat whom (if not a tie)
            if (Players.Count == 2)
            {
                var player1 = Players[0];
                var player2 = Players[1];
                var choice1 = Game.GameData[player1].Choice;
                var choice2 = Game.GameData[player2].Choice;

                if (choice1.HasValue && choice2.HasValue && choice1 != choice2)
                {
                    var winner = Game.GetPlayerGameResult(player1) == GamePlayerResult.Won ? player1 : player2;
                    var loser = winner == player1 ? player2 : player1;
                    var winnerChoice = Game.GameData[winner].Choice!.Value;
                    var loserChoice = Game.GameData[loser].Choice!.Value;

                    description += $"**{RockPaperScissors.GetBeatDescription(winnerChoice, loserChoice)}**\n";
                }
                else if (choice1 == choice2)
                {
                    description += "\n**It's a tie!**\n";
                }
            }
        }
        else if (Game.CurrentPlayer == null && Players.All(p => Game.GameData[p].HasMadeChoice))
        {
            description += "All players have made their choices! Revealing results...\n";
        }

        return description;
    }

    protected override Embed GenerateInProgressEmbed()
    {
        var description = GenerateGameDescription();

        var waitingFor = Game.CurrentPlayer != null ? $"Waiting for {GetCurrentPlayerName()}" : "All choices made - revealing results...";

        return new EmbedBuilder()
            .WithTitle($"Game of {Game.Emoji} {GameName}")
            .WithDescription(description)
            .AddField("Status", waitingFor, true)
            .AddField("Total Pot", $"{GetTotalPot} tokens", true)
            .WithColor(Color.Orange)
            .Build();
    }

    protected override Embed GenerateFinishedEmbed()
    {
        var description = GenerateGameDescription();
        description += GenerateResultsDescription();

        return new EmbedBuilder()
            .WithTitle($"{Game.Emoji} {GameName} Finished!")
            .WithDescription(description)
            .WithColor(Color.Green)
            .Build();
    }

    public override Embed GenerateRules()
    {
        return new EmbedBuilder()
            .WithTitle($"{Game.Emoji} {GameName} Rules")
            .WithDescription("**Objective:** Choose Rock, Paper, or Scissors and try to beat your opponent!\nType `/casino game rockpaperscissors` to start playing!")
            .AddField("🎯 **How to Win**",
                "• **Rock** 🪨 crushes **Scissors** ✂️\n" +
                "• **Paper** 📄 covers **Rock** 🪨\n" +
                "• **Scissors** ✂️ cuts **Paper** 📄", false)
            .AddField("🎮 **How to Play**",
                "1. Two players join the game and place their bets\n" +
                "2. Both players choose Rock, Paper, or Scissors simultaneously\n" +
                "3. Choices are revealed once both players have chosen\n" +
                "4. Winner is determined by the classic rules above", false)
            .AddField("🏆 **Winning Conditions**",
                "• **Win:** Your choice beats your opponent's choice\n" +
                "• **Tie:** Both players choose the same option\n" +
                "• **Lose:** Your opponent's choice beats yours", false)
            .AddField("💰 **Payouts**",
                "• **Win:** 2x your bet (you get your bet back + opponent's bet)\n" +
                "• **Tie:** Get your bet back (no loss, no gain)\n" +
                "• **Lose:** Lose your bet", false)
            .AddField("⚡ **Game Rules**",
                "• Exactly 2 players required\n" +
                "• Games expire after 2 minutes of inactivity\n" +
                "• Both players must make their choice to reveal results\n" +
                "• Choices are hidden until both players have chosen", false)
            .WithColor(Color.Blue)
            .Build();
    }
}