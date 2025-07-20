using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Services;

namespace DiscordBot.Modules;

public partial class CasinoSlashModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Blackjack Commands

    [SlashCommand("blackjack", "Play a game of blackjack")]
    public async Task PlayBlackjack()
    {
        try
        {
            if (!await CheckChannelPermissions()) return;

            if (BlackjackService.HasActiveGame(Context.User.Id))
            {
                await Context.Interaction.RespondAsync("🃏 You already have an active game. Finish it before starting a new one.", ephemeral: true);
                return;
            }

            var user = await CasinoService.GetOrCreateCasinoUser(Context.User.Id.ToString());

            if (user.Tokens == 0)
            {
                await Context.Interaction.RespondAsync("💸 You don't have any tokens to bet with.", ephemeral: true);
                return;
            }

            await Context.Interaction.RespondAsync(embed: CreateBettingEmbed(user.Tokens, 1),
                components: CreateBettingComponents(1));
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in PlayBlackjack for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: PlayBlackjack Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while setting up your blackjack game. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while setting up your blackjack game. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in PlayBlackjack");
            }
        }
    }

    [SlashCommand("blackjack-rules", "Learn how to play blackjack")]
    public async Task BlackjackRules()
    {
        if (!await CheckChannelPermissions()) return;

        var embed = new EmbedBuilder()
            .WithTitle("🃏 Blackjack Rules")
            .WithColor(Color.Blue)
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
            .WithFooter("Good luck at the tables! 🍀")
            .Build();

        await Context.Interaction.RespondAsync(embed: embed, ephemeral: true);
    }

    #endregion

    #region Blackjack Betting UI

    private Embed CreateBettingEmbed(ulong maxTokens, ulong currentBet)
    {
        var betDescription = currentBet == maxTokens ? $"{currentBet:N0} tokens (ALL IN!)" : $"{currentBet:N0} tokens";
        var color = currentBet == maxTokens ? Color.Orange : Color.Blue;

        return new EmbedBuilder()
            .WithTitle("🃏 Blackjack - Place Your Bet")
            .WithDescription($"**Available Tokens:** {maxTokens:N0}\n" +
                           $"**Current Bet:** {betDescription}\n\n" +
                           "Use the buttons to adjust your bet, then start the game!")
            .WithColor(color)
            .WithFooter("Game will timeout after 5 minutes of inactivity")
            .Build();
    }

    private MessageComponent CreateBettingComponents(ulong currentBet)
    {
        return new ComponentBuilder()
            .WithButton("+1", $"bet_add:1:{Context.User.Id}:{currentBet}", ButtonStyle.Secondary, new Emoji("1️⃣"))
            .WithButton("+10", $"bet_add:10:{Context.User.Id}:{currentBet}", ButtonStyle.Secondary, new Emoji("🔟"))
            .WithButton("+100", $"bet_add:100:{Context.User.Id}:{currentBet}", ButtonStyle.Secondary, new Emoji("💯"))
            .WithButton("Custom", $"bet_custom:{Context.User.Id}:{currentBet}", ButtonStyle.Secondary, new Emoji("✏️"))
            .WithButton("All In", $"bet_allin:{Context.User.Id}:{currentBet}", ButtonStyle.Primary, new Emoji("💰"))
            .WithButton("Start Game", $"start_blackjack:{Context.User.Id}:{currentBet}", ButtonStyle.Success, new Emoji("🎮"), row: 1)
            .WithButton("Cancel", $"cancel_bet:{Context.User.Id}", ButtonStyle.Danger, new Emoji("✖️"), row: 1)
            .Build();
    }

    #endregion

    #region Blackjack Betting Component Interactions

    [ComponentInteraction("bet_add:*:*:*", true)]
    public async Task AdjustBet(string amount, string userId, string currentBetStr)
    {
        try
        {
            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            var user = await CasinoService.GetOrCreateCasinoUser(Context.User.Id.ToString());
            ulong currentBet = ulong.Parse(currentBetStr);
            ulong adjustment = ulong.Parse(amount);
            ulong newBet = Math.Min(currentBet + adjustment, user.Tokens);

            var embed = CreateBettingEmbed(user.Tokens, newBet);

            await ((SocketMessageComponent)Context.Interaction).UpdateAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = CreateBettingComponents(newBet);
            });
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in AdjustBet for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: AdjustBet Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while adjusting your bet. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while adjusting your bet. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in AdjustBet");
            }
        }
    }

    [ComponentInteraction("bet_allin:*:*", true)]
    public async Task AllInBet(string userId, string currentBetStr)
    {
        try
        {
            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            var user = await CasinoService.GetOrCreateCasinoUser(Context.User.Id.ToString());
            var embed = CreateBettingEmbed(user.Tokens, user.Tokens);

            await ((SocketMessageComponent)Context.Interaction).UpdateAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = CreateBettingComponents(user.Tokens);
            });
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in AllInBet for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: AllInBet Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while placing your all-in bet. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while placing your all-in bet. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in AllInBet");
            }
        }
    }

    [ComponentInteraction("cancel_bet:*", true)]
    public async Task CancelBetting(string userId)
    {
        try
        {
            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("🃏 Game Cancelled")
                .WithDescription("You cancelled the blackjack game.")
                .WithColor(Color.LightGrey)
                .Build();

            await ((SocketMessageComponent)Context.Interaction).UpdateAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in CancelBetting for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: CancelBetting Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while cancelling the game. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while cancelling the game. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in CancelBetting");
            }
        }
    }

    [ComponentInteraction("bet_custom:*:*", true)]
    public async Task ShowCustomBetModal(string userId, string currentBetStr)
    {
        try
        {
            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            await Context.Interaction.RespondWithModalAsync<CustomBetModal>($"custom_bet_modal");
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in ShowCustomBetModal for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: ShowCustomBetModal Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while opening the custom bet modal. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while opening the custom bet modal. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in ShowCustomBetModal");
            }
        }
    }

    [ModalInteraction("custom_bet_modal", true)]
    public async Task HandleCustomBetModal(CustomBetModal modal)
    {
        try
        {
            var betAmountStr = modal.BetAmount.Trim();

            if (!ulong.TryParse(betAmountStr, out ulong customBet) || customBet == 0)
            {
                await Context.Interaction.RespondAsync("❌ Please enter a valid number greater than 0.", ephemeral: true);
                return;
            }

            var user = await CasinoService.GetOrCreateCasinoUser(Context.User.Id.ToString());

            if (customBet > user.Tokens)
            {
                await Context.Interaction.RespondAsync($"❌ You only have {user.Tokens:N0} tokens available.", ephemeral: true);
                return;
            }

            var embed = CreateBettingEmbed(user.Tokens, customBet);

            await Context.Interaction.DeferAsync();
            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = CreateBettingComponents(customBet);
            });
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in HandleCustomBetModal for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: HandleCustomBetModal Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while processing your custom bet. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while processing your custom bet. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in HandleCustomBetModal");
            }
        }
    }

    [ComponentInteraction("start_blackjack:*:*", true)]
    public async Task StartBlackjackGame(string userId, string betStr)
    {
        try
        {
            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            await Context.Interaction.DeferAsync();

            ulong bet = ulong.Parse(betStr);

            var activeGame = await BlackjackService.StartBlackjackGame(Context.User.Id, bet,
                await Context.Interaction.GetOriginalResponseAsync());

            // Check for immediate blackjack (21 with first 2 cards)
            if (activeGame.Game.IsPlayerBlackjack())
            {
                var (result, payout) = await BlackjackService.CompleteBlackjackGame(activeGame, BlackjackGameState.PlayerWins);
                await Context.Interaction.FollowupAsync(embed: CreateEndGameEmbed(activeGame, result, payout),
                    components: new ComponentBuilder().Build());
            }
            else
            {
                await Context.Interaction.FollowupAsync(embed: CreateGameEmbed(activeGame),
                    components: await CreateGameComponents(activeGame));
            }
        }
        catch (InvalidOperationException ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: InvalidOperation in StartBlackjackGame for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Warning);

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync($"❌ {ex.Message}", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync($"❌ {ex.Message}", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send InvalidOperation response to user {Context.User.Username} in StartBlackjackGame");
            }
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in StartBlackjackGame for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: StartBlackjackGame Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while starting the blackjack game. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while starting the blackjack game. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in StartBlackjackGame");
            }
        }
    }

    #endregion

    #region Game Component Creation

    private string BuildGameDescription(ActiveGame<BlackjackGame> game, bool showDealerHand = true)
    {
        var blackjack = game.Game;
        var playerCards = string.Join(" ", blackjack.PlayerCards.Select(c => c.GetDisplayName()));

        string dealerCards;
        int dealerValue;
        if (showDealerHand)
        {
            dealerCards = string.Join(" ", blackjack.DealerCards.Select(c => c.GetDisplayName()));
            dealerValue = blackjack.GetDealerValue();
        }
        else
        {
            dealerCards = blackjack.DealerCards.Count > 0 ? blackjack.DealerCards[0].GetDisplayName() + " ?" : "";
            dealerValue = blackjack.DealerCards.Count > 0 ? BlackjackHelper.CalculateHandValue(new List<Card> { blackjack.DealerCards[0] }) : 0;
        }

        var description = $"**Your Bet:** {game.Bet:N0} tokens\n\n";
        description += $"**Your Cards:** {playerCards} (Value: {blackjack.GetPlayerValue()})\n";
        description += $"**Dealer Cards:** {dealerCards} (Value: {dealerValue}{(showDealerHand ? "" : "?")})\n\n";

        return description;
    }

    private Embed CreateGameEmbed(ActiveGame<BlackjackGame> game)
    {
        var blackjack = game.Game;
        var description = BuildGameDescription(game, showDealerHand: false);

        return new EmbedBuilder()
            .WithTitle("🃏 Blackjack - Your Turn")
            .WithDescription(description)
            .WithColor(blackjack.IsPlayerBusted() ? Color.Red : Color.Blue)
            .WithFooter($"Game expires in {(game.ExpiryTime - DateTime.UtcNow).Minutes + 1} minutes")
            .Build();
    }

    private async Task<MessageComponent> CreateGameComponents(ActiveGame<BlackjackGame> game)
    {
        if (game.Game.IsPlayerBusted() || game.Game.IsPlayerBlackjack() || !game.Game.PlayerTurn)
        {
            return new ComponentBuilder().Build(); // No buttons if game is over or not player's turn
        }

        var builder = new ComponentBuilder()
            .WithButton("Hit", $"bj_hit_{game.UserId}", ButtonStyle.Primary, new Emoji("👊"))
            .WithButton("Stand", $"bj_stand_{game.UserId}", ButtonStyle.Secondary, new Emoji("✋"));

        if (!game.Game.DoubleDown)
        {
            // Check if user has enough tokens to double down
            var user = await CasinoService.GetOrCreateCasinoUser(game.UserId.ToString());
            var hasEnoughTokens = user.Tokens >= game.Bet;
            builder.WithButton("Double Down", $"bj_double_{game.UserId}", ButtonStyle.Success, new Emoji("⬆️"), disabled: !hasEnoughTokens);
        }

        return builder.Build();
    }

    #endregion

    #region Game Action Interactions

    [ComponentInteraction("bj_hit_*", true)]
    public async Task BlackjackHit(string userIdStr)
    {
        try
        {
            ulong userId = ulong.Parse(userIdStr);
            if (Context.User.Id != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            var game = BlackjackService.GetActiveGame(userId);
            if (game == null)
            {
                await Context.Interaction.RespondAsync("❌ No active game found.", ephemeral: true);
                await LoggingService.LogChannelAndFile($"Casino: BlackjackHit - No active game found for user {Context.User.Username} (ID: {userId}). HasActiveGame: {BlackjackService.HasActiveGame(userId)}");
                return;
            }

            // Draw a card for the player
            var newCard = game.Game.Deck.DrawCard();
            game.Game.PlayerCards.Add(newCard);

            if (game.Game.IsPlayerBusted())
            {
                // Player busted, end game
                var (result, payout) = await BlackjackService.CompleteBlackjackGame(game, BlackjackGameState.PlayerBusted);
                await ((SocketMessageComponent)Context.Interaction).UpdateAsync(msg =>
                {
                    msg.Embed = CreateEndGameEmbed(game, result, payout);
                    msg.Components = new ComponentBuilder().Build();
                });
            }
            else if (game.Game.GetPlayerValue() == 21)
            {
                // Player hit 21, automatically proceed to dealer's turn (same as standing)
                await BlackjackStand(userIdStr);
            }
            else
            {
                await ((SocketMessageComponent)Context.Interaction).UpdateAsync(async msg =>
                {
                    msg.Embed = CreateGameEmbed(game);
                    msg.Components = await CreateGameComponents(game);
                });
            }
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in BlackjackHit for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: BlackjackHit Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while drawing your card. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while drawing your card. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in BlackjackHit");
            }
        }
    }

    [ComponentInteraction("bj_stand_*", true)]
    public async Task BlackjackStand(string userIdStr)
    {
        try
        {
            ulong userId = ulong.Parse(userIdStr);
            if (Context.User.Id != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            await Context.Interaction.DeferAsync();

            var game = BlackjackService.GetActiveGame(userId);
            if (game == null)
            {
                await Context.Interaction.FollowupAsync("❌ No active game found.", ephemeral: true);
                await LoggingService.LogChannelAndFile($"Casino: BlackjackStand - No active game found for user {Context.User.Username} (ID: {userId}). HasActiveGame: {BlackjackService.HasActiveGame(userId)}");
                return;
            }

            game.Game.PlayerTurn = false;

            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Embed = CreateDealerTurnEmbed(game, showDealerHand: false);
                msg.Components = new ComponentBuilder().Build();
            });

            // Add delay for the reveal of the dealer's second card
            await Task.Delay(1000);

            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Embed = CreateDealerTurnEmbed(game);
                msg.Components = new ComponentBuilder().Build();
            });

            // Dealer draws until 17 or higher, but hits on soft 17
            while (game.Game.GetDealerValue() < 17 || game.Game.IsDealerSoft17())
            {
                // Add delay so the user has the time to see what's happening
                await Task.Delay(1000);
                game.Game.DealerCards.Add(game.Game.Deck.DrawCard());

                await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Embed = CreateDealerTurnEmbed(game);
                });
            }

            await Task.Delay(1000);

            // Determine winner
            var dealerValue = game.Game.GetDealerValue();
            var playerValue = game.Game.GetPlayerValue();

            BlackjackGameState result;
            if (dealerValue > 21)
                result = BlackjackGameState.DealerBusted;
            else if (dealerValue > playerValue)
                result = BlackjackGameState.DealerWins;
            else if (playerValue > dealerValue)
                result = BlackjackGameState.PlayerWins;
            else
                result = BlackjackGameState.Tie;

            var (finalResult, payout) = await BlackjackService.CompleteBlackjackGame(game, result);

            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Embed = CreateEndGameEmbed(game, finalResult, payout);
            });
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in BlackjackStand for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: BlackjackStand Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while processing your stand. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while processing your stand. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in BlackjackStand");
            }
        }
    }

    [ComponentInteraction("bj_double_*", true)]
    public async Task BlackjackDoubleDown(string userIdStr)
    {
        try
        {
            ulong userId = ulong.Parse(userIdStr);
            if (Context.User.Id != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            var game = BlackjackService.GetActiveGame(userId);
            if (game == null)
            {
                await Context.Interaction.RespondAsync("❌ No active game found.", ephemeral: true);
                await LoggingService.LogChannelAndFile($"Casino: BlackjackDoubleDown - No active game found for user {Context.User.Username}");
                return;
            }

            // Check if user has enough tokens to double
            var user = await CasinoService.GetOrCreateCasinoUser(userId.ToString());
            if (user.Tokens < game.Bet)
            {
                await Context.Interaction.RespondAsync("💸 Insufficient tokens to double down.", ephemeral: true);
                return;
            }

            // Double the bet (we'll deduct the full amount only if the player loses)
            game.Bet *= 2;
            game.Game.DoubleDown = true;

            // Draw exactly one more card
            game.Game.PlayerCards.Add(game.Game.Deck.DrawCard());

            if (game.Game.IsPlayerBusted())
            {
                var (result, payout) = await BlackjackService.CompleteBlackjackGame(game, BlackjackGameState.PlayerBusted);
                await ((SocketMessageComponent)Context.Interaction).UpdateAsync(msg =>
                {
                    msg.Embed = CreateEndGameEmbed(game, result, payout);
                    msg.Components = new ComponentBuilder().Build();
                });
            }
            else
            {
                // Automatically stand after double down
                await BlackjackStand(userIdStr);
            }
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in BlackjackDoubleDown for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: BlackjackDoubleDown Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while processing your double down. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while processing your double down. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in BlackjackDoubleDown");
            }
        }
    }

    #endregion

    #region Game Display Helpers

    private Embed CreateDealerTurnEmbed(ActiveGame<BlackjackGame> game, bool showDealerHand = true)
    {
        var description = BuildGameDescription(game, showDealerHand);
        description += "🤖 **Dealer's turn...**";

        return new EmbedBuilder()
            .WithTitle("🃏 Blackjack - Dealer's Turn")
            .WithDescription(description)
            .WithColor(Color.Orange)
            .Build();
    }

    private Embed CreateEndGameEmbed(ActiveGame<BlackjackGame> game, BlackjackGameState result, long payout)
    {
        var blackjack = game.Game;
        var description = BuildGameDescription(game, showDealerHand: true);

        if (blackjack.IsPlayerBlackjack())
        {
            description += "🃏 **BLACKJACK!** ";
        }

        string resultText = result switch
        {
            BlackjackGameState.PlayerWins => "🎉 **YOU WIN!**",
            BlackjackGameState.DealerWins => "😞 **DEALER WINS**",
            BlackjackGameState.Tie => "🤝 **TIE GAME**",
            BlackjackGameState.PlayerBusted => "💥 **YOU BUSTED!**",
            BlackjackGameState.DealerBusted => "🎉 **DEALER BUSTED - YOU WIN!**",
            _ => "🎲 **GAME OVER**"
        };

        description += resultText + "\n";
        description += payout > 0 ? $"💰 **Won: {payout:N0} tokens**" : $"💸 **Lost: {Math.Abs(payout):N0} tokens**";

        Color embedColor = result switch
        {
            BlackjackGameState.PlayerWins or BlackjackGameState.DealerBusted => Color.Green,
            BlackjackGameState.Tie => Color.Orange,
            _ => Color.Red
        };

        return new EmbedBuilder()
            .WithTitle("🃏 Blackjack - Result")
            .WithDescription(description)
            .WithColor(embedColor)
            .WithFooter("Thanks for playing! Start a new game anytime.")
            .Build();
    }

    #endregion

    #region Custom Bet Modal

    // Defines the modal that will be sent.
    public class CustomBetModal : IModal
    {
        public string Title => "Set Custom Bet Amount";
        // Strings with the ModalTextInput attribute will automatically become components.
        [InputLabel("Bet Amount")]
        [ModalTextInput("bet_amount", placeholder: "Enter amount", maxLength: 20)]
        public string BetAmount { get; set; }
    }

    #endregion
}
