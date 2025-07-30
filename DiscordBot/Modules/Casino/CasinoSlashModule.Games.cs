using Discord.Interactions;
using Discord.Net;
using DiscordBot.Domain;
using DiscordBot.Services;

namespace DiscordBot.Modules;

public enum CasinoGame
{
    Blackjack,
    [ChoiceDisplay("Rock Paper Scissors")]
    RockPaperScissors,
    Poker,
}

public partial class CasinoSlashModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Dependency Injection

    public GameService GameService { get; set; }

    #endregion
    #region Helper Methods

    private async Task GenerateResponse(IDiscordGameSession gameSession)
    {
        var (embed, components) = await gameSession.GenerateEmbedAndButtons();
        try
        {
            await ModifyOriginalResponseAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = components;
            });
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownWebhook)
        {
            // Generate a new response if the original webhook was deleted

            // Ping every participant to notify them of the new response
            var mentions = "";
            foreach (var player in gameSession.Players)
            {
                var user = await ((IGuild)Context.Guild).GetUserAsync(player.UserId);
                if (user != null) mentions += user.Mention;
            }
            await Context.Channel.SendMessageAsync($"{mentions}\nA new response has been generated for the game session.", embed: embed, components: components);
        }
    }

    /// <summary>
    /// Continues the game session by processing AI and dealer actions and checking if the game should finish.
    /// </summary>
    private async Task ContinueGame(IDiscordGameSession gameSession)
    {
        try
        {
            // Handle AI player actions
            var hasAIAction = gameSession.HasNextAIAction();
            while (hasAIAction)
            {
                await Task.Delay(100);
                await gameSession.DoNextAIAction();
                await GenerateResponse(gameSession);

                hasAIAction = gameSession.HasNextAIAction();
            }

            // Handle Dealer actions
            var hasDealerAction = gameSession.HasNextDealerAction();
            while (hasDealerAction)
            {
                await Task.Delay(1000);
                await gameSession.DoNextDealerAction();
                await GenerateResponse(gameSession);

                hasDealerAction = gameSession.HasNextDealerAction();
            }

            // Check if the game should finish
            if (gameSession.ShouldFinish())
            {
                await Task.Delay(2000);
                await GameService.EndGame(gameSession);
                await GenerateResponse(gameSession);
            }
        }
        catch (Exception ex)
        {
            await LoggingService.LogAction($"Error in ContinueGame: {ex.Message} {ex.StackTrace}", ExtendedLogSeverity.Error);
            await FollowupAsync($"An error occurred while continuing the game: {ex.Message}", ephemeral: true);
        }
    }

    #endregion
    #region Games Commands

    [SlashCommand("rules", "Learn how to play blackjack")]
    public async Task Rules(CasinoGame game)
    {
        await DeferAsync(ephemeral: true);
        var gameSession = GameService.CreateGameSession(game, 2, Context.Client, Context.User, Context.Guild);
        await FollowupAsync(embed: gameSession.GenerateRules(), ephemeral: true);
        GameService.RemoveGameSession(gameSession); // Remove the session since it's just for rules
    }

    [SlashCommand("game", "Play a game of blackjack")]
    public async Task CreateGameSession(CasinoGame game,
    [Summary("seats", "Number of seats for the game (minimum 1)")]
    [MinValue(1)]
    int seats = 0)
    {
        try
        {
            await DeferAsync();

            var gameSession = GameService.CreateGameSession(game, seats, Context.Client, Context.User, Context.Guild);
            await GameService.JoinGame(gameSession, Context.User.Id);

            await GenerateResponse(gameSession);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            await FollowupAsync($"Invalid number of seats: {ex.Message}", ephemeral: true);
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Failed to create game session: {ex.Message}", ExtendedLogSeverity.Warning);
            await FollowupAsync($"Failed to create game session: {ex.Message}", ephemeral: true);
        }
    }

    [ComponentInteraction("play_again:*", true)]
    public async Task PlayAgain(string id)
    {
        await DeferAsync();

        var gameSession = GameService.GetActiveSession(id);
        if (gameSession == null)
        {
            await FollowupAsync("Game session not found.", ephemeral: true);
            return;
        }

        GameService.PlayAgain(gameSession);
        await GameService.JoinGame(gameSession, Context.User.Id);

        await GenerateResponse(gameSession);
    }

    #endregion
    #region Join/Leave/Ready

    [ComponentInteraction("join_game:*", true)]
    public async Task JoinGame(string id)
    {
        await DeferAsync();

        var gameSession = GameService.GetActiveSession(id);
        if (gameSession == null)
        {
            await FollowupAsync("Game session not found.", ephemeral: true);
            return;
        }

        try
        {
            await GameService.JoinGame(gameSession, Context.User.Id);
            await GenerateResponse(gameSession);
            await Task.Delay(500);
            await ContinueGame(gameSession);
        }
        catch (InvalidOperationException ex)
        {
            await FollowupAsync(ex.Message, ephemeral: true);
            return;
        }

        await GenerateResponse(gameSession);
    }

    [ComponentInteraction("leave_game:*", true)]
    public async Task LeaveGame(string id)
    {
        try
        {
            await DeferAsync(); // defers with ephemeral = false by default

            var gameSession = GameService.GetActiveSession(id);
            if (gameSession == null)
            {
                await FollowupAsync($"Game session {id} not found.", ephemeral: true);
                return;
            }

            // Leave the game
            gameSession.RemovePlayer(Context.User.Id);
            await GenerateResponse(gameSession);
            await Task.Delay(500);
            await ContinueGame(gameSession);

            if (gameSession.State == GameState.Abandoned) GameService.RemoveGameSession(gameSession);
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Failed to leave game session: {ex.Message} {ex.StackTrace}", ExtendedLogSeverity.Warning);
            await FollowupAsync($"Failed to leave game session: {ex.Message}", ephemeral: true);
        }
    }

    [ComponentInteraction("toggle_ready:*", true)]
    public async Task ToggleReady(string id)
    {
        try
        {
            await DeferAsync();

            var gameSession = GameService.GetActiveSession(id);
            if (gameSession == null)
            {
                await FollowupAsync("Game session not found.", ephemeral: true);
                return;
            }

            var player = gameSession.GetPlayer(Context.User.Id);
            if (player == null) return;

            gameSession.SetPlayerReady(Context.User.Id, !player.IsReady);
            await GenerateResponse(gameSession);
            await Task.Delay(500);
            await ContinueGame(gameSession);

        }
        catch (Exception ex)
        {
            await LoggingService.LogAction($"Failed to toggle ready status: {ex.Message} {ex.StackTrace}", ExtendedLogSeverity.Warning);
            await FollowupAsync($"Failed to toggle ready status: {ex.Message}", ephemeral: true);
        }
    }

    [ComponentInteraction("ai_add:*", true)]
    public async Task AddAIPlayer(string id)
    {
        await DeferAsync();

        var gameSession = GameService.GetActiveSession(id);
        if (gameSession == null)
        {
            await FollowupAsync("Game session not found.", ephemeral: true);
            return;
        }

        gameSession.AddPlayerAI();
        await GenerateResponse(gameSession);
        await Task.Delay(500);
        await ContinueGame(gameSession);
    }

    [ComponentInteraction("ai_add_full:*", true)]
    public async Task AddAIPlayerFull(string id)
    {
        await DeferAsync();

        var gameSession = GameService.GetActiveSession(id);
        if (gameSession == null)
        {
            await FollowupAsync("Game session not found.", ephemeral: true);
            return;
        }

        // Add AI player to fill the game to maximum seats
        bool couldAddAI;
        do
        {
            couldAddAI = gameSession.AddPlayerAI();
        } while (couldAddAI);
        await GenerateResponse(gameSession);
        await Task.Delay(500);
        await ContinueGame(gameSession);
    }

    [ComponentInteraction("ai_remove:*", true)]
    public async Task RemoveAIPlayer(string id)
    {
        await DeferAsync();

        var gameSession = GameService.GetActiveSession(id);
        if (gameSession == null)
        {
            await FollowupAsync("Game session not found.", ephemeral: true);
            return;
        }

        gameSession.RemovePlayerAI();

        await GenerateResponse(gameSession);
    }

    #endregion
    #region Betting Actions

    [ComponentInteraction("bet_add:*:*", true)]
    public async Task BetAdd(string id, ulong amount)
    {
        await DeferAsync();

        var gameSession = GameService.GetActiveSession(id);
        if (gameSession == null)
        {
            await FollowupAsync("Game session not found.", ephemeral: true);
            return;
        }

        var player = gameSession.GetPlayer(Context.User.Id);
        if (player == null) return;

        var currentBet = player.Bet;
        var newBet = currentBet + amount;
        await BetSet(id, newBet);
    }

    [ComponentInteraction("bet_set:*:*", true)]
    public async Task BetSet(string id, ulong amount)
    {
        if (!Context.Interaction.HasResponded) await DeferAsync();

        var gameSession = GameService.GetActiveSession(id);
        if (gameSession == null)
        {
            await FollowupAsync("Game session not found.", ephemeral: true);
            return;
        }

        try
        {
            await GameService.SetBet(gameSession, Context.User.Id, amount);
        }
        catch (InvalidOperationException ex)
        {
            await FollowupAsync(ex.Message, ephemeral: true);
            await LoggingService.LogChannelAndFile($"Failed to set bet: {ex.Message} {ex.StackTrace}", ExtendedLogSeverity.Warning);
            return;
        }

        await GenerateResponse(gameSession);
    }

    [ComponentInteraction("bet_allin:*", true)]
    public async Task BetAllIn(string id)
    {
        await DeferAsync();

        var gameSession = GameService.GetActiveSession(id);
        if (gameSession == null)
        {
            await FollowupAsync("Game session not found.", ephemeral: true);
            return;
        }

        var user = await CasinoService.GetOrCreateCasinoUser(Context.User.Id.ToString());
        if (user.Tokens == 0)
        {
            await FollowupAsync("You do not have enough tokens to bet all in.", ephemeral: true);
            return;
        }

        await BetSet(id, user.Tokens);
    }

    #endregion
    #region Player Actions

    [ComponentInteraction("action:*:*", true)]
    public async Task DoAction(string id, string action)
    {
        await DeferAsync();

        var gameSession = GameService.GetActiveSession(id);
        if (gameSession == null)
        {
            await FollowupAsync("Game session not found.", ephemeral: true);
            return;
        }

        try
        {
            // Parse the action based on the game type
            var actionType = gameSession.ActionType;
            var parsedAction = (Enum)Enum.Parse(actionType, action);

            gameSession.DoPlayerAction(Context.User.Id, parsedAction);
            await GenerateResponse(gameSession);

            await ContinueGame(gameSession);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"An error occurred while performing the action: {ex.Message}", ephemeral: true);
            await LoggingService.LogAction($"Unexpected error in DoAction: {ex.Message} {ex.StackTrace}", ExtendedLogSeverity.Error);
        }
    }

    [ComponentInteraction("show_hand:*", true)]
    public async Task ShowHand(string id)
    {
        try
        {
            await DeferAsync(ephemeral: true);

            var gameSession = GameService.GetActiveSession(id);
            if (gameSession == null)
            {
                await FollowupAsync("Game session not found.", ephemeral: true);
                return;
            }

            var player = gameSession.GetPlayer(Context.User.Id);
            if (player == null)
            {
                await FollowupAsync("You are not in this game.", ephemeral: true);
                return;
            }

            if (!gameSession.GameName.Equals("Poker", StringComparison.OrdinalIgnoreCase))
            {
                await FollowupAsync("This game does not have private hands.", ephemeral: true);
                return;
            }

            var handInfo = gameSession.ShowHand(player);
            await FollowupAsync(handInfo, ephemeral: true);
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Failed to show hand: {ex.Message} {ex.StackTrace}", ExtendedLogSeverity.Warning);
            await FollowupAsync($"Failed to show hand: {ex.Message}", ephemeral: true);
        }
    }

    #endregion

    [ComponentInteraction("reload:*", true)]
    public async Task Reload(string id)
    {
        await DeferAsync();

        var gameSession = GameService.GetActiveSession(id);
        if (gameSession == null)
        {
            await FollowupAsync("Game session not found.", ephemeral: true);
            return;
        }

        await GenerateResponse(gameSession);
    }

    #region Misc Methods

    private string CapitalizeWords(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return string.Join(" ", input.Split(' ').Select(word =>
            string.IsNullOrEmpty(word) ? word : char.ToUpper(word[0]) + word.Substring(1).ToLower()));
    }

    [SlashCommand("statistics", "View game statistics showing wins vs losses")]
    public async Task GameStatistics()
    {
        if (!await CheckChannelPermissions()) return;

        try
        {
            await Context.Interaction.DeferAsync(ephemeral: true);

            var statistics = await CasinoService.GetGameStatistics(Context.User);

            if (statistics.Count == 0)
            {
                await Context.Interaction.FollowupAsync("📊 No game statistics available yet. Play some games to generate statistics!");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("🎮 Casino Game Statistics")
                .WithColor(Color.Purple)
                .WithCurrentTimestamp();

            foreach (var game in statistics)
            {
                var gameDisplayName = game.GameName != null ? CapitalizeWords(game.GameName) : "Unknown Game";
                var winRateIcon = game.WinPercentage >= 50 ? "📈" : "📉";
                var profitIcon = game.NetProfit >= 0 ? "💰" : "💸";

                var fieldValue = $"* **Wins:** {game.Wins:N0} | **Losses:** {game.Losses:N0} | ***Total:*** {game.TotalGames:N0}\n" +
                               $"* {winRateIcon} **Win Rate:** {game.WinPercentage:F1}%\n" +
                               $"* {profitIcon} **Net Profit:** {game.NetProfit:+0;-0;0} tokens\n" +
                               $"* **Avg Profit/Game:** {game.AverageProfit:+0.0;-0.0;0.0} tokens";

                embed.AddField($"{gameDisplayName}", fieldValue, false);
            }

            embed.WithFooter($"Total games tracked: {statistics.Sum(s => s.TotalGames):N0}");

            await Context.Interaction.FollowupAsync(embed: embed.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            await LoggingService.LogAction($"Casino: ERROR in GameStatistics command for user {Context.User.Username}: {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogAction($"Casino: GameStatistics Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while loading game statistics. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while loading game statistics. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in GameStatistics command");
            }
        }
    }

    [SlashCommand("leaderboard", "View the leaderboard of best players by game performance")]
    public async Task GameLeaderboard(
            [Summary("game", "Specific game to show leaderboard for (leave empty for global leaderboard)")]
            CasinoGame? game = null)
    {
        if (!await CheckChannelPermissions()) return;

        await Context.Interaction.DeferAsync();

        try
        {
            // Map enum to actual game name stored in database
            var gameName = game?.ToString() switch
            {
                "Blackjack" => "Blackjack",
                "RockPaperScissors" => "Rock Paper Scissors",
                "Poker" => "Poker",
                _ => null
            };

            var leaderboard = await CasinoService.GetGameLeaderboard(gameName, 9);

            if (leaderboard.Count == 0)
            {
                var noDataMessage = game.HasValue
                    ? $"📊 No players found for {gameName} yet."
                    : "📊 No game data found yet. Play some casino games to generate leaderboards!";
                await Context.Interaction.FollowupAsync(noDataMessage);
                return;
            }

            var title = game.HasValue
                ? $"🏆 {gameName} Leaderboard"
                : "🏆 Global Game Leaderboard";

            var embed = new EmbedBuilder()
                .WithTitle(title)
                .WithColor(Color.Gold)
                .WithDescription($"-# Ranked by score (`Win Rate × log₁₀(Total Games + 1)`)")
                .WithCurrentTimestamp();

            for (int i = 0; i < leaderboard.Count; i++)
            {
                var entry = leaderboard[i];
                var user = Context.Guild.GetUser(ulong.Parse(entry.UserID));
                var username = user?.DisplayName ?? "Unknown User";

                var medal = i switch
                {
                    0 => "🥇",
                    1 => "🥈",
                    2 => "🥉",
                    _ => $"{i + 1}."
                };

                var profitIcon = entry.NetProfit >= 0 ? "💰" : "💸";
                var fieldValue = $"* **Score:** {entry.Score:F2}\n" +
                               $"* **Games:** {entry.TotalGames:N0} | **W/L:** {entry.Wins}/{entry.Losses}\n" +
                               $"* **Win Rate:** {entry.WinPercentage:F1}%\n" +
                               $"* {profitIcon} **Profit:** {entry.NetProfit:+#,0;-#,0;0}";

                embed.AddField($"{medal} {username}", fieldValue, true);
            }

            var footerText = game.HasValue
                ? $"Showing top {leaderboard.Count} {gameName} players"
                : $"Showing top {leaderboard.Count} players across all games";
            embed.WithFooter(footerText);

            await Context.Interaction.FollowupAsync(embed: embed.Build());
        }
        catch (Exception ex)
        {
            await LoggingService.LogAction($"Casino: ERROR in GameLeaderboard command for user {Context.User.Username}: {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogAction($"Casino: GameLeaderboard Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while loading the game leaderboard. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while loading the game leaderboard. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in GameLeaderboard command");
            }
        }
    }

    #endregion
}
