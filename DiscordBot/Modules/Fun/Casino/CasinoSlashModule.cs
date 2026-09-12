using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Services;
using DiscordBot.Settings;

namespace DiscordBot.Modules.Fun.Casino;

[Group("casino", "Casino games and token management")]
public partial class CasinoSlashModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Dependency Injection

    public CasinoService CasinoService { get; set; } = null!;
    public ILoggingService LoggingService { get; set; } = null!;
    public BotSettings BotSettings { get; set; } = null!;

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
        public CasinoService CasinoService { get; set; } = null!;
        public ILoggingService LoggingService { get; set; } = null!;
        public BotSettings BotSettings { get; set; } = null!;
        public TransactionFormatter TransactionFormatter { get; set; } = null!;

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
            [Summary("amount", "Amount of tokens to gift")] int amount)
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
        public async Task TokenLeaderboard(
            [Summary("user", "User to show rank for (optional)")] SocketGuildUser? guildUser = null
        )
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync();

            var topUsers = await CasinoService.GetLeaderboard(15);

            Console.WriteLine($"Top users count: {topUsers.Count}");

            if (topUsers.Count == 0)
            {
                await Context.Interaction.FollowupAsync("📊 No users found in the casino system yet.");
                return;
            }

            var targetUserId = guildUser?.Id.ToString() ?? Context.User.Id.ToString();
            var allUsers = await CasinoService.GetLeaderboard(int.MaxValue);
            var targetUserEntry = allUsers.FirstOrDefault(u => u.UserID == targetUserId);
            var targetUserPosition = targetUserEntry != null ? allUsers.IndexOf(targetUserEntry) + 1 : -1;

            var embed = new EmbedBuilder()
                .WithTitle("🏆 Casino Token Leaderboard")
                .WithColor(Color.Gold)
                .WithFooter($"Showing top {topUsers.Count} out of {allUsers.Count} players")
                .WithCurrentTimestamp();

            // Display top 10 users
            for (int i = 0; i < topUsers.Count; i++)
            {
                AddTokenLeaderboardEntry(embed, topUsers[i], i + 1, true);
            }

            var isUserInTopEntries = topUsers.Any(u => u.UserID == targetUserId);
            // If target user is not in top, show their rank separately
            if (targetUserEntry != null && !isUserInTopEntries)
            {
                AddTokenLeaderboardEntry(embed, targetUserEntry, targetUserPosition, false);
            }

            await Context.Interaction.FollowupAsync(embed: embed.Build());
        }

        private void AddTokenLeaderboardEntry(EmbedBuilder embed, CasinoUser entry, int position, bool inline)
        {
            var medal = position switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => $"{position}."
            };

            var username = GetUsernameFromCasinoUser(entry);
            var fieldTitle = $"{medal} {username}";
            var fieldValue = $"{entry.Tokens:N0} tokens";

            embed.AddField(fieldTitle, fieldValue, inline);
        }

        private string GetUsernameFromCasinoUser(CasinoUser entry)
        {
            var user = Context.Guild.GetUser(ulong.Parse(entry.UserID));
            return user?.DisplayName ?? "Unknown User";
        }

        [SlashCommand("history", "View your recent token transactions")]
        public async Task TokenHistory()
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync(ephemeral: true);

            await DisplayTransactionHistory(userId: Context.User.Id.ToString(), page: 1, targetUser: null, isInitialCall: true);
        }

        private async Task DisplayTransactionHistory(string? userId = null, int page = 1, SocketGuildUser? targetUser = null, bool isInitialCall = false)
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
                        queryUserId = targetUser!.Id.ToString();
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
                    var (emoji, transactionTitle, transactionDescription) = TransactionFormatter.Format(transaction, Context.Guild, isAllUsersRequest);

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
}