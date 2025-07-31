using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Services;
using DiscordBot.Settings;

namespace DiscordBot.Modules;

[Group("casino", "Casino games and token management")]
public partial class CasinoSlashModule : InteractionModuleBase<SocketInteractionContext>
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
                .WithDescription(
                    $"You have **{user.Tokens:N0}** tokens"
                    + "\n-# * Use `/casino tokens daily` to claim your daily tokens"
                    + "\n-# * Use `/casino tokens gift` to gift tokens to another user"
                )
                .WithColor(Color.Gold)
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
        public async Task TokenHistory()
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync(ephemeral: true);

            await DisplayTransactionHistory(userId: null, page: 1, targetUser: null, isInitialCall: true);
        }

        [SlashCommand("history-admin", "View transaction history for any user or all users (Admin only)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task TokenHistoryAdmin(
            [Summary("user", "User to view history for (optional - if omitted, shows all users)")] SocketGuildUser? targetUser = null)
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync(ephemeral: true);

            await DisplayTransactionHistory(userId: null, page: 1, targetUser: targetUser, isInitialCall: true);
        }

        private async Task DisplayTransactionHistory(string userId = null, int page = 1, SocketGuildUser? targetUser = null, bool isInitialCall = false)
        {
            try
            {
                // Determine the actual user ID to query and request type
                var queryUserId = userId ?? Context.User.Id.ToString();
                var isAdminRequest = targetUser != null && targetUser.Id != Context.User.Id;
                var isAllUsersRequest = targetUser == null && userId == null && (Context.User as SocketGuildUser)?.GuildPermissions.Administrator == true;

                // Validate permissions for admin requests
                if (isAdminRequest || isAllUsersRequest || (userId != null && Context.User.Id.ToString() != userId))
                {
                    var guildUser = Context.User as SocketGuildUser;
                    if (guildUser == null || !guildUser.GuildPermissions.Administrator)
                    {
                        var message = "🚫 Only administrators can view other users' transaction history.";
                        await Context.Interaction.FollowupAsync(message, ephemeral: true);
                        return;
                    }

                    if (isAdminRequest)
                        queryUserId = targetUser.Id.ToString();
                }

                const int transactionsPerPage = 5;

                // Get transactions based on request type
                List<TokenTransaction> allTransactions;
                if (isAllUsersRequest)
                {
                    // Get all recent transactions across all users
                    var recentTransactions = await CasinoService.GetAllRecentTransactions(int.MaxValue);
                    allTransactions = recentTransactions.ToList();
                }
                else
                {
                    // Get transactions for specific user
                    allTransactions = await CasinoService.GetUserTransactionHistory(queryUserId, int.MaxValue);
                }

                var totalTransactions = allTransactions.Count;

                // Should not happen since any user we interact with gets at least the initial tokens as transaction
                if (totalTransactions == 0)
                {
                    var noHistoryText = isAllUsersRequest ?
                        "📜 No transaction history found in the casino system." :
                        isAdminRequest ?
                        $"📜 No transaction history found for {targetUser?.DisplayName}." :
                        "📜 No transaction history found.";

                    if (isInitialCall)
                        await Context.Interaction.FollowupAsync(noHistoryText, ephemeral: true);
                    else
                        await Context.Interaction.FollowupAsync(noHistoryText, ephemeral: true);
                    return;
                }

                var totalPages = (int)Math.Ceiling(totalTransactions / (double)transactionsPerPage);
                page = Math.Max(1, Math.Min(page, totalPages)); // Clamp page to valid range

                var transactions = allTransactions.Skip((page - 1) * transactionsPerPage).Take(transactionsPerPage).ToList();

                // Get display info based on request type
                string title, description;
                if (isAllUsersRequest)
                {
                    title = "📜 All Users Transaction History";
                    description = "Recent transactions across all casino users";
                }
                else
                {
                    var displayUser = targetUser ?? Context.Guild.GetUser(ulong.Parse(queryUserId));
                    var displayName = displayUser?.DisplayName ?? "Unknown User";
                    title = $"📜 {(isAdminRequest ? $"{displayName}'s " : "Your ")}Transaction History";
                    description = $"Current balance: **{(await CasinoService.GetOrCreateCasinoUser(queryUserId)).Tokens:N0} tokens**";
                }

                var embed = new EmbedBuilder()
                    .WithTitle(title)
                    .WithColor(Color.Blue)
                    .WithDescription(description)
                    .WithFooter($"Page {page}/{totalPages} • {totalTransactions} total transactions");

                foreach (var transaction in transactions)
                {
                    var amountText = transaction.Amount >= 0 ? $"+{transaction.Amount}" : transaction.Amount.ToString();
                    var (emoji, transactionTitle, transactionDescription) = FormatTransactionDisplay(transaction, isAllUsersRequest);

                    embed.AddField($"{emoji} {transactionTitle}",
                        $"{amountText} tokens - *{TimestampTag.FromDateTime(transaction.CreatedAt)}*\n{transactionDescription}",
                        false);
                }

                var components = CreateHistoryNavigationComponents(isAllUsersRequest ? "all" : queryUserId, page, totalPages, isAdminRequest || isAllUsersRequest);

                if (isInitialCall)
                {
                    await Context.Interaction.FollowupAsync(embed: embed.Build(), components: components, ephemeral: true);
                }
                else
                {
                    await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Embed = embed.Build();
                        msg.Components = components;
                    });
                }

            }
            catch (Exception ex)
            {
                await LoggingService.LogChannelAndFile($"Casino: ERROR in DisplayTransactionHistory for user {Context.User.Username}: {ex.Message}", ExtendedLogSeverity.Error);

                var errorMessage = "❌ An error occurred while displaying transaction history. Please try again.";
                try
                {
                    if (isInitialCall)
                        await Context.Interaction.FollowupAsync(errorMessage, ephemeral: true);
                    else
                        await Context.Interaction.FollowupAsync(errorMessage, ephemeral: true);
                }
                catch
                {
                    await LoggingService.LogChannelAndFile($"Casino: Failed to send error response in DisplayTransactionHistory");
                }
            }
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
            await Context.Interaction.DeferAsync(ephemeral: true);

            if (!int.TryParse(pageStr, out int page))
            {
                await Context.Interaction.FollowupAsync("❌ Invalid page number.", ephemeral: true);
                return;
            }

            var isAdminRequest = requestType == "admin";
            var targetUser = userId == "all" ? null : Context.Guild.GetUser(ulong.Parse(userId));
            await DisplayTransactionHistory(userId: userId == "all" ? null : userId, page: page, targetUser: targetUser, isInitialCall: false);
        }

        private (string emoji, string title, string description) FormatTransactionDisplay(TokenTransaction transaction, bool showUserInfo = false)
        {
            var (emoji, title, description) = transaction.Type switch
            {
                TransactionType.TokenInitialisation => ("🎯", "Account Created", ""),
                TransactionType.DailyReward => ("📅", "Daily Reward", ""),
                TransactionType.Gift => GetGiftDisplay(transaction),
                TransactionType.Game => GetGameDisplay(transaction),
                TransactionType.Admin => GetAdminDisplay(transaction),
                TransactionType.Shop => GetShopDisplay(transaction),
                _ => ("❓", transaction.Type.ToString(), "")
            };

            // If showing user info (for all-users view), prepend user name to title
            if (showUserInfo)
            {
                var user = Context.Guild.GetUser(ulong.Parse(transaction.UserID));
                var username = user?.DisplayName ?? "Unknown User";
                return (emoji, $"{username}: {title}", description);
            }

            return (emoji, title, description);
        }

        private (string emoji, string title, string description) GetGiftDisplay(TokenTransaction transaction)
        {
            SocketGuildUser? user = null;
            var userId = transaction.Details?.GetValueOrDefault(transaction.Amount >= 0 ? "from" : "to");
            if (userId != null) user = Context.Guild.GetUser(ulong.Parse(userId));

            string title = transaction.Amount > 0 ? "Gift Received" : "Gift Sent";
            if (user != null) title = transaction.Amount > 0 ? $"Gift from {user.DisplayName}" : $"Gift to {user.DisplayName}";

            return ("🎁", title, "");
        }

        private (string emoji, string title, string description) GetGameDisplay(TokenTransaction transaction)
        {
            var gameName = transaction.Details?.GetValueOrDefault("game");

            string emoji = transaction.Amount >= 0 ? "📈" : "📉";
            string title = transaction.Amount >= 0 ? "Won" : "Lost";
            if (gameName != null) title += $" {CapitalizeFirst(gameName)}";

            return (emoji, title, "");
        }

        private (string emoji, string title, string description) GetAdminDisplay(TokenTransaction transaction)
        {
            var adminId = transaction.Details?.GetValueOrDefault("admin");
            var action = transaction.Details?.GetValueOrDefault("action");
            SocketGuildUser? admin = null;
            if (adminId != null) admin = Context.Guild.GetUser(ulong.Parse(adminId));

            string title = action switch
            {
                "add" => "Tokens Added",
                "set" => "Tokens Set",
                _ => $"UNKNOWN ACTION: {action}"
            };
            string description = action switch
            {
                "set" => "This overrides past transactions",
                _ => ""
            };

            if (admin != null) title += $" by Admin {admin.DisplayName}";

            return ("⚙️", title, description);
        }

        private (string emoji, string title, string description) GetShopDisplay(TokenTransaction transaction)
        {
            var itemTitle = transaction.Details?.GetValueOrDefault("item_title");
            
            string title = "Shop Purchase";
            if (itemTitle != null) title = $"Bought {itemTitle}";

            return ("🛒", title, "");
        }

        private string CapitalizeFirst(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            return char.ToUpper(input[0]) + input.Substring(1).ToLower();
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

            await CasinoService.UpdateUserTokens(targetUser.Id.ToString(), (long)amount, TransactionType.Admin, new Dictionary<string, string>
            {
                ["admin"] = Context.User.Id.ToString(),
                ["action"] = "add"
            });

            var embed = new EmbedBuilder()
                .WithTitle("⚙️ Admin: Tokens Added")
                .WithDescription($"Added **{amount:N0}** tokens to {targetUser.Mention}")
                .WithColor(Color.Purple)
                .Build();

            await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
            await LoggingService.LogChannelAndFile($"Admin Token Add: {Context.User.Username} added {amount} tokens to {targetUser.Username}");
        }

        #endregion

        [SlashCommand("daily", "Claim your daily token reward")]
        public async Task Daily()
        {
            if (!await CheckChannelPermissions()) return;

            try
            {
                await Context.Interaction.DeferAsync(ephemeral: true);

                var result = await CasinoService.TryClaimDailyReward(Context.User.Id.ToString());

                if (result.success)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("🎁 Daily Reward Claimed!")
                        .WithDescription($"You have received **{result.tokensAwarded:N0}** daily tokens!")
                        .AddField("💰 New Balance", $"{result.newBalance:N0} tokens", true)
                        .AddField("⏳ Next Reward", TimestampTag.FromDateTime(result.nextRewardTime, TimestampTagStyles.Relative).ToString(), true)
                        .WithColor(Color.Gold)
                        .WithCurrentTimestamp()
                        .Build();

                    await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
                    await LoggingService.LogChannelAndFile($"Casino: {Context.User.Username} claimed daily reward of {result.tokensAwarded} tokens");
                }
                else
                {
                    var nextRewardTime = await CasinoService.GetNextDailyRewardTime(Context.User.Id.ToString());

                    var embed = new EmbedBuilder()
                        .WithTitle("⏰ Daily Reward Not Available")
                        .WithDescription($"You have already claimed your daily reward!\n")
                        .AddField("⏳ Next Reward", TimestampTag.FromDateTime(nextRewardTime, TimestampTagStyles.Relative).ToString(), true)
                        .WithColor(Color.Orange)
                        .Build();

                    await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                await LoggingService.LogChannelAndFile($"Casino: ERROR in Daily command for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
                await LoggingService.LogChannelAndFile($"Casino: Daily Exception Details: {ex}");

                try
                {
                    if (!Context.Interaction.HasResponded)
                    {
                        await Context.Interaction.RespondAsync("❌ An error occurred while processing your daily reward. Please try again.", ephemeral: true);
                    }
                    else
                    {
                        await Context.Interaction.FollowupAsync("❌ An error occurred while processing your daily reward. Please try again.", ephemeral: true);
                    }
                }
                catch
                {
                    await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in Daily command");
                }
            }
        }
    }

    #endregion

    #region Shop Command

    [SlashCommand("shop", "View and purchase items from the casino shop")]
    public async Task Shop()
    {
        if (!await CheckChannelPermissions()) return;

        try
        {
            await Context.Interaction.DeferAsync(ephemeral: true);

            // Ensure shop items are initialized
            await CasinoService.InitializeDefaultShopItems();

            var user = await CasinoService.GetOrCreateCasinoUser(Context.User.Id.ToString());
            var allItems = await CasinoService.GetAllShopItems();
            var userPurchases = await CasinoService.GetUserShopPurchases(Context.User.Id.ToString());
            var purchasedItemIds = userPurchases.Select(p => p.ItemId).ToHashSet();

            if (allItems.Count == 0)
            {
                await Context.Interaction.FollowupAsync("🛒 The shop is currently empty. Please check back later!", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("🛒 Casino Shop")
                .WithDescription($"**Your Balance:** {user.Tokens:N0} tokens\n\nSelect an item from the dropdown below to purchase it.")
                .WithColor(Color.Purple);

            // Add shop items to embed (strike through purchased items)
            foreach (var item in allItems)
            {
                var isPurchased = purchasedItemIds.Contains(item.Id);
                var titleDisplay = isPurchased ? $"~~{item.Title}~~" : item.Title;
                var statusText = isPurchased ? " ✅ **OWNED**" : $" - **{item.Price:N0} tokens**";
                
                embed.AddField($"{titleDisplay}{statusText}", item.Description, false);
            }

            // Create dropdown with available items (not purchased)
            var availableItems = allItems.Where(item => !purchasedItemIds.Contains(item.Id)).ToList();
            
            if (availableItems.Count == 0)
            {
                embed.WithFooter("🎉 You own all available shop items!");
                await Context.Interaction.FollowupAsync(embed: embed.Build(), ephemeral: true);
                return;
            }

            var selectMenu = new SelectMenuBuilder()
                .WithPlaceholder("Select an item to purchase...")
                .WithCustomId($"shop_purchase:{Context.User.Id}")
                .WithMinValues(1)
                .WithMaxValues(1);

            foreach (var item in availableItems.Take(25)) // Discord limit of 25 options
            {
                var canAfford = user.Tokens >= item.Price;
                var label = $"{item.Title} - {item.Price:N0} tokens";
                if (!canAfford) label += " (Can't afford)";
                
                selectMenu.AddOption(
                    label: label,
                    value: item.Id.ToString(),
                    description: item.Description.Length > 100 ? item.Description.Substring(0, 97) + "..." : item.Description,
                    isDefault: false
                );
            }

            var components = new ComponentBuilder()
                .WithSelectMenu(selectMenu)
                .Build();

            await Context.Interaction.FollowupAsync(embed: embed.Build(), components: components, ephemeral: true);
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in Shop command for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: Shop Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while loading the shop. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while loading the shop. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in Shop command");
            }
        }
    }

    [ComponentInteraction("shop_purchase:*", true)]
    public async Task HandleShopPurchase(string userId, string[] selectedValues)
    {
        try
        {
            await Context.Interaction.DeferAsync(ephemeral: true);

            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.FollowupAsync("🚫 You are not authorized to make this purchase.", ephemeral: true);
                return;
            }

            if (selectedValues == null || selectedValues.Length == 0)
            {
                await Context.Interaction.FollowupAsync("❌ No item selected.", ephemeral: true);
                return;
            }

            if (!int.TryParse(selectedValues[0], out int itemId))
            {
                await Context.Interaction.FollowupAsync("❌ Invalid item selected.", ephemeral: true);
                return;
            }

            var (success, message) = await CasinoService.PurchaseShopItem(Context.User.Id.ToString(), itemId);

            if (success)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("🎉 Purchase Successful!")
                    .WithDescription(message)
                    .WithColor(Color.Green)
                    .WithCurrentTimestamp()
                    .Build();

                await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
                await LoggingService.LogChannelAndFile($"Casino: {Context.User.Username} successfully purchased shop item {itemId}");
            }
            else
            {
                var embed = new EmbedBuilder()
                    .WithTitle("❌ Purchase Failed")
                    .WithDescription(message)
                    .WithColor(Color.Red)
                    .Build();

                await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            await LoggingService.LogChannelAndFile($"Casino: ERROR in HandleShopPurchase for user {Context.User.Username} (ID: {Context.User.Id}): {ex.Message}", ExtendedLogSeverity.Error);
            await LoggingService.LogChannelAndFile($"Casino: HandleShopPurchase Exception Details: {ex}");

            try
            {
                if (!Context.Interaction.HasResponded)
                {
                    await Context.Interaction.RespondAsync("❌ An error occurred while processing your purchase. Please try again.", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.FollowupAsync("❌ An error occurred while processing your purchase. Please try again.", ephemeral: true);
                }
            }
            catch
            {
                await LoggingService.LogChannelAndFile($"Casino: Failed to send error response to user {Context.User.Username} in HandleShopPurchase");
            }
        }
    }

    #endregion

    #region Admin Reset Command

    [SlashCommand("reset", "Reset all casino data (Admin only) - REQUIRES CONFIRMATION")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ResetCasino()
    {
        if (!await CheckChannelPermissions()) return;

        await LoggingService.LogChannelAndFile($"Casino: ResetCasino called by {Context.User.Username} (ID: {Context.User.Id})");

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

            if (Context.User.Id.ToString() != userId)
            {
                await Context.Interaction.RespondAsync("🚫 You are not authorized to confirm this action.", ephemeral: true);
                return;
            }

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
}