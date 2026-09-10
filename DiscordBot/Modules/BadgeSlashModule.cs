using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Extensions;
using DiscordBot.Services;

namespace DiscordBot.Modules;

[Group("badge", "Badge management commands")]
public class BadgeSlashModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Dependency Injection

    public BadgeService BadgeService { get; set; }
    public ILoggingService LoggingService { get; set; }

    #endregion

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
                .WithDescription($"Assigned **{badge.Title}** to {user.Mention}")
                .AddField("Badge Description", badge.Description)
                .AddField("Assigned By", Context.User.Mention)
                .AddField("Assigned At", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
                .WithColor(Color.Gold)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
            
            // Also send a congratulatory message to the user (if possible)
            try
            {
                var dmChannel = await user.CreateDMChannelAsync();
                var dmEmbed = new EmbedBuilder()
                    .WithTitle("🏆 You've been awarded a badge!")
                    .WithDescription($"**{badge.Title}**")
                    .AddField("Description", badge.Description)
                    .AddField("Server", Context.Guild.Name)
                    .WithColor(Color.Gold)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await dmChannel.SendMessageAsync(embed: dmEmbed);
            }
            catch
            {
                // DM failed, but badge assignment was successful
            }
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

    [SlashCommand("list", "List all available badges")]
    public async Task ListBadges()
    {
        await Context.Interaction.DeferAsync(ephemeral: false);

        var user = Context.User as SocketGuildUser;
        var isAdmin = BadgeService.IsUserAdmin(user);
        var badges = await BadgeService.GetAllBadges(isAdmin);
        
        if (!badges.Any())
        {
            await Context.Interaction.FollowupAsync("📭 No badges have been created yet.", ephemeral: false);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("🏆 Available Badges")
            .WithColor(Color.Blue)
            .WithTimestamp(DateTimeOffset.UtcNow);

        const int maxFieldValue = 1024;
        var description = string.Empty;

        foreach (var badge in badges)
        {
            var visibilityIndicator = isAdmin && !badge.IsPublic ? " 🔒" : "";
            var groupInfo = string.IsNullOrEmpty(badge.GroupKey) ? string.Empty : $"\n*Group: `{badge.GroupKey}`*";
            var badgeInfo = $"**{badge.Title}**{visibilityIndicator} (ID: {badge.Id})\n{badge.Description}{groupInfo}\n\n";
            
            if (description.Length + badgeInfo.Length > maxFieldValue)
            {
                embed.AddField("Badges", description.TrimEnd(), false);
                description = badgeInfo;
            }
            else
            {
                description += badgeInfo;
            }
        }

        if (!string.IsNullOrEmpty(description))
        {
            embed.AddField("Badges", description.TrimEnd(), false);
        }

        var footerText = $"Total badges: {badges.Count}";
        if (isAdmin)
        {
            var publicCount = badges.Count(b => b.IsPublic);
            var privateCount = badges.Count - publicCount;
            footerText += $" (Public: {publicCount}, Private: {privateCount})";
        }
        embed.WithFooter(footerText);

        await Context.Interaction.FollowupAsync(embed: embed.Build(), ephemeral: false);
    }

    [SlashCommand("view", "View badges of a specific user")]
    public async Task ViewUserBadges(
        [Summary("user", "The user whose badges you want to view")] SocketGuildUser user)
    {
        await Context.Interaction.DeferAsync(ephemeral: false);

        if (user == null)
        {
            await Context.Interaction.FollowupAsync("❌ User not found.", ephemeral: false);
            return;
        }

        var requestingUser = Context.User as SocketGuildUser;
        var isAdmin = BadgeService.IsUserAdmin(requestingUser);
        var userBadges = await BadgeService.GetUserBadges(user, isAdmin);
        
        if (!userBadges.Any())
        {
            await Context.Interaction.FollowupAsync($"📭 {user.Mention} has no badges yet.", ephemeral: false);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"🏆 {user.DisplayName}'s Badges")
            .WithThumbnailUrl(user.GetAvatarUrl())
            .WithColor(Color.Gold)
            .WithTimestamp(DateTimeOffset.UtcNow);

        const int maxFieldValue = 1024;
        var description = string.Empty;

        foreach (var userBadge in userBadges)
        {
            var awardedBy = Context.Guild.GetUser(Convert.ToUInt64(userBadge.AwardedBy));
            var awardedByName = awardedBy?.DisplayName ?? "Unknown";
            
            var visibilityIndicator = isAdmin && !userBadge.Badge.IsPublic ? " 🔒" : "";
            var groupInfo = string.IsNullOrEmpty(userBadge.Badge.GroupKey) ? string.Empty : $"\n*Group: `{userBadge.Badge.GroupKey}`*";
            var badgeInfo = $"**{userBadge.Badge.Title}**{visibilityIndicator}\n{userBadge.Badge.Description}{groupInfo}\n*Awarded by {awardedByName} on {userBadge.AwardedAt:yyyy-MM-dd}*\n\n";
            
            if (description.Length + badgeInfo.Length > maxFieldValue)
            {
                embed.AddField("Badges", description.TrimEnd(), false);
                description = badgeInfo;
            }
            else
            {
                description += badgeInfo;
            }
        }

        if (!string.IsNullOrEmpty(description))
        {
            embed.AddField("Badges", description.TrimEnd(), false);
        }

        var footerText = $"Total badges: {userBadges.Count}";
        if (isAdmin)
        {
            var publicCount = userBadges.Count(ub => ub.Badge.IsPublic);
            var privateCount = userBadges.Count - publicCount;
            if (privateCount > 0)
            {
                footerText += $" (Public: {publicCount}, Private: {privateCount})";
            }
        }
        embed.WithFooter(footerText);

        await Context.Interaction.FollowupAsync(embed: embed.Build(), ephemeral: false);
    }

    [SlashCommand("leaderboard", "Show the badge leaderboard")]
    public async Task BadgeLeaderboard(
        [Summary("group", "Optional group key to filter the leaderboard (example: udcjam)")] string? group = null)
    {
        await Context.Interaction.DeferAsync(ephemeral: false);

        if (!TryNormalizeGroupKey(group, out var normalizedGroup, out var groupValidationError))
        {
            await Context.Interaction.FollowupAsync(groupValidationError, ephemeral: true);
            return;
        }

        var requestingUser = Context.User as SocketGuildUser;
        var isAdmin = BadgeService.IsUserAdmin(requestingUser);
        var leaderboard = await BadgeService.GetBadgeLeaderboard(isAdmin, normalizedGroup);

        if (!leaderboard.Any())
        {
            var scope = normalizedGroup == null ? "yet" : $"for group `{normalizedGroup}` yet";
            await Context.Interaction.FollowupAsync($"📭 No visible badge awards have been recorded {scope}.", ephemeral: false);
            return;
        }

        var title = normalizedGroup == null ? "🏆 Badge Leaderboard" : $"🏆 Badge Leaderboard — {normalizedGroup}";
        var lines = leaderboard.Select((entry, index) =>
        {
            var userLabel = GetLeaderboardUserLabel(entry.UserId);
            var badgeWord = entry.BadgeCount == 1 ? "badge" : "badges";
            return $"**{index + 1}.** {userLabel} — **{entry.BadgeCount}** {badgeWord}";
        });

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(string.Join('\n', lines))
            .WithColor(Color.Purple)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter(isAdmin ? "Admins see public and private badges." : "Only public badges are counted.")
            .Build();

        await Context.Interaction.FollowupAsync(embed: embed, ephemeral: false);
    }

    private bool TryNormalizeGroupKey(string? group, out string? normalizedGroup, out string? errorMessage)
    {
        errorMessage = null;
        normalizedGroup = global::DiscordBot.Services.BadgeService.NormalizeGroupKey(group);
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

    private string GetLeaderboardUserLabel(string userId)
    {
        if (ulong.TryParse(userId, out var parsedUserId))
        {
            var user = Context.Guild.GetUser(parsedUserId);
            if (user != null)
                return user.Mention;
        }

        return $"User `{userId}`";
    }
}