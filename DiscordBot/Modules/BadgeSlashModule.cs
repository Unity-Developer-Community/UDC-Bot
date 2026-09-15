using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Extensions;
using DiscordBot.Services;
using System.Text;
using System.Text.RegularExpressions;

namespace DiscordBot.Modules;

[Group("badge", "Badge management commands")]
public class BadgeSlashModule : InteractionModuleBase<SocketInteractionContext>
{
    private const int BadgeListPageSize = 24;
    private const int EmbedFieldNameMaxLength = 256;
    private const int EmbedFieldValueMaxLength = 1024;

    /// <summary>Discord caps a select menu at 25 options.</summary>
    private const int BadgeSelectMaxOptions = 25;
    private const string AssignBadgeSelectCustomIdPrefix = "badge_assign_select";
    private static readonly IComparer<string> BadgeTitleNaturalComparer = new NaturalBadgeTitleComparer();

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
            .OrderBy(b => b.Title, BadgeTitleNaturalComparer)
            .ThenBy(b => b.Id)
            .ToList();
        var badgeHolderCounts = isAdmin
            ? await BadgeService.GetBadgeHolderCounts()
            : null;

        if (!badges.Any())
        {
            await Context.Interaction.FollowupAsync("📭 No badges have been created yet.", ephemeral: true);
            return;
        }

        const int initialPage = 1;
        var totalPages = GetBadgeListTotalPages(badges.Count);
        var embed = BuildBadgeListEmbed(badges, isAdmin, badgeHolderCounts, initialPage, totalPages);
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
            .OrderBy(b => b.Title, BadgeTitleNaturalComparer)
            .ThenBy(b => b.Id)
            .ToList();
        var badgeHolderCounts = isAdmin
            ? await BadgeService.GetBadgeHolderCounts()
            : null;

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
        var embed = BuildBadgeListEmbed(badges, isAdmin, badgeHolderCounts, page, totalPages);
        var components = BuildBadgeListNavigationComponents(expectedUserId, page, totalPages);

        await Context.Interaction.ModifyOriginalResponseAsync(msg =>
        {
            msg.Content = null;
            msg.Embed = embed;
            msg.Components = components;
        });
    }

    private Embed BuildBadgeListEmbed(IReadOnlyList<Badge> badges, bool isAdmin, IReadOnlyDictionary<int, long>? badgeHolderCounts, int page, int totalPages)
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
            var holderCount = 0L;
            if (isAdmin && badgeHolderCounts != null && badgeHolderCounts.TryGetValue(badge.Id, out var count))
            {
                holderCount = count;
            }

            var fieldName = $"{badge.Title}{visibilityIndicator}{badgeIdInfo}";
            var holderInfo = isAdmin ? $"\n*Holders: {holderCount}*" : string.Empty;
            var fieldValue = string.IsNullOrEmpty(badge.GroupKey)
                ? $"{badge.Description}{holderInfo}"
                : $"{badge.Description}\n*Group: `{badge.GroupKey}`*{holderInfo}";

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

    private sealed class NaturalBadgeTitleComparer : IComparer<string>
    {
        private static readonly Regex TokenRegex = new(@"\d+|\D+", RegexOptions.Compiled);

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x == null)
                return -1;
            if (y == null)
                return 1;

            var xTokens = TokenRegex.Matches(x);
            var yTokens = TokenRegex.Matches(y);
            var minCount = Math.Min(xTokens.Count, yTokens.Count);

            for (var i = 0; i < minCount; i++)
            {
                var xPart = xTokens[i].Value;
                var yPart = yTokens[i].Value;
                var xIsNumber = char.IsDigit(xPart[0]);
                var yIsNumber = char.IsDigit(yPart[0]);

                if (xIsNumber && yIsNumber)
                {
                    var numericComparison = CompareNumberParts(xPart, yPart);
                    if (numericComparison != 0)
                        return numericComparison;

                    continue;
                }

                var textComparison = StringComparer.OrdinalIgnoreCase.Compare(xPart, yPart);
                if (textComparison != 0)
                    return textComparison;
            }

            return xTokens.Count.CompareTo(yTokens.Count);
        }

        private static int CompareNumberParts(string xPart, string yPart)
        {
            var xTrimmed = xPart.TrimStart('0');
            var yTrimmed = yPart.TrimStart('0');

            if (xTrimmed.Length == 0)
                xTrimmed = "0";
            if (yTrimmed.Length == 0)
                yTrimmed = "0";

            var lengthComparison = xTrimmed.Length.CompareTo(yTrimmed.Length);
            if (lengthComparison != 0)
                return lengthComparison;

            var lexicalComparison = string.CompareOrdinal(xTrimmed, yTrimmed);
            if (lexicalComparison != 0)
                return lexicalComparison;

            return xPart.Length.CompareTo(yPart.Length);
        }
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

        await ShowUserBadgesForTarget(user);
    }

    [UserCommand("View Badges")]
    public async Task ViewUserBadgesContext(IUser user)
    {
        await Context.Interaction.DeferAsync(ephemeral: true);

        if (user is not SocketGuildUser guildUser)
        {
            await Context.Interaction.FollowupAsync("❌ User not found.", ephemeral: true);
            return;
        }

        await ShowUserBadgesForTarget(guildUser);
    }

    [UserCommand("Assign Badge")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task AssignBadgeContext(IUser user)
    {
        if (user is not SocketGuildUser guildUser)
        {
            await Context.Interaction.RespondAsync("❌ User not found.", ephemeral: true);
            return;
        }

        if (guildUser.IsBot)
        {
            await Context.Interaction.RespondAsync("❌ Cannot assign badges to bots.", ephemeral: true);
            return;
        }

        await Context.Interaction.DeferAsync(ephemeral: true);

        var assignableBadges = await GetAssignableBadgesAsync(guildUser);
        if (assignableBadges.Count == 0)
        {
            await Context.Interaction.FollowupAsync($"📭 {guildUser.Mention} already has every badge.", ephemeral: true);
            return;
        }

        var options = assignableBadges.Take(BadgeSelectMaxOptions).Select(badge =>
        {
            var option = new SelectMenuOptionBuilder()
                .WithLabel(badge.Title.ToSelectOptionText())
                .WithValue(badge.Id.ToString());

            var description = badge.Description.ToSelectOptionText();
            return string.IsNullOrEmpty(description) ? option : option.WithDescription(description);
        }).ToList();

        // The assignable badge list is per-user, so it cannot be presented in a modal: modal select menu
        // options are declared as attributes at compile time. A message select menu is built at runtime
        // instead, and picking the entries doubles as the confirmation step.
        var selectMenu = new SelectMenuBuilder()
            .WithCustomId($"{AssignBadgeSelectCustomIdPrefix}:{Context.User.Id}:{guildUser.Id}")
            .WithPlaceholder("Select the badge(s) to assign")
            .WithMinValues(1)
            .WithMaxValues(options.Count)
            .WithOptions(options);

        var components = new ComponentBuilder().WithSelectMenu(selectMenu).Build();
        var truncationNotice = assignableBadges.Count > BadgeSelectMaxOptions
            ? $"\n⚠️ Showing the first {BadgeSelectMaxOptions} of {assignableBadges.Count} badges — use `/admin badge assign` for the rest."
            : string.Empty;

        await Context.Interaction.FollowupAsync(
            $"Select which badge(s) to assign to **{guildUser.DisplayName}**:{truncationNotice}",
            components: components,
            ephemeral: true);
    }

    /// <summary>Badges the target does not already hold, ordered the way the badge list is.</summary>
    private async Task<List<Badge>> GetAssignableBadgesAsync(SocketGuildUser guildUser)
    {
        var ownedIds = (await BadgeService.GetUserBadges(guildUser, isAdmin: true))
            .Select(userBadge => userBadge.EnsureBadgeDetails().Id)
            .ToHashSet();

        return (await BadgeService.GetAllBadges(isAdmin: true))
            .Where(badge => !ownedIds.Contains(badge.Id))
            .OrderBy(badge => badge.Title, BadgeTitleNaturalComparer)
            .ThenBy(badge => badge.Id)
            .ToList();
    }

    [ComponentInteraction(AssignBadgeSelectCustomIdPrefix + ":*:*", true)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task HandleAssignBadgeSelect(string requesterIdRaw, string targetUserIdRaw, string[] selectedBadgeIds)
    {
        await Context.Interaction.DeferAsync(ephemeral: true);

        if (!ulong.TryParse(requesterIdRaw, out var requesterId) || Context.User.Id != requesterId)
        {
            await Context.Interaction.FollowupAsync("🚫 You are not authorized to use these controls.", ephemeral: true);
            return;
        }

        if (!ulong.TryParse(targetUserIdRaw, out var targetUserId) || Context.Guild?.GetUser(targetUserId) is not { } targetUser)
        {
            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = "❌ User not found.";
                msg.Components = new ComponentBuilder().Build();
            });
            return;
        }

        var awardedBy = Context.User as SocketGuildUser;
        if (awardedBy == null)
        {
            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = "❌ This command can only be used in a server.";
                msg.Components = new ComponentBuilder().Build();
            });
            return;
        }

        var assignedBadges = new List<string>();
        var failedBadges = new List<string>();

        foreach (var selectedBadgeId in selectedBadgeIds)
        {
            var badge = int.TryParse(selectedBadgeId, out var badgeId)
                ? await BadgeService.GetBadge(badgeId)
                : null;

            if (badge == null)
            {
                failedBadges.Add($"`{selectedBadgeId}`");
                continue;
            }

            if (await BadgeService.AssignBadgeToUser(targetUser, badge, awardedBy))
            {
                assignedBadges.Add(badge.Title);
            }
            else
            {
                failedBadges.Add(badge.Title);
            }
        }

        var summary = new StringBuilder();
        if (assignedBadges.Count > 0)
        {
            var badgeWord = assignedBadges.Count == 1 ? "badge" : "badges";
            summary.AppendLine($"🏆 Assigned {assignedBadges.Count} {badgeWord} to **{targetUser.DisplayName}**: {string.Join(", ", assignedBadges.Select(title => $"**{title}**"))}");
        }

        if (failedBadges.Count > 0)
        {
            summary.AppendLine($"⚠️ Could not assign: {string.Join(", ", failedBadges.Select(title => $"**{title}**"))}");
        }

        var result = summary.Length > 0 ? summary.ToString().TrimEnd() : "❌ No badges were assigned.";

        await Context.Interaction.ModifyOriginalResponseAsync(msg =>
        {
            msg.Content = result;
            msg.Components = new ComponentBuilder().Build();
        });
    }

    private async Task ShowUserBadgesForTarget(SocketGuildUser user)
    {

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

        const int maxEmbedTotalLength = 6000;
        var usedEmbedCharacters = $"🏆 {user.DisplayName}'s Badges".Length;
        var displayedBadges = 0;

        foreach (var userBadge in userBadges)
        {
            var badge = userBadge.EnsureBadgeDetails();
            if (string.IsNullOrWhiteSpace(badge.Title) || string.IsNullOrWhiteSpace(badge.Description))
            {
                continue;
            }

            if (displayedBadges >= BadgeListPageSize)
            {
                break;
            }

            var awardedByName = "Unknown";
            if (ulong.TryParse(userBadge.AwardedBy, out var awardedById))
            {
                var awardedBy = Context.Guild.GetUser(awardedById);
                awardedByName = awardedBy?.DisplayName ?? "Unknown";
            }

            var visibilityIndicator = isAdmin && !badge.IsPublic ? " 🔒" : string.Empty;
            var badgeIdInfo = isAdmin ? $" (ID: {badge.Id})" : string.Empty;
            var fieldName = $"{badge.Title}{visibilityIndicator}{badgeIdInfo}";
            var groupInfo = string.IsNullOrEmpty(badge.GroupKey) ? string.Empty : $"\n*Group: `{badge.GroupKey}`*";
            var sourceInfo = $"\n*Awarded by {awardedByName} on {userBadge.AwardedAt:yyyy-MM-dd}*";
            var fieldValue = $"{badge.Description}{groupInfo}{(isAdmin ? sourceInfo : string.Empty)}";

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
            displayedBadges++;
        }

        if (displayedBadges == 0)
        {
            await Context.Interaction.FollowupAsync($"📭 {user.Mention} has no displayable badges.", ephemeral: true);
            return;
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

        if (userBadges.Count > displayedBadges)
        {
            footerText += $" | Showing first {displayedBadges}";
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