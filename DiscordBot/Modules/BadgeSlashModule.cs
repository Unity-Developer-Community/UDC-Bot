using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Extensions;
using DiscordBot.Services;

namespace DiscordBot.Modules;

[Group("badge", "Badge management commands")]
[RequireUserPermission(GuildPermission.Administrator)]
public class BadgeSlashModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Dependency Injection

    public BadgeService BadgeService { get; set; }
    public ILoggingService LoggingService { get; set; }

    #endregion

    [SlashCommand("create", "Create a new badge")]
    public async Task CreateBadge(
        [Summary("title", "The title of the badge")] string title,
        [Summary("description", "The description of the badge")] string description)
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

        var createdBadge = await BadgeService.CreateBadge(title, description);
        
        if (createdBadge != null)
        {
            var embed = new EmbedBuilder()
                .WithTitle("🏆 Badge Created Successfully")
                .WithDescription($"**{createdBadge.Title}**")
                .AddField("Description", createdBadge.Description)
                .AddField("Badge ID", createdBadge.Id.ToString())
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

    [SlashCommand("assign", "Assign a badge to a user")]
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
        await Context.Interaction.DeferAsync(ephemeral: true);

        var badges = await BadgeService.GetAllBadges();
        
        if (!badges.Any())
        {
            await Context.Interaction.FollowupAsync("📭 No badges have been created yet.", ephemeral: true);
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
            var badgeInfo = $"**{badge.Title}** (ID: {badge.Id})\n{badge.Description}\n\n";
            
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

        embed.WithFooter($"Total badges: {badges.Count}");

        await Context.Interaction.FollowupAsync(embed: embed.Build(), ephemeral: true);
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

        var userBadges = await BadgeService.GetUserBadges(user);
        
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
            var awardedBy = Context.Guild.GetUser(Convert.ToUInt64(userBadge.AwardedBy));
            var awardedByName = awardedBy?.DisplayName ?? "Unknown";
            
            var badgeInfo = $"**{userBadge.Badge.Title}**\n{userBadge.Badge.Description}\n*Awarded by {awardedByName} on {userBadge.AwardedAt:yyyy-MM-dd}*\n\n";
            
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

        embed.WithFooter($"Total badges: {userBadges.Count}");

        await Context.Interaction.FollowupAsync(embed: embed.Build(), ephemeral: true);
    }
}