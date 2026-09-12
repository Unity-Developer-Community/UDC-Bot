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