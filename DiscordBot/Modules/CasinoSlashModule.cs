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
                var username = user?.DisplayName ?? "Unknown User";
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
        public async Task TokenHistory(
            [Summary("user", "User to view history for (Admin only)")] SocketGuildUser targetUser = null)
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync(ephemeral: true);

            // Check if requesting another user's history
            var userId = Context.User.Id.ToString();
            var isAdminRequest = targetUser != null && targetUser.Id != Context.User.Id;

            if (isAdminRequest)
            {
                var guildUser = Context.User as SocketGuildUser;
                if (guildUser == null || !guildUser.GuildPermissions.Administrator)
                {
                    await Context.Interaction.FollowupAsync("🚫 Only administrators can view other users' transaction history.", ephemeral: true);
                    return;
                }
                userId = targetUser.Id.ToString();
            }

            const int transactionsPerPage = 5;

            // Get total transaction count first
            var allTransactions = await CasinoService.GetUserTransactionHistory(userId, int.MaxValue);
            var totalTransactions = allTransactions.Count;

            if (totalTransactions == 0)
            {
                var noHistoryText = isAdminRequest ?
                    $"📜 No transaction history found for {targetUser.DisplayName}." :
                    "📜 No transaction history found.";
                await Context.Interaction.FollowupAsync(noHistoryText, ephemeral: true);
                return;
            }

            var totalPages = (int)Math.Ceiling(totalTransactions / (double)transactionsPerPage);

            var transactions = allTransactions.Take(transactionsPerPage).ToList();

            var embed = new EmbedBuilder()
                .WithTitle($"📜 {(isAdminRequest ? $"{targetUser.DisplayName}'s " : "Your ")}Transaction History")
                .WithColor(Color.Blue)
                .WithFooter($"Page {1}/{totalPages} • {totalTransactions} total transactions");

            foreach (var transaction in transactions)
            {
                var amountText = transaction.Amount >= 0 ? $"+{transaction.Amount}" : transaction.Amount.ToString();
                var emoji = transaction.Amount >= 0 ? "📈" : "📉";
                var toto = new TimestampTag(transaction.CreatedAt);
                embed.AddField($"{emoji} {transaction.TransactionType}",
                    $"{amountText} tokens - {transaction.Description}\n*{toto}*",
                    false);
            }

            var components = CreateHistoryNavigationComponents(userId, 1, totalPages, isAdminRequest);

            await Context.Interaction.FollowupAsync(embed: embed.Build(), components: components, ephemeral: true);
        }

        private MessageComponent CreateHistoryNavigationComponents(string userId, int currentPage, int totalPages, bool isAdminRequest)
        {
            var builder = new ComponentBuilder();

            if (totalPages <= 1)
                return builder.Build(); // No navigation needed for single page

            // Previous button
            builder.WithButton("◀️ Previous", $"history_nav:{userId}:{currentPage - 1}:{(isAdminRequest ? "admin" : "self")}", ButtonStyle.Secondary, disabled: currentPage <= 1);

            // Page info button (disabled, just for display)
            builder.WithButton($"Page {currentPage}/{totalPages}", "page_info", ButtonStyle.Primary, disabled: true);

            // Next button  
            builder.WithButton("Next ▶️", $"history_nav:{userId}:{currentPage + 1}:{(isAdminRequest ? "admin" : "self")}", ButtonStyle.Secondary, disabled: currentPage >= totalPages);

            return builder.Build();
        }

        [ComponentInteraction("history_nav:*:*:*", true)]
        public async Task NavigateHistory(string userId, string pageStr, string requestType)
        {
            try
            {
                await Context.Interaction.DeferAsync(ephemeral: true);

                var page = int.Parse(pageStr);
                var isAdminRequest = requestType == "admin";

                // Verify permissions
                if (isAdminRequest)
                {
                    var guildUser = Context.User as SocketGuildUser;
                    if (guildUser == null || !guildUser.GuildPermissions.Administrator)
                    {
                        await Context.Interaction.FollowupAsync("🚫 Only administrators can view other users' transaction history.", ephemeral: true);
                        return;
                    }
                }
                else if (Context.User.Id.ToString() != userId)
                {
                    await Context.Interaction.FollowupAsync("🚫 You can only view your own transaction history.", ephemeral: true);
                    return;
                }

                const int transactionsPerPage = 5;

                // Get total transaction count
                var allTransactions = await CasinoService.GetUserTransactionHistory(userId, int.MaxValue);
                var totalTransactions = allTransactions.Count;
                var totalPages = (int)Math.Ceiling(totalTransactions / (double)transactionsPerPage);

                page = Math.Max(1, Math.Min(page, totalPages)); // Clamp page to valid range

                var transactions = allTransactions.Skip((page - 1) * transactionsPerPage).Take(transactionsPerPage).ToList();

                // Get user info for display
                var targetUser = Context.Guild.GetUser(ulong.Parse(userId));
                var displayName = targetUser?.DisplayName ?? "Unknown User";

                var embed = new EmbedBuilder()
                    .WithTitle($"📜 {(isAdminRequest ? $"{displayName}'s " : "Your ")}Transaction History")
                    .WithColor(Color.Blue)
                    .WithFooter($"Page {page}/{totalPages} • {totalTransactions} total transactions");

                foreach (var transaction in transactions)
                {
                    var amountText = transaction.Amount >= 0 ? $"+{transaction.Amount}" : transaction.Amount.ToString();
                    var emoji = transaction.Amount >= 0 ? "📈" : "📉";
                    var toto = new TimestampTag(transaction.CreatedAt);
                    embed.AddField($"{emoji} {transaction.TransactionType}",
                        $"{amountText} tokens - {transaction.Description}\n*{toto} UTC*",
                        false);
                }

                var components = CreateHistoryNavigationComponents(userId, page, totalPages, isAdminRequest);

                await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Embed = embed.Build();
                    msg.Components = components;
                });

                await LoggingService.LogChannelAndFile($"Casino: NavigateHistory completed for {Context.User.Username} - Page: {page}, UserId: {userId}, IsAdmin: {isAdminRequest}");
            }
            catch (Exception ex)
            {
                await LoggingService.LogChannelAndFile($"Casino: ERROR in NavigateHistory for user {Context.User.Username}: {ex.Message}", ExtendedLogSeverity.Error);

                try
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while navigating transaction history. Please try again.", ephemeral: true);
                }
                catch
                {
                    await LoggingService.LogChannelAndFile($"Casino: Failed to send error response in NavigateHistory");
                }
            }
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
            .WithButton("❌ Cancel", $"casino_reset_cancel:{Context.User.Id}", ButtonStyle.Secondary)
            .WithButton("⚠️ CONFIRM RESET", $"casino_reset_confirm:{Context.User.Id}", ButtonStyle.Danger)
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

    [ComponentInteraction("casino_reset_confirm:*", true)]
    public async Task ConfirmReset(string userId)
    {
        try
        {
            await Context.Interaction.DeferAsync(ephemeral: true);

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

    [ComponentInteraction("casino_reset_cancel:*", true)]
    public async Task CancelReset(string userId)
    {
        try
        {
            await Context.Interaction.DeferAsync(ephemeral: true);
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
                await Context.Interaction.RespondAsync("🃏 You already have an active game. Finish it before starting a new one.", ephemeral: true);
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
            .WithTitle("🃏 Blackjack - Place Your Bet")
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

    #region Betting Component Interactions

    [ComponentInteraction("bet_add:*:*:*", true)]
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
                .WithTitle("🃏 Blackjack - Place Your Bet")
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

    [ComponentInteraction("bet_allin:*:*", true)]
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
                .WithTitle("🃏 Blackjack - Place Your Bet")
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

    [ComponentInteraction("cancel_bet:*", true)]
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
                .WithTitle("🃏 Game Cancelled")
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

    // Defines the modal that will be sent.
    public class CustomBetModal : IModal
    {
        public string Title => "Set Custom Bet Amount";
        // Strings with the ModalTextInput attribute will automatically become components.
        [InputLabel("Bet Amount")]
        [ModalTextInput("bet_amount", placeholder: "Enter amount", maxLength: 20)]
        public string BetAmount { get; set; }
    }

    [ComponentInteraction("bet_custom:*:*", true)]
    public async Task ShowCustomBetModal(string userId, string currentBetStr)
    {
        try
        {
            await LoggingService.LogChannelAndFile($"Casino: ShowCustomBetModal called by {Context.User.Username} (ID: {Context.User.Id}) - UserId: {userId}, CurrentBet: {currentBetStr}");

            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.RespondAsync("🚫 This is not your game.", ephemeral: true);
                return;
            }

            var user = await CasinoService.GetOrCreateCasinoUser(Context.User.Id.ToString());

            await Context.Interaction.RespondWithModalAsync<CustomBetModal>($"custom_bet_modal");

            await LoggingService.LogChannelAndFile($"Casino: ShowCustomBetModal completed for {Context.User.Username}");
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

            var embed = new EmbedBuilder()
                .WithTitle("🃏 Blackjack - Place Your Bet")
                .WithDescription($"**Available Tokens:** {user.Tokens:N0}\n" +
                               $"**Current Bet:** {customBet:N0} tokens\n\n" +
                               "Use the buttons to adjust your bet, then start the game!")
                .WithColor(Color.Blue)
                .WithFooter("Game will timeout after 5 minutes of inactivity")
                .Build();

            await Context.Interaction.DeferAsync();
            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = CreateBettingComponents(customBet);
            });

            await LoggingService.LogChannelAndFile($"Casino: HandleCustomBetModal completed successfully for {Context.User.Username} - Custom bet: {customBet}");
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

    [ComponentInteraction("bj_hit_*", true)]
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

    [ComponentInteraction("bj_stand_*", true)]
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

    [ComponentInteraction("bj_double_*", true)]
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