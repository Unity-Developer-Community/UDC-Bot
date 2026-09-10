using Discord.WebSocket;
using DiscordBot.Extensions;
using DiscordBot.Services;

namespace DiscordBot.Services;

public class BadgeService
{
    private const string ServiceName = "BadgeService";
    
    private readonly ILoggingService _logging;
    private readonly DatabaseService _databaseService;

    public BadgeService(ILoggingService logging, DatabaseService databaseService)
    {
        _logging = logging;
        _databaseService = databaseService;
    }

    public static string? NormalizeGroupKey(string? groupKey)
    {
        if (string.IsNullOrWhiteSpace(groupKey))
            return null;

        return groupKey.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Creates a new badge with the specified title and description.
    /// </summary>
    public async Task<Badge> CreateBadge(string title, string description, bool isPublic = true, string? groupKey = null)
    {
        try
        {
            var badgeQuery = _databaseService.BadgeQuery;
            if (badgeQuery == null)
                return null;

            var existingBadge = await badgeQuery.GetBadgeByTitle(title);
            if (existingBadge != null)
            {
                await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                    $"Badge creation failed: Badge with title '{title}' already exists.", ExtendedLogSeverity.Warning);
                return null;
            }

            var badge = new Badge
            {
                Title = title,
                Description = description,
                GroupKey = NormalizeGroupKey(groupKey),
                IsPublic = isPublic,
                CreatedAt = DateTime.UtcNow
            };

            var createdBadge = await badgeQuery.CreateBadge(badge);
            
            await _logging.Log(LogBehaviour.File,
                $"Badge '{title}' created successfully with ID {createdBadge.Id} (Public: {isPublic}, Group: {createdBadge.GroupKey ?? "none"}).", ExtendedLogSeverity.Positive);
            
            return createdBadge;
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Error creating badge '{title}': {e}", ExtendedLogSeverity.Error);
            return null;
        }
    }

    /// <summary>
    /// Updates an existing badge with new title, description, and/or visibility.
    /// </summary>
    public async Task<Badge> UpdateBadge(int badgeId, string title, string description, bool isPublic, string? groupKey = null, bool updateGroup = false)
    {
        try
        {
            var badgeQuery = _databaseService.BadgeQuery;
            if (badgeQuery == null)
                return null;

            var existingBadge = await badgeQuery.GetBadge(badgeId);
            if (existingBadge == null)
            {
                await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                    $"Badge update failed: Badge with ID {badgeId} not found.", ExtendedLogSeverity.Warning);
                return null;
            }

            // Check if title conflicts with another badge (if title is being changed)
            if (existingBadge.Title != title)
            {
                var conflictingBadge = await badgeQuery.GetBadgeByTitle(title);
                if (conflictingBadge != null && conflictingBadge.Id != badgeId)
                {
                    await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                        $"Badge update failed: Badge with title '{title}' already exists.", ExtendedLogSeverity.Warning);
                    return null;
                }
            }

            existingBadge.Title = title;
            existingBadge.Description = description;
            if (updateGroup)
                existingBadge.GroupKey = NormalizeGroupKey(groupKey);
            existingBadge.IsPublic = isPublic;

            await badgeQuery.UpdateBadge(existingBadge);
            
            await _logging.Log(LogBehaviour.File,
                $"Badge ID {badgeId} updated successfully: '{title}' (Public: {isPublic}, Group: {existingBadge.GroupKey ?? "none"}).", ExtendedLogSeverity.Positive);
            
            return existingBadge;
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Error updating badge ID {badgeId}: {e}", ExtendedLogSeverity.Error);
            return null;
        }
    }

    /// <summary>
    /// Gets all badges from the database.
    /// </summary>
    public async Task<IList<Badge>> GetAllBadges(bool isAdmin = false)
    {
        try
        {
            if (isAdmin)
            {
                var badgeQuery = _databaseService.BadgeQuery;
                return badgeQuery == null ? new List<Badge>() : await badgeQuery.GetAllBadges();
            }
            else
            {
                var badgeQuery = _databaseService.BadgeQuery;
                return badgeQuery == null ? new List<Badge>() : await badgeQuery.GetPublicBadges();
            }
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Error retrieving badges: {e}", ExtendedLogSeverity.Error);
            return new List<Badge>();
        }
    }

    /// <summary>
    /// Gets a badge by its ID.
    /// </summary>
    public async Task<Badge> GetBadge(int badgeId)
    {
        try
        {
            var badgeQuery = _databaseService.BadgeQuery;
            return badgeQuery == null ? null : await badgeQuery.GetBadge(badgeId);
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Error retrieving badge {badgeId}: {e}", ExtendedLogSeverity.Error);
            return null;
        }
    }

    /// <summary>
    /// Gets a badge by its title.
    /// </summary>
    public async Task<Badge> GetBadgeByTitle(string title)
    {
        try
        {
            var badgeQuery = _databaseService.BadgeQuery;
            return badgeQuery == null ? null : await badgeQuery.GetBadgeByTitle(title);
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Error retrieving badge '{title}': {e}", ExtendedLogSeverity.Error);
            return null;
        }
    }

    /// <summary>
    /// Assigns a badge to a user.
    /// </summary>
    public async Task<bool> AssignBadgeToUser(SocketGuildUser user, Badge badge, SocketGuildUser awardedBy)
    {
        if (user == null || badge == null || awardedBy == null)
            return false;

        try
        {
            var badgeQuery = _databaseService.BadgeQuery;
            if (badgeQuery == null)
                return false;

            // Check if user already has this badge
            var hasCount = await badgeQuery.CheckUserHasBadge(user.Id.ToString(), badge.Id);
            if (hasCount > 0)
            {
                await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                    $"User {user.GetPreferredAndUsername()} already has badge '{badge.Title}'.", ExtendedLogSeverity.Warning);
                return false;
            }

            var userBadge = new UserBadge
            {
                UserID = user.Id.ToString(),
                BadgeId = badge.Id,
                AwardedAt = DateTime.UtcNow,
                AwardedBy = awardedBy.Id.ToString()
            };

            var result = await badgeQuery.AssignBadgeToUser(userBadge);
            
            if (result != null)
            {
                await _logging.Log(LogBehaviour.File,
                    $"Badge '{badge.Title}' assigned to user {user.GetPreferredAndUsername()} by {awardedBy.GetPreferredAndUsername()}.", 
                    ExtendedLogSeverity.Positive);
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Error assigning badge '{badge.Title}' to user {user.GetPreferredAndUsername()}: {e}", 
                ExtendedLogSeverity.Error);
            return false;
        }
    }

    /// <summary>
    /// Removes a badge from a user.
    /// </summary>
    public async Task<bool> RemoveBadgeFromUser(SocketGuildUser user, Badge badge)
    {
        if (user == null || badge == null)
            return false;

        try
        {
            var badgeQuery = _databaseService.BadgeQuery;
            if (badgeQuery == null)
                return false;

            // Check if user has this badge
            var hasCount = await badgeQuery.CheckUserHasBadge(user.Id.ToString(), badge.Id);
            if (hasCount == 0)
            {
                await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                    $"User {user.GetPreferredAndUsername()} does not have badge '{badge.Title}'.", ExtendedLogSeverity.Warning);
                return false;
            }

            await badgeQuery.RemoveBadgeFromUser(user.Id.ToString(), badge.Id);
            
            await _logging.Log(LogBehaviour.File,
                $"Badge '{badge.Title}' removed from user {user.GetPreferredAndUsername()}.", 
                ExtendedLogSeverity.Positive);
            
            return true;
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Error removing badge '{badge.Title}' from user {user.GetPreferredAndUsername()}: {e}", 
                ExtendedLogSeverity.Error);
            return false;
        }
    }

    /// <summary>
    /// Gets all badges for a specific user.
    /// </summary>
    public async Task<IList<UserBadge>> GetUserBadges(SocketGuildUser user, bool isAdmin = false)
    {
        if (user == null)
            return new List<UserBadge>();

        try
        {
            if (isAdmin)
            {
                var badgeQuery = _databaseService.BadgeQuery;
                return badgeQuery == null ? new List<UserBadge>() : await badgeQuery.GetUserBadges(user.Id.ToString());
            }
            else
            {
                var badgeQuery = _databaseService.BadgeQuery;
                return badgeQuery == null ? new List<UserBadge>() : await badgeQuery.GetUserPublicBadges(user.Id.ToString());
            }
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Error retrieving badges for user {user.GetPreferredAndUsername()}: {e}", ExtendedLogSeverity.Error);
            return new List<UserBadge>();
        }
    }

    /// <summary>
    /// Gets the badge leaderboard, optionally filtered by group.
    /// </summary>
    public async Task<IList<BadgeLeaderboardEntry>> GetBadgeLeaderboard(bool isAdmin = false, string? groupKey = null, int limit = 10)
    {
        try
        {
            var badgeQuery = _databaseService.BadgeQuery;
            if (badgeQuery == null)
                return new List<BadgeLeaderboardEntry>();

            var normalizedGroupKey = NormalizeGroupKey(groupKey);
            if (string.IsNullOrEmpty(normalizedGroupKey))
            {
                return isAdmin
                    ? await badgeQuery.GetBadgeLeaderboard(limit)
                    : await badgeQuery.GetPublicBadgeLeaderboard(limit);
            }

            return isAdmin
                ? await badgeQuery.GetBadgeLeaderboardByGroup(normalizedGroupKey, limit)
                : await badgeQuery.GetPublicBadgeLeaderboardByGroup(normalizedGroupKey, limit);
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Error retrieving badge leaderboard for group '{groupKey ?? "all"}': {e}", ExtendedLogSeverity.Error);
            return new List<BadgeLeaderboardEntry>();
        }
    }

    /// <summary>
    /// Gets all users who have a specific badge.
    /// </summary>
    public async Task<IList<UserBadge>> GetBadgeHolders(Badge badge)
    {
        if (badge == null)
            return new List<UserBadge>();

        try
        {
            var badgeQuery = _databaseService.BadgeQuery;
            return badgeQuery == null ? new List<UserBadge>() : await badgeQuery.GetBadgeHolders(badge.Id);
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Error retrieving holders for badge '{badge.Title}': {e}", ExtendedLogSeverity.Error);
            return new List<UserBadge>();
        }
    }

    /// <summary>
    /// Checks if the user has administrator permissions.
    /// </summary>
    public bool IsUserAdmin(SocketGuildUser user)
    {
        return user?.GuildPermissions.Administrator == true;
    }
}