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

    /// <summary>
    /// Creates a new badge with the specified title and description.
    /// </summary>
    public async Task<Badge> CreateBadge(string title, string description)
    {
        try
        {
            var existingBadge = await _databaseService.BadgeQuery.GetBadgeByTitle(title);
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
                CreatedAt = DateTime.UtcNow
            };

            var createdBadge = await _databaseService.BadgeQuery.CreateBadge(badge);
            
            await _logging.Log(LogBehaviour.File,
                $"Badge '{title}' created successfully with ID {createdBadge.Id}.", ExtendedLogSeverity.Positive);
            
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
    /// Gets all badges from the database.
    /// </summary>
    public async Task<IList<Badge>> GetAllBadges()
    {
        try
        {
            return await _databaseService.BadgeQuery.GetAllBadges();
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
            return await _databaseService.BadgeQuery.GetBadge(badgeId);
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
            return await _databaseService.BadgeQuery.GetBadgeByTitle(title);
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
            // Check if user already has this badge
            var hasCount = await _databaseService.BadgeQuery.CheckUserHasBadge(user.Id.ToString(), badge.Id);
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

            var result = await _databaseService.BadgeQuery.AssignBadgeToUser(userBadge);
            
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
            // Check if user has this badge
            var hasCount = await _databaseService.BadgeQuery.CheckUserHasBadge(user.Id.ToString(), badge.Id);
            if (hasCount == 0)
            {
                await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                    $"User {user.GetPreferredAndUsername()} does not have badge '{badge.Title}'.", ExtendedLogSeverity.Warning);
                return false;
            }

            await _databaseService.BadgeQuery.RemoveBadgeFromUser(user.Id.ToString(), badge.Id);
            
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
    public async Task<IList<UserBadge>> GetUserBadges(SocketGuildUser user)
    {
        if (user == null)
            return new List<UserBadge>();

        try
        {
            return await _databaseService.BadgeQuery.GetUserBadges(user.Id.ToString());
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Error retrieving badges for user {user.GetPreferredAndUsername()}: {e}", ExtendedLogSeverity.Error);
            return new List<UserBadge>();
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
            return await _databaseService.BadgeQuery.GetBadgeHolders(badge.Id);
        }
        catch (Exception e)
        {
            await _logging.Log(LogBehaviour.ConsoleChannelAndFile,
                $"Error retrieving holders for badge '{badge.Title}': {e}", ExtendedLogSeverity.Error);
            return new List<UserBadge>();
        }
    }
}