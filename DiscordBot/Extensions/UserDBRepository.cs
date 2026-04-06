using Insight.Database;

namespace DiscordBot.Extensions;

public class ServerUser
{
    // ReSharper disable once InconsistentNaming
    public string UserID { get; set; }
    public int Karma { get; set; }
    public int KarmaWeekly { get; set; }
    public int KarmaMonthly { get; set; }
    public int KarmaYearly { get; set; }
    public int KarmaGiven { get; set; }
    public long Exp { get; set; }
    public int Level { get; set; }
    // DefaultCity - Optional Location for Weather, BDay, Temp, Time, etc. (Added - Jan 2024)
    public string DefaultCity { get; set; } = string.Empty;
}

/// <summary>
/// Table Properties for ServerUser. Intended to be used with IServerUserRepo and enforce consistency and reduce errors.
/// </summary>
public static class UserProps
{
    public const string TableName = "users";

    public const string UserID = nameof(ServerUser.UserID);
    public const string Karma = nameof(ServerUser.Karma);
    public const string KarmaWeekly = nameof(ServerUser.KarmaWeekly);
    public const string KarmaMonthly = nameof(ServerUser.KarmaMonthly);
    public const string KarmaYearly = nameof(ServerUser.KarmaYearly);
    public const string KarmaGiven = nameof(ServerUser.KarmaGiven);
    public const string Exp = nameof(ServerUser.Exp);
    public const string Level = nameof(ServerUser.Level);
    public const string DefaultCity = nameof(ServerUser.DefaultCity);
}

public interface IServerUserRepo
{
    [Sql($@"
    INSERT INTO {UserProps.TableName} ({UserProps.UserID}) VALUES (@{UserProps.UserID})
    RETURNING *")]
    Task<ServerUser> InsertUser(ServerUser user);
    [Sql($"DELETE FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task RemoveUser(string userId);

    [Sql($"SELECT * FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task<ServerUser> GetUser(string userId);

    #region Ranks

    [Sql($"SELECT {UserProps.UserID}, {UserProps.Karma}, {UserProps.Level}, {UserProps.Exp} FROM {UserProps.TableName} ORDER BY {UserProps.Level} DESC, RANDOM() LIMIT @n")]
    Task<IList<ServerUser>> GetTopLevel(int n);
    [Sql($"SELECT {UserProps.UserID}, {UserProps.Karma}, {UserProps.KarmaGiven} FROM {UserProps.TableName} ORDER BY {UserProps.Karma} DESC, RANDOM() LIMIT @n")]
    Task<IList<ServerUser>> GetTopKarma(int n);
    [Sql($"SELECT {UserProps.UserID}, {UserProps.KarmaWeekly} FROM {UserProps.TableName} ORDER BY {UserProps.KarmaWeekly} DESC, RANDOM() LIMIT @n")]
    Task<IList<ServerUser>> GetTopKarmaWeekly(int n);
    [Sql($"SELECT {UserProps.UserID}, {UserProps.KarmaMonthly} FROM {UserProps.TableName} ORDER BY {UserProps.KarmaMonthly} DESC, RANDOM() LIMIT @n")]
    Task<IList<ServerUser>> GetTopKarmaMonthly(int n);
    [Sql($"SELECT {UserProps.UserID}, {UserProps.KarmaYearly} FROM {UserProps.TableName} ORDER BY {UserProps.KarmaYearly} DESC, RANDOM() LIMIT @n")]
    Task<IList<ServerUser>> GetTopKarmaYearly(int n);
    [Sql($"SELECT COUNT({UserProps.UserID})+1 FROM {UserProps.TableName} WHERE {UserProps.Level} > @level")]
    Task<long> GetLevelRank(string userId, int level);
    [Sql($"SELECT COUNT({UserProps.UserID})+1 FROM {UserProps.TableName} WHERE {UserProps.Karma} > @karma")]
    Task<long> GetKarmaRank(string userId, int karma);

    #endregion // Ranks

    #region Update Values

    [Sql($"UPDATE {UserProps.TableName} SET {UserProps.Karma} = @karma WHERE {UserProps.UserID} = @userId")]
    Task UpdateKarma(string userId, int karma);
    [Sql($"UPDATE {UserProps.TableName} SET {UserProps.Karma} = {UserProps.Karma} + 1, {UserProps.KarmaWeekly} = {UserProps.KarmaWeekly} + 1, {UserProps.KarmaMonthly} = {UserProps.KarmaMonthly} + 1, {UserProps.KarmaYearly} = {UserProps.KarmaYearly} + 1 WHERE {UserProps.UserID} = @userId")]
    Task IncrementKarma(string userId);
    [Sql($"UPDATE {UserProps.TableName} SET {UserProps.KarmaGiven} = @karmaGiven WHERE {UserProps.UserID} = @userId")]
    Task UpdateKarmaGiven(string userId, int karmaGiven);
    [Sql($"UPDATE {UserProps.TableName} SET {UserProps.Exp} = @xp WHERE {UserProps.UserID} = @userId")]
    Task UpdateXp(string userId, long xp);
    [Sql($"UPDATE {UserProps.TableName} SET {UserProps.Level} = @level WHERE {UserProps.UserID} = @userId")]
    Task UpdateLevel(string userId, int level);
    [Sql($"UPDATE {UserProps.TableName} SET {UserProps.DefaultCity} = @city WHERE {UserProps.UserID} = @userId")]
    Task UpdateDefaultCity(string userId, string city);

    #endregion // Update Values

    #region Get Single Values

    [Sql($"SELECT {UserProps.Karma} FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task<int> GetKarma(string userId);
    [Sql($"SELECT {UserProps.KarmaGiven} FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task<int> GetKarmaGiven(string userId);
    [Sql($"SELECT {UserProps.Exp} FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task<long> GetXp(string userId);
    [Sql($"SELECT {UserProps.Level} FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task<int> GetLevel(string userId);
    [Sql($"SELECT {UserProps.DefaultCity} FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task<string> GetDefaultCity(string userId);

    #endregion // Get Single Values

    /// <summary>Returns a count of {Props.TableName} in the Table, otherwise it fails. </summary>
    [Sql($"SELECT COUNT(*) FROM {UserProps.TableName}")]
    Task<long> TestConnection();
}