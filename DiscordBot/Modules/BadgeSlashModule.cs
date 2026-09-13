using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Extensions;
using DiscordBot.Services;

namespace DiscordBot.Modules;

[Group("badge", "Badge management commands")]
public class BadgeSlashModule : InteractionModuleBase<SocketInteractionContext>
{
    private const int BadgeListPageSize = 24;
    private const int EmbedFieldNameMaxLength = 256;
    private const int EmbedFieldValueMaxLength = 1024;

    #region Dependency Injection

    public BadgeService BadgeService { get; set; }
    public ILoggingService LoggingService { get; set; }

    #endregion

    [SlashCommand("list", "List all available badges")]
    public async Task ListBadges()
    {
        await Context.Interaction.DeferAsync(ephemeral: true);

        var user = Context.User as SocketGuildUser;
        var isAdmin = BadgeService.IsUserAdmin(user);
        var badges = (await BadgeService.GetAllBadges(isAdmin))
            .OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Id)
            .ToList();

        if (!badges.Any())
        {
            await Context.Interaction.FollowupAsync("📭 No badges have been created yet.", ephemeral: true);
            return;
        }

        const int initialPage = 1;
        var totalPages = GetBadgeListTotalPages(badges.Count);
        var embed = BuildBadgeListEmbed(badges, isAdmin, initialPage, totalPages);
        var components = BuildBadgeListNavigationComponents(user?.Id ?? Context.User.Id, initialPage, totalPages);

        await Context.Interaction.FollowupAsync(embed: embed, components: components, ephemeral: true);
    }

    [ComponentInteraction("badge_list_nav:*:*", true)]
    public async Task NavigateBadgeList(string requesterId, string pageRaw)
    {
        await Context.Interaction.DeferAsync(ephemeral: true);

        if (!ulong.TryParse(requesterId, out var expectedUserId) || Context.User.Id != expectedUserId)
        {
            await Context.Interaction.FollowupAsync("🚫 You are not authorized to use these controls.", ephemeral: true);
            return;
        }

        if (!int.TryParse(pageRaw, out var requestedPage))
        {
            await Context.Interaction.FollowupAsync("❌ Invalid page number.", ephemeral: true);
            return;
        }

        var user = Context.User as SocketGuildUser;
        var isAdmin = BadgeService.IsUserAdmin(user);
        var badges = (await BadgeService.GetAllBadges(isAdmin))
            .OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Id)
            .ToList();

        if (!badges.Any())
        {
            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = "📭 No badges have been created yet.";
                msg.Embeds = Array.Empty<Embed>();
                msg.Components = new ComponentBuilder().Build();
            });
            return;
        }

        var totalPages = GetBadgeListTotalPages(badges.Count);
        var page = Math.Clamp(requestedPage, 1, totalPages);
        var embed = BuildBadgeListEmbed(badges, isAdmin, page, totalPages);
        var components = BuildBadgeListNavigationComponents(expectedUserId, page, totalPages);

        await Context.Interaction.ModifyOriginalResponseAsync(msg =>
        {
            msg.Content = null;
            msg.Embed = embed;
            msg.Components = components;
        });
    }

    private Embed BuildBadgeListEmbed(IReadOnlyList<Badge> badges, bool isAdmin, int page, int totalPages)
    {
        var embed = new EmbedBuilder()
            .WithTitle("🏆 Available Badges")
            .WithColor(Color.Blue)
            .WithTimestamp(DateTimeOffset.UtcNow);

        var footerText = $"Total badges: {badges.Count}";
        if (isAdmin)
        {
            var publicCount = badges.Count(b => b.IsPublic);
            var privateCount = badges.Count - publicCount;
            footerText += $" (Public: {publicCount}, Private: {privateCount})";
        }

        var startIndex = (page - 1) * BadgeListPageSize;
        var pageBadges = badges.Skip(startIndex).Take(BadgeListPageSize);
        const int maxEmbedTotalLength = 6000;
        var usedEmbedCharacters = "🏆 Available Badges".Length + footerText.Length;

        foreach (var badge in pageBadges)
        {
            var visibilityIndicator = isAdmin && !badge.IsPublic ? " 🔒" : string.Empty;
            var badgeIdInfo = isAdmin ? $" (ID: {badge.Id})" : string.Empty;
            var fieldName = $"{badge.Title}{visibilityIndicator}{badgeIdInfo}";
            var fieldValue = string.IsNullOrEmpty(badge.GroupKey)
                ? badge.Description
                : $"{badge.Description}\n*Group: `{badge.GroupKey}`*";

            if (fieldName.Length > EmbedFieldNameMaxLength)
            {
                fieldName = fieldName[..(EmbedFieldNameMaxLength - 3)] + "...";
            }

            if (fieldValue.Length > EmbedFieldValueMaxLength)
            {
                fieldValue = fieldValue[..(EmbedFieldValueMaxLength - 3)] + "...";
            }

            var fieldCharacters = fieldName.Length + fieldValue.Length;
            if (usedEmbedCharacters + fieldCharacters > maxEmbedTotalLength)
            {
                break;
            }

            embed.AddField(fieldName, fieldValue, true);
            usedEmbedCharacters += fieldCharacters;
        }

        embed.WithFooter(footerText);

        return embed.Build();
    }

    private MessageComponent BuildBadgeListNavigationComponents(ulong requesterId, int page, int totalPages)
    {
        var builder = new ComponentBuilder()
            .WithButton("◀️ Previous", $"badge_list_nav:{requesterId}:{page - 1}", ButtonStyle.Secondary, disabled: page <= 1)
            .WithButton($"Page {page}/{totalPages}", "badge_list_page_info", ButtonStyle.Primary, disabled: true)
            .WithButton("Next ▶️", $"badge_list_nav:{requesterId}:{page + 1}", ButtonStyle.Secondary, disabled: page >= totalPages);

        return builder.Build();
    }

    private int GetBadgeListTotalPages(int badgeCount)
    {
        return Math.Max(1, (int)Math.Ceiling(badgeCount / (double)BadgeListPageSize));
    }

    [SlashCommand("view", "View badges of a specific user")]
    public async Task ViewUserBadges(
        [Summary("user", "The user whose badges you want to view")] SocketGuildUser user)
    {
        await Context.Interaction.DeferAsync(ephemeral: true);

        if (user == null)
        {
            await Context.Interaction.FollowupAsync("❌ User not found.", ephemeral: true);
            return;
        }

        var requestingUser = Context.User as SocketGuildUser;
        var isAdmin = BadgeService.IsUserAdmin(requestingUser);
        var userBadges = await BadgeService.GetUserBadges(user, isAdmin);

        if (!userBadges.Any())
        {
            await Context.Interaction.FollowupAsync($"📭 {user.Mention} has no badges yet.", ephemeral: true);
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
            var badge = userBadge.EnsureBadgeDetails();
            if (string.IsNullOrWhiteSpace(badge.Title) || string.IsNullOrWhiteSpace(badge.Description))
            {
                continue;
            }

            var awardedByName = "Unknown";
            if (ulong.TryParse(userBadge.AwardedBy, out var awardedById))
            {
                var awardedBy = Context.Guild.GetUser(awardedById);
                awardedByName = awardedBy?.DisplayName ?? "Unknown";
            }

            var visibilityIndicator = isAdmin && !badge.IsPublic ? " 🔒" : "";
            var groupInfo = string.IsNullOrEmpty(badge.GroupKey) ? string.Empty : $"\n*Group: `{badge.GroupKey}`*";
            var badgeInfo = $"**{badge.Title}**{visibilityIndicator}\n{badge.Description}{groupInfo}\n*Awarded by {awardedByName} on {userBadge.AwardedAt:yyyy-MM-dd}*\n\n";

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
            var publicCount = userBadges.Count(ub => ub.EnsureBadgeDetails().IsPublic);
            var privateCount = userBadges.Count - publicCount;
            if (privateCount > 0)
            {
                footerText += $" (Public: {publicCount}, Private: {privateCount})";
            }
        }
        embed.WithFooter(footerText);

        await Context.Interaction.FollowupAsync(embed: embed.Build(), ephemeral: true);
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
        var leaderboard = await BadgeService.GetBadgeLeaderboard(false, normalizedGroup);

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