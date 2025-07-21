using Insight.Database;

namespace DiscordBot.Extensions;

public class Badge
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserBadge
{
    public int Id { get; set; }
    public string UserID { get; set; }
    public int BadgeId { get; set; }
    public DateTime AwardedAt { get; set; }
    public string AwardedBy { get; set; }
    
    // Navigation properties for joined queries
    public Badge Badge { get; set; }
}

/// <summary>
/// Table Properties for Badge. Intended to be used with IBadgeRepo and enforce consistency.
/// </summary>
public static class BadgeProps
{
    public const string TableName = "badges";
    
    public const string Id = nameof(Badge.Id);
    public const string Title = nameof(Badge.Title);
    public const string Description = nameof(Badge.Description);
    public const string CreatedAt = nameof(Badge.CreatedAt);
}

/// <summary>
/// Table Properties for UserBadge. Intended to be used with IBadgeRepo and enforce consistency.
/// </summary>
public static class UserBadgeProps
{
    public const string TableName = "user_badges";
    
    public const string Id = nameof(UserBadge.Id);
    public const string UserID = nameof(UserBadge.UserID);
    public const string BadgeId = nameof(UserBadge.BadgeId);
    public const string AwardedAt = nameof(UserBadge.AwardedAt);
    public const string AwardedBy = nameof(UserBadge.AwardedBy);
}

public interface IBadgeRepo
{
    #region Badge Management
    
    [Sql($@"
    INSERT INTO {BadgeProps.TableName} ({BadgeProps.Title}, {BadgeProps.Description}, {BadgeProps.CreatedAt}) 
    VALUES (@{BadgeProps.Title}, @{BadgeProps.Description}, @{BadgeProps.CreatedAt});
    SELECT * FROM {BadgeProps.TableName} WHERE {BadgeProps.Id} = LAST_INSERT_ID()")]
    Task<Badge> CreateBadge(Badge badge);
    
    [Sql($"SELECT * FROM {BadgeProps.TableName} ORDER BY {BadgeProps.Title}")]
    Task<IList<Badge>> GetAllBadges();
    
    [Sql($"SELECT * FROM {BadgeProps.TableName} WHERE {BadgeProps.Id} = @badgeId")]
    Task<Badge> GetBadge(int badgeId);
    
    [Sql($"SELECT * FROM {BadgeProps.TableName} WHERE {BadgeProps.Title} = @title")]
    Task<Badge> GetBadgeByTitle(string title);
    
    [Sql($"DELETE FROM {BadgeProps.TableName} WHERE {BadgeProps.Id} = @badgeId")]
    Task DeleteBadge(int badgeId);
    
    #endregion // Badge Management
    
    #region User Badge Management
    
    [Sql($@"
    INSERT INTO {UserBadgeProps.TableName} ({UserBadgeProps.UserID}, {UserBadgeProps.BadgeId}, {UserBadgeProps.AwardedAt}, {UserBadgeProps.AwardedBy}) 
    VALUES (@{UserBadgeProps.UserID}, @{UserBadgeProps.BadgeId}, @{UserBadgeProps.AwardedAt}, @{UserBadgeProps.AwardedBy});
    SELECT * FROM {UserBadgeProps.TableName} WHERE {UserBadgeProps.Id} = LAST_INSERT_ID()")]
    Task<UserBadge> AssignBadgeToUser(UserBadge userBadge);
    
    [Sql($"DELETE FROM {UserBadgeProps.TableName} WHERE {UserBadgeProps.UserID} = @userId AND {UserBadgeProps.BadgeId} = @badgeId")]
    Task RemoveBadgeFromUser(string userId, int badgeId);
    
    [Sql($@"
    SELECT ub.*, b.{BadgeProps.Title}, b.{BadgeProps.Description}, b.{BadgeProps.CreatedAt}
    FROM {UserBadgeProps.TableName} ub
    JOIN {BadgeProps.TableName} b ON ub.{UserBadgeProps.BadgeId} = b.{BadgeProps.Id}
    WHERE ub.{UserBadgeProps.UserID} = @userId
    ORDER BY ub.{UserBadgeProps.AwardedAt} DESC")]
    Task<IList<UserBadge>> GetUserBadges(string userId);
    
    [Sql($@"
    SELECT COUNT(*) FROM {UserBadgeProps.TableName}
    WHERE {UserBadgeProps.UserID} = @userId AND {UserBadgeProps.BadgeId} = @badgeId")]
    Task<int> CheckUserHasBadge(string userId, int badgeId);
    
    [Sql($@"
    SELECT ub.{UserBadgeProps.UserID}, ub.{UserBadgeProps.AwardedAt}, ub.{UserBadgeProps.AwardedBy}
    FROM {UserBadgeProps.TableName} ub
    WHERE ub.{UserBadgeProps.BadgeId} = @badgeId
    ORDER BY ub.{UserBadgeProps.AwardedAt} DESC")]
    Task<IList<UserBadge>> GetBadgeHolders(int badgeId);
    
    #endregion // User Badge Management
    
    /// <summary>Returns a count of badges in the Table, used for testing connection. </summary>
    [Sql($"SELECT COUNT(*) FROM {BadgeProps.TableName}")]
    Task<long> TestBadgeConnection();
}