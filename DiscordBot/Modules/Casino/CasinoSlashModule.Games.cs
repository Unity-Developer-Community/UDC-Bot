using Discord.Interactions;
using Discord.Net;
using DiscordBot.Domain;
using DiscordBot.Services;

namespace DiscordBot.Modules;

public enum CasinoGame
{
    Blackjack,
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
                await Task.Delay(1000);
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
    int seats)
    {
        try
        {
            await DeferAsync();

            var gameSession = GameService.CreateGameSession(game, seats, Context.Client, Context.User, Context.Guild);
            gameSession.AddPlayer(Context.User.Id, 1); // Add the command user as a player with a bet of 1

            var (embed, components) = await gameSession.GenerateEmbedAndButtons();
            await FollowupAsync(embed: embed, components: components);
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

    #endregion

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
}
