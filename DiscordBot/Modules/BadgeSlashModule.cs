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
        [Summary("public", "Whether the badge is public (default: true)")] bool isPublic = true)
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

        var createdBadge = await BadgeService.CreateBadge(title, description, isPublic);
        
        if (createdBadge != null)
        {
            var embed = new EmbedBuilder()
                .WithTitle("🏆 Badge Created Successfully")
                .WithDescription($"**{createdBadge.Title}**")
                .AddField("Description", createdBadge.Description)
                .AddField("Badge ID", createdBadge.Id.ToString())
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
        [Summary("public", "Whether the badge should be public")] bool isPublic = true)
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

        var updatedBadge = await BadgeService.UpdateBadge(existingBadge.Id, newTitle, newDescription, isPublic);
        
        if (updatedBadge != null)
        {
            var embed = new EmbedBuilder()
                .WithTitle("✏️ Badge Updated Successfully")
                .WithDescription($"**{updatedBadge.Title}**")
                .AddField("Description", updatedBadge.Description)
                .AddField("Badge ID", updatedBadge.Id.ToString())
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
            var badgeInfo = $"**{badge.Title}**{visibilityIndicator} (ID: {badge.Id})\n{badge.Description}\n\n";
            
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
            var badgeInfo = $"**{userBadge.Badge.Title}**{visibilityIndicator}\n{userBadge.Badge.Description}\n*Awarded by {awardedByName} on {userBadge.AwardedAt:yyyy-MM-dd}*\n\n";
            
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
}