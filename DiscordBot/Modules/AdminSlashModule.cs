using System.Globalization;
using System.Text;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Services;
using DiscordBot.Settings;

namespace DiscordBot.Modules;

[Group("admin", "Administrator-only commands")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public class AdminSlashModule : InteractionModuleBase
{
    [Group("badge", "Badge administration commands")]
    public class BadgeAdminCommands : InteractionModuleBase<SocketInteractionContext>
    {
        public BadgeService BadgeService { get; set; } = null!;

        [SlashCommand("create", "Create a new badge")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task CreateBadge(
            [Summary("title", "The title of the badge")] string title,
            [Summary("description", "The description of the badge")] string description,
            [Summary("public", "Whether the badge is public (default: true)")] bool isPublic = true,
            [Summary("group", "Optional group key for leaderboard filtering (example: udcjam)")] string? group = null)
        {
            await Context.Interaction.DeferAsync(ephemeral: true);

            if (string.IsNullOrWhiteSpace(title) || title.Length > 100)
            {
                await Context.Interaction.FollowupAsync("Badge title must be between 1 and 100 characters.", ephemeral: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(description) || description.Length > 500)
            {
                await Context.Interaction.FollowupAsync("Badge description must be between 1 and 500 characters.", ephemeral: true);
                return;
            }

            if (!TryNormalizeGroupKey(group, out var normalizedGroup, out var groupValidationError))
            {
                await Context.Interaction.FollowupAsync(groupValidationError, ephemeral: true);
                return;
            }

            var createdBadge = await BadgeService.CreateBadge(title, description, isPublic, normalizedGroup);

            if (createdBadge != null)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("🏆 Badge Created Successfully")
                    .WithDescription($"**{createdBadge.Title}**")
                    .AddField("Description", createdBadge.Description)
                    .AddField("Badge ID", createdBadge.Id.ToString())
                    .AddField("Group", createdBadge.GroupKey ?? "None")
                    .AddField("Visibility", createdBadge.IsPublic ? "Public" : "Private")
                    .AddField("Created", createdBadge.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"))
                    .WithColor(Color.Green)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
            }
            else
            {
                await Context.Interaction.FollowupAsync("❌ Failed to create badge. A badge with this title may already exist.", ephemeral: true);
            }
        }

        [SlashCommand("edit", "Edit an existing badge")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task EditBadge(
            [Summary("badge", "The title of the badge to edit")] string badgeTitle,
            [Summary("title", "New title for the badge")] string newTitle,
            [Summary("description", "New description for the badge")] string newDescription,
            [Summary("public", "Whether the badge should be public")] bool isPublic = true,
            [Summary("group", "Optional new group key (leave unset to keep the current group)")] string? group = null,
            [Summary("clear-group", "Clear the badge's current group")] bool clearGroup = false)
        {
            await Context.Interaction.DeferAsync(ephemeral: true);

            if (string.IsNullOrWhiteSpace(newTitle) || newTitle.Length > 100)
            {
                await Context.Interaction.FollowupAsync("Badge title must be between 1 and 100 characters.", ephemeral: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(newDescription) || newDescription.Length > 500)
            {
                await Context.Interaction.FollowupAsync("Badge description must be between 1 and 500 characters.", ephemeral: true);
                return;
            }

            var existingBadge = await BadgeService.GetBadgeByTitle(badgeTitle);
            if (existingBadge == null)
            {
                await Context.Interaction.FollowupAsync($"❌ Badge '{badgeTitle}' not found.", ephemeral: true);
                return;
            }

            if (!TryNormalizeGroupKey(group, out var normalizedGroup, out var groupValidationError))
            {
                await Context.Interaction.FollowupAsync(groupValidationError, ephemeral: true);
                return;
            }

            var updatedBadge = await BadgeService.UpdateBadge(
                existingBadge.Id,
                newTitle,
                newDescription,
                isPublic,
                clearGroup ? null : normalizedGroup,
                clearGroup || group != null);

            if (updatedBadge != null)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("✏️ Badge Updated Successfully")
                    .WithDescription($"**{updatedBadge.Title}**")
                    .AddField("Description", updatedBadge.Description)
                    .AddField("Badge ID", updatedBadge.Id.ToString())
                    .AddField("Group", updatedBadge.GroupKey ?? "None")
                    .AddField("Visibility", updatedBadge.IsPublic ? "Public" : "Private")
                    .AddField("Updated", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
                    .WithColor(Color.Blue)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
            }
            else
            {
                await Context.Interaction.FollowupAsync("❌ Failed to update badge. The new title may already be in use.", ephemeral: true);
            }
        }

        [SlashCommand("assign", "Assign a badge to a user")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task AssignBadge(
            [Summary("user", "The user to assign the badge to")] SocketGuildUser user,
            [Summary("badge", "The title of the badge to assign")] string badgeTitle)
        {
            await Context.Interaction.DeferAsync(ephemeral: true);

            if (user == null)
            {
                await Context.Interaction.FollowupAsync("❌ User not found.", ephemeral: true);
                return;
            }

            if (user.IsBot)
            {
                await Context.Interaction.FollowupAsync("❌ Cannot assign badges to bots.", ephemeral: true);
                return;
            }

            var badge = await BadgeService.GetBadgeByTitle(badgeTitle);
            if (badge == null)
            {
                await Context.Interaction.FollowupAsync($"❌ Badge '{badgeTitle}' not found.", ephemeral: true);
                return;
            }

            var success = await BadgeService.AssignBadgeToUser(user, badge, Context.User as SocketGuildUser);

            if (success)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("🏆 Badge Assigned Successfully")
                    .WithDescription($"Assigned **{badge.Title}** to **{user.DisplayName}**")
                    .AddField("Badge Description", badge.Description)
                    .AddField("Assigned By", Context.User.Mention)
                    .AddField("Assigned At", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
                    .WithColor(Color.Gold)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
            }
            else
            {
                await Context.Interaction.FollowupAsync("❌ Failed to assign badge. The user may already have this badge.", ephemeral: true);
            }
        }

        [SlashCommand("remove", "Remove a badge from a user")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task RemoveBadge(
            [Summary("user", "The user to remove the badge from")] SocketGuildUser user,
            [Summary("badge", "The title of the badge to remove")] string badgeTitle)
        {
            await Context.Interaction.DeferAsync(ephemeral: true);

            if (user == null)
            {
                await Context.Interaction.FollowupAsync("❌ User not found.", ephemeral: true);
                return;
            }

            var badge = await BadgeService.GetBadgeByTitle(badgeTitle);
            if (badge == null)
            {
                await Context.Interaction.FollowupAsync($"❌ Badge '{badgeTitle}' not found.", ephemeral: true);
                return;
            }

            var success = await BadgeService.RemoveBadgeFromUser(user, badge);

            if (success)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("🗑️ Badge Removed Successfully")
                    .WithDescription($"Removed **{badge.Title}** from {user.Mention}")
                    .AddField("Badge Description", badge.Description)
                    .AddField("Removed By", Context.User.Mention)
                    .AddField("Removed At", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
                    .WithColor(Color.Orange)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
            }
            else
            {
                await Context.Interaction.FollowupAsync("❌ Failed to remove badge. The user may not have this badge.", ephemeral: true);
            }
        }

        private bool TryNormalizeGroupKey(string? group, out string? normalizedGroup, out string? errorMessage)
        {
            errorMessage = null;
            normalizedGroup = BadgeService.NormalizeGroupKey(group);
            if (normalizedGroup == null)
                return true;

            if (normalizedGroup.Length > 64)
            {
                errorMessage = "Badge group must be 64 characters or fewer.";
                return false;
            }

            if (normalizedGroup.Any(ch => !char.IsLetterOrDigit(ch) && ch != '-' && ch != '_'))
            {
                errorMessage = "Badge group may only contain letters, numbers, hyphens, and underscores.";
                return false;
            }

            return true;
        }
    }

    // Discord allows only two levels of command nesting (command -> group -> subcommand),
    // so casino admin actions are flat subcommands of /admin casino (e.g. /admin casino tokens-set),
    // not a further nested "tokens" subcommand group.
    [Group("casino", "Casino administration commands")]
    public class CasinoAdminCommands : InteractionModuleBase<SocketInteractionContext>
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

        [SlashCommand("tokens-history", "View transaction history for any user or all users")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task TokenHistoryAdmin(
            [Summary("user", "User to view history for (optional - if omitted, shows all users)")] SocketGuildUser? targetUser = null)
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync(ephemeral: true);

            await DisplayAdminTransactionHistory(userId: null, page: 1, targetUser: targetUser, isInitialCall: true);
        }

        [SlashCommand("tokens-set", "Set a user's token balance")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetTokens(
            [Summary("user", "User to set tokens for")] SocketGuildUser targetUser,
            [Summary("amount", "New token amount")] int amount)
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

        [SlashCommand("tokens-add", "Add tokens to a user's balance")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task AddTokens(
            [Summary("user", "User to add tokens to")] SocketGuildUser targetUser,
            [Summary("amount", "Amount of tokens to add")] int amount)
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync(ephemeral: true);

            await CasinoService.UpdateUserTokens(targetUser.Id.ToString(), amount, TransactionKind.Admin, new Dictionary<string, string>
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

        [SlashCommand("reset", "Reset all casino data - REQUIRES CONFIRMATION")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ResetCasino()
        {
            if (!await CheckChannelPermissions()) return;

            await LoggingService.LogChannelAndFile($"Casino: ResetCasino called by {Context.User.Username} (ID: {Context.User.Id})");

            var embed = new EmbedBuilder()
                .WithTitle("⚠️ Casino Reset Confirmation")
                .WithDescription("**WARNING:** This will permanently delete:\n"
                    + "• All user token balances\n"
                    + "• All transaction history\n"
                    + "• All active games\n\n"
                    + "This action **CANNOT** be undone!\n\n"
                    + "This confirmation will expire in 30 seconds.")
                .WithColor(Color.Red)
                .Build();

            var components = new ComponentBuilder()
                .WithButton("❌ Cancel", $"admin_casino_reset_cancel:{Context.User.Id}", ButtonStyle.Secondary)
                .WithButton("⚠️ CONFIRM RESET", $"admin_casino_reset_confirm:{Context.User.Id}", ButtonStyle.Danger)
                .Build();

            await Context.Interaction.RespondAsync(embed: embed, components: components, ephemeral: true);

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

        [ComponentInteraction("admin_casino_reset_confirm:*", true)]
        public async Task ConfirmReset(string userId)
        {
            try
            {
                await Context.Interaction.DeferAsync(ephemeral: true);

                if (Context.User.Id.ToString() != userId)
                {
                    await Context.Interaction.FollowupAsync("🚫 You are not authorized to confirm this action.", ephemeral: true);
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

        [ComponentInteraction("admin_casino_reset_cancel:*", true)]
        public async Task CancelReset(string userId)
        {
            try
            {
                await Context.Interaction.DeferAsync(ephemeral: true);

                if (Context.User.Id.ToString() != userId)
                {
                    await Context.Interaction.FollowupAsync("🚫 You are not authorized to cancel this action.", ephemeral: true);
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

        private async Task DisplayAdminTransactionHistory(string? userId = null, int page = 1, SocketGuildUser? targetUser = null, bool isInitialCall = false)
        {
            try
            {
                var queryUserId = userId ?? Context.User.Id.ToString();
                var isAdminRequest = targetUser != null && targetUser.Id != Context.User.Id;
                var isAllUsersRequest = targetUser == null && userId == null;

                if (isAdminRequest)
                {
                    queryUserId = targetUser!.Id.ToString();
                }

                const int transactionsPerPage = 5;

                List<TokenTransaction> allTransactions;
                if (isAllUsersRequest)
                {
                    var recentTransactions = await CasinoService.GetAllRecentTransactions(int.MaxValue);
                    allTransactions = recentTransactions.ToList();
                }
                else
                {
                    allTransactions = await CasinoService.GetUserTransactionHistory(queryUserId, int.MaxValue);
                }

                var totalTransactions = allTransactions.Count;

                if (totalTransactions == 0)
                {
                    var noHistoryText = isAllUsersRequest
                        ? "📜 No transaction history found in the casino system."
                        : isAdminRequest
                            ? $"📜 No transaction history found for {targetUser?.DisplayName}."
                            : "📜 No transaction history found.";

                    await Context.Interaction.FollowupAsync(noHistoryText, ephemeral: true);
                    return;
                }

                var totalPages = (int)Math.Ceiling(totalTransactions / (double)transactionsPerPage);
                page = Math.Max(1, Math.Min(page, totalPages));

                var transactions = allTransactions.Skip((page - 1) * transactionsPerPage).Take(transactionsPerPage).ToList();

                string title;
                string description;
                if (isAllUsersRequest)
                {
                    title = "📜 All Users Transaction History";
                    description = "Recent transactions across all casino users";
                }
                else
                {
                    var displayUser = targetUser;
                    if (displayUser == null && ulong.TryParse(queryUserId, out var queryUserUlong))
                    {
                        displayUser = Context.Guild.GetUser(queryUserUlong);
                    }

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

                var components = CreateAdminHistoryNavigationComponents(isAllUsersRequest ? "all" : queryUserId, page, totalPages, isAdminRequest || isAllUsersRequest);

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
                await LoggingService.LogChannelAndFile($"Casino Admin: ERROR in DisplayAdminTransactionHistory for user {Context.User.Username}: {ex.Message}", ExtendedLogSeverity.Error);

                var errorMessage = "❌ An error occurred while displaying transaction history. Please try again.";
                try
                {
                    await Context.Interaction.FollowupAsync(errorMessage, ephemeral: true);
                }
                catch
                {
                    await LoggingService.LogChannelAndFile("Casino Admin: Failed to send error response in DisplayAdminTransactionHistory");
                }
            }
        }

        private MessageComponent CreateAdminHistoryNavigationComponents(string userId, int currentPage, int totalPages, bool isAdminRequest)
        {
            var builder = new ComponentBuilder();

            if (totalPages <= 1)
            {
                return builder.Build();
            }

            builder.WithButton("◀️ Previous", $"admin_history_nav:{userId}:{currentPage - 1}:{(isAdminRequest ? "admin" : "self")}", ButtonStyle.Secondary, disabled: currentPage <= 1);
            builder.WithButton($"Page {currentPage}/{totalPages}", "admin_page_info", ButtonStyle.Primary, disabled: true);
            builder.WithButton("Next ▶️", $"admin_history_nav:{userId}:{currentPage + 1}:{(isAdminRequest ? "admin" : "self")}", ButtonStyle.Secondary, disabled: currentPage >= totalPages);

            return builder.Build();
        }

        [ComponentInteraction("admin_history_nav:*:*:*", true)]
        public async Task NavigateAdminHistory(string userId, string pageStr, string requestType)
        {
            await Context.Interaction.DeferAsync(ephemeral: true);

            if (!int.TryParse(pageStr, out var page))
            {
                await Context.Interaction.FollowupAsync("❌ Invalid page number.", ephemeral: true);
                return;
            }

            var isAdminRequest = requestType == "admin";
            SocketGuildUser? targetUser = null;
            if (isAdminRequest && userId != "all" && ulong.TryParse(userId, out var targetUserId))
            {
                targetUser = Context.Guild.GetUser(targetUserId);
            }

            await DisplayAdminTransactionHistory(userId: userId == "all" ? null : userId, page: page, targetUser: targetUser, isInitialCall: false);
        }
    }

    [Group("bday", "Birthday administration commands")]
    public class BirthdayAdminCommands : InteractionModuleBase<SocketInteractionContext>
    {
        public DatabaseService DatabaseService { get; set; } = null!;
        public ILoggingService LoggingService { get; set; } = null!;

        [SlashCommand("set-user", "Set another user's birthday")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetUserBirthday(
            [Summary(description: "User to set the birthday for")] SocketGuildUser targetUser,
            [Summary(description: "Birthday in DD/MM/YYYY or DD/MM format (e.g., 15/03/1990 or 15/03)")] string date)
        {
            await Context.Interaction.DeferAsync(ephemeral: true);

            if (targetUser.IsBot)
            {
                await Context.Interaction.FollowupAsync("🤖 You cannot set a birthday for a bot.", ephemeral: true);
                return;
            }

            if (!TryParseBirthdayInput(date, out var birthday))
            {
                await Context.Interaction.FollowupAsync("Invalid date format. Please use DD/MM/YYYY or DD/MM format (e.g., 15/03/1990 or 15/03).", ephemeral: true);
                return;
            }

            try
            {
                var user = await DatabaseService.GetOrAddUser(targetUser);
                if (user == null)
                {
                    await Context.Interaction.FollowupAsync("Failed to access user data.", ephemeral: true);
                    return;
                }

                await DatabaseService.Query.UpdateBirthday(user.UserID, birthday);
                await Context.Interaction.FollowupAsync($"Set **{targetUser.DisplayName}**'s birthday to **{FormatBirthday(birthday)}**. 🎂", ephemeral: true);
                await LoggingService.LogAction(
                    $"[BirthdayAdminSet] actor={Context.User.Id} target={targetUser.Id} value={birthday:yyyy-MM-dd}",
                    ExtendedLogSeverity.Info);
            }
            catch (Exception e)
            {
                await LoggingService.LogAction(
                    $"Error setting birthday for target {targetUser.Id} by actor {Context.User.Id}: {e.Message}",
                    ExtendedLogSeverity.Warning);
                await Context.Interaction.FollowupAsync("An error occurred while setting the birthday.", ephemeral: true);
            }
        }

        [SlashCommand("del-user", "Remove another user's birthday")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task RemoveUserBirthday(
            [Summary(description: "User to remove the birthday for")] SocketGuildUser targetUser)
        {
            await Context.Interaction.DeferAsync(ephemeral: true);

            try
            {
                var user = await DatabaseService.GetOrAddUser(targetUser);
                if (user == null)
                {
                    await Context.Interaction.FollowupAsync("Failed to access user data.", ephemeral: true);
                    return;
                }

                var currentBirthday = await DatabaseService.Query.GetBirthday(user.UserID);
                if (currentBirthday == null)
                {
                    await Context.Interaction.FollowupAsync($"**{targetUser.DisplayName}** doesn't have a birthday set.", ephemeral: true);
                    return;
                }

                await DatabaseService.Query.UpdateBirthday(user.UserID, null);
                await Context.Interaction.FollowupAsync($"Removed **{targetUser.DisplayName}**'s birthday.", ephemeral: true);
                await LoggingService.LogAction(
                    $"[BirthdayAdminDelete] actor={Context.User.Id} target={targetUser.Id}",
                    ExtendedLogSeverity.Info);
            }
            catch (Exception e)
            {
                await LoggingService.LogAction(
                    $"Error removing birthday for target {targetUser.Id} by actor {Context.User.Id}: {e.Message}",
                    ExtendedLogSeverity.Warning);
                await Context.Interaction.FollowupAsync("An error occurred while removing the birthday.", ephemeral: true);
            }
        }

        [SlashCommand("list", "List all defined birthdays")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ListBirthdays()
        {
            await Context.Interaction.DeferAsync(ephemeral: true);

            try
            {
                var birthdays = (await DatabaseService.Query.GetAllBirthdays()).ToList();
                if (birthdays.Count == 0)
                {
                    await Context.Interaction.FollowupAsync("No birthdays are currently defined.", ephemeral: true);
                    return;
                }

                var lines = new List<string>(birthdays.Count);
                for (var i = 0; i < birthdays.Count; i++)
                {
                    lines.Add(await BuildBirthdayListLine(birthdays[i], i + 1));
                }

                var chunks = BuildBirthdayListChunks(lines, birthdays.Count);
                foreach (var chunk in chunks)
                {
                    await Context.Interaction.FollowupAsync(chunk, ephemeral: true);
                }

                await LoggingService.LogAction(
                    $"[BirthdayAdminList] actor={Context.User.Id} count={birthdays.Count}",
                    ExtendedLogSeverity.Info);
            }
            catch (Exception e)
            {
                await LoggingService.LogAction($"Error listing birthdays for actor {Context.User.Id}: {e.Message}", ExtendedLogSeverity.Warning);
                await Context.Interaction.FollowupAsync("An error occurred while listing birthdays.", ephemeral: true);
            }
        }

        private async Task<string> BuildBirthdayListLine(ServerUser entry, int index)
        {
            string displayName;
            if (!ulong.TryParse(entry.UserID, out var userId))
            {
                displayName = $"Unknown User ({entry.UserID})";
            }
            else
            {
                var guildUser = Context.Guild.GetUser(userId);
                displayName = guildUser?.DisplayName ?? $"Unknown User ({entry.UserID})";
            }

            var birthdayText = entry.Birthday.HasValue ? FormatBirthday(entry.Birthday.Value) : "Unknown date";
            return $"{index}. {birthdayText} - {displayName}";
        }

        private static List<string> BuildBirthdayListChunks(IList<string> lines, int totalCount)
        {
            const int maxMessageLength = 1800;
            var chunks = new List<string>();
            var current = new StringBuilder();
            var isFirstChunk = true;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                if (current.Length == 0)
                {
                    var header = isFirstChunk ? $"Birthday List ({totalCount})\n" : "Birthday List (continued)\n";
                    current.Append(header);
                    isFirstChunk = false;
                }

                if (current.Length + line.Length + 1 > maxMessageLength)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                    current.Append("Birthday List (continued)\n");
                }

                current.AppendLine(line);
            }

            if (current.Length > 0)
            {
                chunks.Add(current.ToString());
            }

            return chunks;
        }

        private static string FormatBirthday(DateTime birthday)
        {
            var provider = CultureInfo.InvariantCulture;
            var birthdayString = birthday.ToString("MMMM dd", provider);
            if (birthday.Year != 1900)
            {
                birthdayString += $", {birthday.Year}";
            }

            return birthdayString;
        }

        private static bool TryParseBirthdayInput(string input, out DateTime birthday)
        {
            birthday = default;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var provider = CultureInfo.InvariantCulture;

            // Parse as a date-only value, then force UTC kind for PostgreSQL timestamptz writes.
            if (DateTime.TryParseExact(input, "d/M/yyyy", provider, DateTimeStyles.None, out birthday))
            {
                birthday = DateTime.SpecifyKind(birthday, DateTimeKind.Utc);
                return true;
            }

            // Try parsing without year (DD/MM) - use 1900 as sentinel value
            if (DateTime.TryParseExact(input, "d/M", provider, DateTimeStyles.None, out var tempDate))
            {
                birthday = DateTime.SpecifyKind(new DateTime(1900, tempDate.Month, tempDate.Day), DateTimeKind.Utc);
                return true;
            }

            return false;
        }
    }
}
