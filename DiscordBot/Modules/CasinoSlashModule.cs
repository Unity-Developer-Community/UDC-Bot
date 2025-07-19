using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Services;
using DiscordBot.Settings;

namespace DiscordBot.Modules;

[Group("casino", "Casino games and token management")]
public class CasinoSlashModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Dependency Injection
    
    public CasinoService CasinoService { get; set; }
    public ILoggingService LoggingService { get; set; }
    public BotSettings BotSettings { get; set; }
    
    #endregion

    #region Channel Permission Check
    
    private async Task<bool> CheckChannelPermissions()
    {
        if (!CasinoService.IsChannelAllowed(Context.Channel.Id))
        {
            await Context.Interaction.RespondAsync(
                "🚫 Casino commands are not allowed in this channel.", 
                ephemeral: true);
            return false;
        }
        return true;
    }

    #endregion

    #region Token Commands

    [Group("tokens", "Token management commands")]
    public class TokenCommands : InteractionModuleBase<SocketInteractionContext>
    {
        public CasinoService CasinoService { get; set; }
        public ILoggingService LoggingService { get; set; }
        public BotSettings BotSettings { get; set; }

        private async Task<bool> CheckChannelPermissions()
        {
            if (!CasinoService.IsChannelAllowed(Context.Channel.Id))
            {
                await Context.Interaction.RespondAsync(
                    "🚫 Casino commands are not allowed in this channel.", 
                    ephemeral: true);
                return false;
            }
            return true;
        }

        [SlashCommand("balance", "Check your token balance")]
        public async Task CheckTokens()
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync(ephemeral: true);

            var user = await CasinoService.GetOrCreateCasinoUser(Context.User.Id.ToString());
            
            var embed = new EmbedBuilder()
                .WithTitle("🪙 Your Token Balance")
                .WithDescription($"You have **{user.Tokens:N0}** tokens")
                .WithColor(Color.Gold)
                .WithFooter("Use '/casino tokens gift' to send tokens to other users")
                .Build();

            await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
        }

        [SlashCommand("gift", "Gift tokens to another user")]
        public async Task GiftTokens(
            [Summary("user", "User to gift tokens to")] SocketGuildUser targetUser,
            [Summary("amount", "Amount of tokens to gift")] uint amount)
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync(ephemeral: true);

            if (targetUser.IsBot)
            {
                await Context.Interaction.FollowupAsync("🤖 You cannot gift tokens to bots.", ephemeral: true);
                return;
            }

            if (targetUser.Id == Context.User.Id)
            {
                await Context.Interaction.FollowupAsync("🚫 You cannot gift tokens to yourself.", ephemeral: true);
                return;
            }

            if (amount == 0)
            {
                await Context.Interaction.FollowupAsync("🚫 You must gift at least 1 token.", ephemeral: true);
                return;
            }

            var success = await CasinoService.TransferTokens(Context.User.Id.ToString(), targetUser.Id.ToString(), amount);

            if (success)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("🎁 Tokens Gifted Successfully")
                    .WithDescription($"You gifted **{amount:N0}** tokens to {targetUser.Mention}")
                    .WithColor(Color.Green)
                    .Build();

                await Context.Interaction.FollowupAsync(embed: embed);

                await LoggingService.LogChannelAndFile($"Token Gift: {Context.User.Username} gifted {amount} tokens to {targetUser.Username}");
            }
            else
            {
                await Context.Interaction.FollowupAsync("💸 Insufficient tokens for this gift.", ephemeral: true);
            }
        }

        [SlashCommand("leaderboard", "View the top token holders")]
        public async Task TokenLeaderboard()
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync();

            var topUsers = await CasinoService.GetLeaderboard(10);
            
            if (topUsers.Count == 0)
            {
                await Context.Interaction.FollowupAsync("📊 No users found in the casino system yet.");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("🏆 Casino Token Leaderboard")
                .WithColor(Color.Gold);

            for (int i = 0; i < topUsers.Count; i++)
            {
                var user = Context.Guild.GetUser(ulong.Parse(topUsers[i].UserID));
                var username = user?.Username ?? "Unknown User";
                var medal = i switch
                {
                    0 => "🥇",
                    1 => "🥈", 
                    2 => "🥉",
                    _ => $"{i + 1}."
                };

                embed.AddField($"{medal} {username}", $"{topUsers[i].Tokens:N0} tokens", true);
            }

            await Context.Interaction.FollowupAsync(embed: embed.Build());
        }

        [SlashCommand("history", "View your recent token transactions")]
        public async Task TokenHistory()
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync(ephemeral: true);

            var transactions = await CasinoService.GetUserTransactionHistory(Context.User.Id.ToString(), 10);
            
            if (transactions.Count == 0)
            {
                await Context.Interaction.FollowupAsync("📜 No transaction history found.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("📜 Your Transaction History")
                .WithColor(Color.Blue);

            foreach (var transaction in transactions)
            {
                var amountText = transaction.Amount >= 0 ? $"+{transaction.Amount}" : transaction.Amount.ToString();
                var emoji = transaction.Amount >= 0 ? "📈" : "📉";
                embed.AddField($"{emoji} {transaction.TransactionType}", 
                    $"{amountText} tokens - {transaction.Description}\n*{transaction.CreatedAt:MMM dd, yyyy HH:mm} UTC*", 
                    false);
            }

            await Context.Interaction.FollowupAsync(embed: embed.Build(), ephemeral: true);
        }

        #region Admin Commands

        [SlashCommand("set", "Set a user's token balance (Admin only)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetTokens(
            [Summary("user", "User to set tokens for")] SocketGuildUser targetUser,
            [Summary("amount", "New token amount")] uint amount)
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync(ephemeral: true);

            await CasinoService.SetUserTokens(targetUser.Id.ToString(), amount, Context.User.Id.ToString());

            var embed = new EmbedBuilder()
                .WithTitle("⚙️ Admin: Tokens Set")
                .WithDescription($"Set {targetUser.Mention}'s tokens to **{amount:N0}**")
                .WithColor(Color.Purple)
                .Build();

            await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
            await LoggingService.LogChannelAndFile($"Admin Token Set: {Context.User.Username} set {targetUser.Username}'s tokens to {amount}");
        }

        [SlashCommand("add", "Add tokens to a user's balance (Admin only)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task AddTokens(
            [Summary("user", "User to add tokens to")] SocketGuildUser targetUser,
            [Summary("amount", "Amount of tokens to add")] uint amount)
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync(ephemeral: true);

            await CasinoService.UpdateUserTokens(targetUser.Id.ToString(), (long)amount, "admin_add", 
                $"Admin added {amount} tokens ({Context.User.Username})");

            var embed = new EmbedBuilder()
                .WithTitle("⚙️ Admin: Tokens Added")
                .WithDescription($"Added **{amount:N0}** tokens to {targetUser.Mention}")
                .WithColor(Color.Purple)
                .Build();

            await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
            await LoggingService.LogChannelAndFile($"Admin Token Add: {Context.User.Username} added {amount} tokens to {targetUser.Username}");
        }

        #endregion
    }

    #endregion

    #region Admin Reset Command

    [SlashCommand("reset", "Reset all casino data (Admin only) - REQUIRES CONFIRMATION")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ResetCasino()
    {
        if (!await CheckChannelPermissions()) return;

        var embed = new EmbedBuilder()
            .WithTitle("⚠️ Casino Reset Confirmation")
            .WithDescription("**WARNING:** This will permanently delete:\n" +
                           "• All user token balances\n" +
                           "• All transaction history\n" +
                           "• All active games\n\n" +
                           "This action **CANNOT** be undone!\n\n" +
                           "This confirmation will expire in 30 seconds.")
            .WithColor(Color.Red)
            .Build();

        var components = new ComponentBuilder()
            .WithButton("❌ Cancel", $"casino_reset_cancel_{Context.User.Id}", ButtonStyle.Secondary)
            .WithButton("⚠️ CONFIRM RESET", $"casino_reset_confirm_{Context.User.Id}", ButtonStyle.Danger)
            .Build();

        await Context.Interaction.RespondAsync(embed: embed, components: components, ephemeral: true);

        // Auto-expire after 30 seconds
        _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(async _ =>
        {
            try
            {
                var expiredEmbed = new EmbedBuilder()
                    .WithTitle("⏰ Reset Confirmation Expired")
                    .WithDescription("The reset confirmation has expired. No changes were made.")
                    .WithColor(Color.Orange)
                    .Build();

                await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Embed = expiredEmbed;
                    msg.Components = new ComponentBuilder().Build();
                });
            }
            catch
            {
                // Ignore if already modified
            }
        });
    }

    [ComponentInteraction("casino_reset_confirm_*")]
    public async Task ConfirmReset(string userId)
    {
        try
        {
            await LoggingService.LogChannelAndFile($"Casino: ConfirmReset called by {Context.User.Username} (ID: {Context.User.Id}) - ExpectedUserId: {userId}");

            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.RespondAsync("🚫 You are not authorized to confirm this action.", ephemeral: true);
                return;
            }

            await Context.Interaction.DeferAsync(ephemeral: true);

            await CasinoService.ResetAllCasinoData();

            var embed = new EmbedBuilder()
                .WithTitle("🔄 Casino Reset Complete")
                .WithDescription("All casino data has been permanently deleted.")
                .WithColor(Color.Green)
                .Build();

            await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
            await LoggingService.LogChannelAndFile($"Casino: ConfirmReset completed successfully by admin {Context.User.Username}");
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in ConfirmReset for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: ConfirmReset Exception Details: {ex}");
            
            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while resetting casino data. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while resetting casino data. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in ConfirmReset");
            }
        }
    }

    [ComponentInteraction("casino_reset_cancel_*")]
    public async Task CancelReset(string userId)
    {
        try
        {
            await LoggingService.LogChannelAndFile($"Casino: CancelReset called by {Context.User.Username} (ID: {Context.User.Id}) - ExpectedUserId: {userId}");

            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.RespondAsync("🚫 You are not authorized to cancel this action.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("❌ Reset Cancelled")
                .WithDescription("Casino reset has been cancelled. No changes were made.")
                .WithColor(Color.LightGrey)
                .Build();

            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = new ComponentBuilder().Build();
            });

            await LoggingService.LogChannelAndFile($"Casino: CancelReset completed by admin {Context.User.Username}");
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in CancelReset for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: CancelReset Exception Details: {ex}");
            
            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while cancelling the reset. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while cancelling the reset. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in CancelReset");
            }
        }
    }

    #endregion

    #region Games

    [SlashCommand("blackjack", "Play a game of blackjack")]
    public async Task PlayBlackjack()
    {
        try
        {
            await LoggingService.LogChannelAndFile($"Casino: PlayBlackjack called by {Context.User.Username} (ID: {Context.User.Id}) in channel {Context.Channel.Name}");

            if (!await CheckChannelPermissions()) return;

            if (CasinoService.HasActiveGame(Context.User.Id))
            {
                await Context.Interaction.RespondAsync("🎰 You already have an active game. Finish it before starting a new one.", ephemeral: true);
                return;
            }

            var user = await CasinoService.GetOrCreateCasinoUser(Context.User.Id.ToString());
            
            if (user.Tokens == 0)
            {
                await Context.Interaction.RespondAsync("💸 You don't have any tokens to bet with.", ephemeral: true);
                return;
            }

            await Context.Interaction.RespondAsync(embed: CreateBettingEmbed(user.Tokens), 
                components: CreateBettingComponents(1));

            await LoggingService.LogChannelAndFile($"Casino: PlayBlackjack setup completed for {Context.User.Username} - Available tokens: {user.Tokens}");
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

    private Embed CreateBettingEmbed(ulong maxTokens)
    {
        return new EmbedBuilder()
            .WithTitle("🎰 Blackjack - Place Your Bet")
            .WithDescription($"**Available Tokens:** {maxTokens:N0}\n" +
                           $"**Current Bet:** 1 token\n\n" +
                           "Use the buttons to adjust your bet, then start the game!")
            .WithColor(Color.Blue)
            .WithFooter("Game will timeout after 5 minutes of inactivity")
            .Build();
    }

    private MessageComponent CreateBettingComponents(ulong currentBet)
    {
        return new ComponentBuilder()
            .WithButton("+1", $"bet_add_1_{Context.User.Id}_{currentBet}", ButtonStyle.Secondary, new Emoji("1️⃣"))
            .WithButton("+10", $"bet_add_10_{Context.User.Id}_{currentBet}", ButtonStyle.Secondary, new Emoji("🔟"))
            .WithButton("+100", $"bet_add_100_{Context.User.Id}_{currentBet}", ButtonStyle.Secondary, new Emoji("💯"))
            .WithButton("All In", $"bet_allin_{Context.User.Id}_{currentBet}", ButtonStyle.Primary, new Emoji("💰"))
            .WithButton("Start Game", $"start_blackjack_{Context.User.Id}_{currentBet}", ButtonStyle.Success, new Emoji("🎮"), row: 1)
            .WithButton("Cancel", $"cancel_bet_{Context.User.Id}", ButtonStyle.Danger, new Emoji("❌"), row: 1)
            .Build();
    }

    #endregion

    #region Betting Component Interactions

    [ComponentInteraction("bet_add_*")]
    public async Task AdjustBet(string amount, string userId, string currentBetStr)
    {
        try
        {
            await LoggingService.LogChannelAndFile($"Casino: AdjustBet called by {Context.User.Username} (ID: {Context.User.Id}) - Amount: {amount}, UserId: {userId}, CurrentBet: {currentBetStr}");

            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            var user = await CasinoService.GetOrCreateCasinoUser(Context.User.Id.ToString());
            ulong currentBet = ulong.Parse(currentBetStr);
            ulong adjustment = ulong.Parse(amount);
            ulong newBet = Math.Min(currentBet + adjustment, user.Tokens);

            var embed = new EmbedBuilder()
                .WithTitle("🎰 Blackjack - Place Your Bet")
                .WithDescription($"**Available Tokens:** {user.Tokens:N0}\n" +
                               $"**Current Bet:** {newBet:N0} tokens\n\n" +
                               "Use the buttons to adjust your bet, then start the game!")
                .WithColor(Color.Blue)
                .WithFooter("Game will timeout after 5 minutes of inactivity")
                .Build();

            await ((SocketMessageComponent)Context.Interaction).UpdateAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = CreateBettingComponents(newBet);
            });

            await LoggingService.LogChannelAndFile($"Casino: AdjustBet completed successfully for {Context.User.Username} - New bet: {newBet}");
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
                // If we can't respond, just log it
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in AdjustBet");
            }
        }
    }

    [ComponentInteraction("bet_allin_*")]
    public async Task AllInBet(string userId, string currentBetStr)
    {
        try
        {
            await LoggingService.LogChannelAndFile($"Casino: AllInBet called by {Context.User.Username} (ID: {Context.User.Id}) - UserId: {userId}, CurrentBet: {currentBetStr}");

            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            var user = await CasinoService.GetOrCreateCasinoUser(Context.User.Id.ToString());

            var embed = new EmbedBuilder()
                .WithTitle("🎰 Blackjack - Place Your Bet")
                .WithDescription($"**Available Tokens:** {user.Tokens:N0}\n" +
                               $"**Current Bet:** {user.Tokens:N0} tokens (ALL IN!)\n\n" +
                               "Use the buttons to adjust your bet, then start the game!")
                .WithColor(Color.Orange)
                .WithFooter("Game will timeout after 5 minutes of inactivity")
                .Build();

            await ((SocketMessageComponent)Context.Interaction).UpdateAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = CreateBettingComponents(user.Tokens);
            });

            await LoggingService.LogChannelAndFile($"Casino: AllInBet completed successfully for {Context.User.Username} - All-in bet: {user.Tokens}");
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

    [ComponentInteraction("cancel_bet_*")]
    public async Task CancelBetting(string userId)
    {
        try
        {
            await LoggingService.LogChannelAndFile($"Casino: CancelBetting called by {Context.User.Username} (ID: {Context.User.Id}) - UserId: {userId}");

            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("🎰 Game Cancelled")
                .WithDescription("You cancelled the blackjack game.")
                .WithColor(Color.LightGrey)
                .Build();

            await ((SocketMessageComponent)Context.Interaction).UpdateAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = new ComponentBuilder().Build();
            });

            await LoggingService.LogChannelAndFile($"Casino: CancelBetting completed successfully for {Context.User.Username}");
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

    [ComponentInteraction("start_blackjack_*")]
    public async Task StartBlackjackGame(string userId, string betStr)
    {
        try
        {
            await LoggingService.LogChannelAndFile($"Casino: StartBlackjackGame called by {Context.User.Username} (ID: {Context.User.Id}) - UserId: {userId}, Bet: {betStr}");

            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            await Context.Interaction.DeferAsync();

            ulong bet = ulong.Parse(betStr);
            
            var activeGame = await CasinoService.StartBlackjackGame(Context.User.Id, bet, 
                await Context.Interaction.GetOriginalResponseAsync());
            
            await Context.Interaction.FollowupAsync(embed: CreateGameEmbed(activeGame), 
                components: CreateGameComponents(activeGame));

            await LoggingService.LogChannelAndFile($"Casino: StartBlackjackGame completed successfully for {Context.User.Username} - Bet: {bet}");
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

    private Embed CreateGameEmbed(ActiveGame game)
    {
        var blackjack = game.BlackjackGame;
        var playerCards = string.Join(" ", blackjack.PlayerCards.Select(c => c.GetDisplayName()));
        var dealerCards = blackjack.DealerCards.Count > 0 ? blackjack.DealerCards[0].GetDisplayName() + " ?" : "";

        var description = $"**Your Bet:** {game.Bet:N0} tokens\n\n";
        description += $"**Your Cards:** {playerCards} (Value: {blackjack.GetPlayerValue()})\n";
        description += $"**Dealer Cards:** {dealerCards}\n\n";

        if (blackjack.IsPlayerBlackjack())
        {
            description += "🎉 **BLACKJACK!** You got 21!\n";
        }
        else if (blackjack.IsPlayerBusted())
        {
            description += "💥 **BUSTED!** You went over 21.\n";
        }

        return new EmbedBuilder()
            .WithTitle("🃏 Blackjack Game")
            .WithDescription(description)
            .WithColor(blackjack.IsPlayerBusted() ? Color.Red : Color.Blue)
            .WithFooter($"Game expires in {(game.ExpiryTime - DateTime.UtcNow).Minutes + 1} minutes")
            .Build();
    }

    private MessageComponent CreateGameComponents(ActiveGame game)
    {
        if (game.BlackjackGame.IsPlayerBusted() || game.BlackjackGame.IsPlayerBlackjack() || !game.BlackjackGame.PlayerTurn)
        {
            return new ComponentBuilder().Build(); // No buttons if game is over or not player's turn
        }

        var builder = new ComponentBuilder()
            .WithButton("Hit", $"bj_hit_{game.UserId}", ButtonStyle.Primary, new Emoji("👊"))
            .WithButton("Stand", $"bj_stand_{game.UserId}", ButtonStyle.Secondary, new Emoji("✋"));

        if (game.BlackjackGame.PlayerCards.Count == 2 && !game.BlackjackGame.DoubleDown)
        {
            builder.WithButton("Double Down", $"bj_double_{game.UserId}", ButtonStyle.Success, new Emoji("⬆️"));
        }

        return builder.Build();
    }

    #endregion

    #region Game Action Interactions

    [ComponentInteraction("bj_hit_*")]
    public async Task BlackjackHit(string userIdStr)
    {
        try
        {
            await LoggingService.LogChannelAndFile($"Casino: BlackjackHit called by {Context.User.Username} (ID: {Context.User.Id}) - GameUserId: {userIdStr}");

            ulong userId = ulong.Parse(userIdStr);
            if (Context.User.Id != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            var game = CasinoService.GetActiveGame(userId);
            if (game == null)
            {
                await Context.Interaction.RespondAsync("❌ No active game found.", ephemeral: true);
                await LoggingService.LogChannelAndFile($"Casino: BlackjackHit - No active game found for user {Context.User.Username}");
                return;
            }

            // Draw a card for the player
            game.BlackjackGame.PlayerCards.Add(game.BlackjackGame.Deck.DrawCard());

            if (game.BlackjackGame.IsPlayerBusted())
            {
                // Player busted, end game
                var (result, payout) = await CasinoService.CompleteBlackjackGame(userId, BlackjackGameState.PlayerBusted);
                await ((SocketMessageComponent)Context.Interaction).UpdateAsync(msg =>
                {
                    msg.Embed = CreateEndGameEmbed(game, result, payout);
                    msg.Components = new ComponentBuilder().Build();
                });

                await LoggingService.LogChannelAndFile($"Casino: BlackjackHit - Player {Context.User.Username} busted. Payout: {payout}");
            }
            else
            {
                await ((SocketMessageComponent)Context.Interaction).UpdateAsync(msg =>
                {
                    msg.Embed = CreateGameEmbed(game);
                    msg.Components = CreateGameComponents(game);
                });

                await LoggingService.LogChannelAndFile($"Casino: BlackjackHit completed for {Context.User.Username} - New hand value: {game.BlackjackGame.GetPlayerValue()}");
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

    [ComponentInteraction("bj_stand_*")]
    public async Task BlackjackStand(string userIdStr)
    {
        try
        {
            await LoggingService.LogChannelAndFile($"Casino: BlackjackStand called by {Context.User.Username} (ID: {Context.User.Id}) - GameUserId: {userIdStr}");

            ulong userId = ulong.Parse(userIdStr);
            if (Context.User.Id != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            await Context.Interaction.DeferAsync();

            var game = CasinoService.GetActiveGame(userId);
            if (game == null)
            {
                await Context.Interaction.FollowupAsync("❌ No active game found.", ephemeral: true);
                await LoggingService.LogChannelAndFile($"Casino: BlackjackStand - No active game found for user {Context.User.Username}");
                return;
            }

            game.BlackjackGame.PlayerTurn = false;

            // Dealer's turn - reveal dealer's second card first
            if (game.BlackjackGame.DealerCards.Count == 1)
            {
                game.BlackjackGame.DealerCards.Add(game.BlackjackGame.Deck.DrawCard());
            }

            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Embed = CreateDealerTurnEmbed(game);
                msg.Components = new ComponentBuilder().Build();
            });

            // Add delay for dramatic effect
            await Task.Delay(2000);

            // Dealer draws until 17 or higher
            while (game.BlackjackGame.GetDealerValue() < 17)
            {
                await Task.Delay(2000);
                game.BlackjackGame.DealerCards.Add(game.BlackjackGame.Deck.DrawCard());
                
                await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Embed = CreateDealerTurnEmbed(game);
                });
            }

            await Task.Delay(1000);

            // Determine winner
            var dealerValue = game.BlackjackGame.GetDealerValue();
            var playerValue = game.BlackjackGame.GetPlayerValue();
            
            BlackjackGameState result;
            if (dealerValue > 21)
                result = BlackjackGameState.DealerBusted;
            else if (dealerValue > playerValue)
                result = BlackjackGameState.DealerWins;
            else if (playerValue > dealerValue)
                result = BlackjackGameState.PlayerWins;
            else
                result = BlackjackGameState.Tie;

            var (finalResult, payout) = await CasinoService.CompleteBlackjackGame(userId, result);

            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Embed = CreateEndGameEmbed(game, finalResult, payout);
            });

            await LoggingService.LogChannelAndFile($"Casino: BlackjackStand completed for {Context.User.Username} - Result: {finalResult}, Payout: {payout}");
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

    [ComponentInteraction("bj_double_*")]
    public async Task BlackjackDoubleDown(string userIdStr)
    {
        try
        {
            await LoggingService.LogChannelAndFile($"Casino: BlackjackDoubleDown called by {Context.User.Username} (ID: {Context.User.Id}) - GameUserId: {userIdStr}");

            ulong userId = ulong.Parse(userIdStr);
            if (Context.User.Id != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            var game = CasinoService.GetActiveGame(userId);
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

            // Double the bet and deduct additional tokens
            var originalBet = game.Bet;
            game.Bet *= 2;
            game.BlackjackGame.DoubleDown = true;
            await CasinoService.UpdateUserTokens(userId.ToString(), -(long)originalBet, "blackjack_double", 
                $"Double down additional bet: {originalBet} tokens");

            // Draw exactly one more card
            game.BlackjackGame.PlayerCards.Add(game.BlackjackGame.Deck.DrawCard());

            if (game.BlackjackGame.IsPlayerBusted())
            {
                var (result, payout) = await CasinoService.CompleteBlackjackGame(userId, BlackjackGameState.PlayerBusted);
                await ((SocketMessageComponent)Context.Interaction).UpdateAsync(msg =>
                {
                    msg.Embed = CreateEndGameEmbed(game, result, payout);
                    msg.Components = new ComponentBuilder().Build();
                });

                await LoggingService.LogChannelAndFile($"Casino: BlackjackDoubleDown - Player {Context.User.Username} busted after double down. Payout: {payout}");
            }
            else
            {
                // Automatically stand after double down
                await BlackjackStand(userIdStr);
                await LoggingService.LogChannelAndFile($"Casino: BlackjackDoubleDown completed for {Context.User.Username} - Auto-standing after double down");
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

    private Embed CreateDealerTurnEmbed(ActiveGame game)
    {
        var blackjack = game.BlackjackGame;
        var playerCards = string.Join(" ", blackjack.PlayerCards.Select(c => c.GetDisplayName()));
        var dealerCards = string.Join(" ", blackjack.DealerCards.Select(c => c.GetDisplayName()));

        var description = $"**Your Bet:** {game.Bet:N0} tokens\n\n";
        description += $"**Your Cards:** {playerCards} (Value: {blackjack.GetPlayerValue()})\n";
        description += $"**Dealer Cards:** {dealerCards} (Value: {blackjack.GetDealerValue()})\n\n";
        description += "🤖 **Dealer's turn...**";

        return new EmbedBuilder()
            .WithTitle("🃏 Blackjack Game - Dealer's Turn")
            .WithDescription(description)
            .WithColor(Color.Orange)
            .Build();
    }

    private Embed CreateEndGameEmbed(ActiveGame game, BlackjackGameState result, long payout)
    {
        var blackjack = game.BlackjackGame;
        var playerCards = string.Join(" ", blackjack.PlayerCards.Select(c => c.GetDisplayName()));
        var dealerCards = string.Join(" ", blackjack.DealerCards.Select(c => c.GetDisplayName()));

        var description = $"**Your Bet:** {game.Bet:N0} tokens\n\n";
        description += $"**Your Cards:** {playerCards} (Value: {blackjack.GetPlayerValue()})\n";
        description += $"**Dealer Cards:** {dealerCards} (Value: {blackjack.GetDealerValue()})\n\n";

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
        description += payout > 0 ? $"💰 **Won: {payout:N0} tokens**" : "💸 **No payout**";

        Color embedColor = result switch
        {
            BlackjackGameState.PlayerWins or BlackjackGameState.DealerBusted => Color.Green,
            BlackjackGameState.Tie => Color.Orange,
            _ => Color.Red
        };

        return new EmbedBuilder()
            .WithTitle("🃏 Blackjack Game - Final Result")
            .WithDescription(description)
            .WithColor(embedColor)
            .WithFooter("Thanks for playing! Start a new game anytime.")
            .Build();
    }

    #endregion
}