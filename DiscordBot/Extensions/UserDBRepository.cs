using Insight.Database;

namespace DiscordBot.Extensions;

public class ServerUser
{
    // ReSharper disable once InconsistentNaming
    public string UserID { get; set; }
    public uint Karma { get; set; }
    public uint KarmaWeekly { get; set; }
    public uint KarmaMonthly { get; set; }
    public uint KarmaYearly { get; set; }
    public uint KarmaGiven { get; set; }
    public ulong Exp { get; set; }
    public uint Level { get; set; }
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
    [Sql($"INSERT INTO {UserProps.TableName} ({UserProps.UserID}) VALUES (@{UserProps.UserID})")]
    Task InsertUser(ServerUser user);
    [Sql($"DELETE FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task RemoveUser(string userId);

    [Sql($"SELECT * FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task<ServerUser> GetUser(string userId);

    #region Ranks
    
    [Sql($"SELECT {UserProps.UserID}, {UserProps.Karma}, {UserProps.Level}, {UserProps.Exp} FROM {UserProps.TableName} ORDER BY {UserProps.Level} DESC, RAND() LIMIT @n")]
    Task<IList<ServerUser>> GetTopLevel(int n);
    [Sql($"SELECT {UserProps.UserID}, {UserProps.Karma}, {UserProps.KarmaGiven} FROM {UserProps.TableName} ORDER BY {UserProps.Karma} DESC, RAND() LIMIT @n")]
    Task<IList<ServerUser>> GetTopKarma(int n);
    [Sql($"SELECT {UserProps.UserID}, {UserProps.KarmaWeekly} FROM {UserProps.TableName} ORDER BY {UserProps.KarmaWeekly} DESC, RAND() LIMIT @n")]
    Task<IList<ServerUser>> GetTopKarmaWeekly(int n);
    [Sql($"SELECT {UserProps.UserID}, {UserProps.KarmaMonthly} FROM {UserProps.TableName} ORDER BY {UserProps.KarmaMonthly} DESC, RAND() LIMIT @n")]
    Task<IList<ServerUser>> GetTopKarmaMonthly(int n);
    [Sql($"SELECT {UserProps.UserID}, {UserProps.KarmaYearly} FROM {UserProps.TableName} ORDER BY {UserProps.KarmaYearly} DESC, RAND() LIMIT @n")]
    Task<IList<ServerUser>> GetTopKarmaYearly(int n);
    [Sql($"SELECT COUNT({UserProps.UserID})+1 FROM {UserProps.TableName} WHERE {UserProps.Level} > @level")]
    Task<long> GetLevelRank(string userId, uint level);
    [Sql($"SELECT COUNT({UserProps.UserID})+1 FROM {UserProps.TableName} WHERE {UserProps.Karma} > @karma")]
    Task<long> GetKarmaRank(string userId, uint karma);
    
    #endregion // Ranks

    #region Update Values
    
    [Sql($"UPDATE {UserProps.TableName} SET {UserProps.Karma} = @karma WHERE {UserProps.UserID} = @userId")]
    Task UpdateKarma(string userId, uint karma);
    [Sql($"UPDATE {UserProps.TableName} SET {UserProps.Karma} = {UserProps.Karma} + 1, {UserProps.KarmaWeekly} = {UserProps.KarmaWeekly} + 1, {UserProps.KarmaMonthly} = {UserProps.KarmaMonthly} + 1, {UserProps.KarmaYearly} = {UserProps.KarmaYearly} + 1 WHERE {UserProps.UserID} = @userId")]
    Task IncrementKarma(string userId);
    [Sql($"UPDATE {UserProps.TableName} SET {UserProps.KarmaGiven} = @karmaGiven WHERE {UserProps.UserID} = @userId")]
    Task UpdateKarmaGiven(string userId, uint karmaGiven);
    [Sql($"UPDATE {UserProps.TableName} SET {UserProps.Exp} = @xp WHERE {UserProps.UserID} = @userId")]
    Task UpdateXp(string userId, ulong xp);
    [Sql($"UPDATE {UserProps.TableName} SET {UserProps.Level} = @level WHERE {UserProps.UserID} = @userId")]
    Task UpdateLevel(string userId, uint level);
    [Sql($"UPDATE {UserProps.TableName} SET {UserProps.DefaultCity} = @city WHERE {UserProps.UserID} = @userId")]
    Task UpdateDefaultCity(string userId, string city);
    
    #endregion // Update Values

    #region Get Single Values
    
    [Sql($"SELECT {UserProps.Karma} FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task<uint> GetKarma(string userId);
    [Sql($"SELECT {UserProps.KarmaGiven} FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task<uint> GetKarmaGiven(string userId);
    [Sql($"SELECT {UserProps.Exp} FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task<ulong> GetXp(string userId);
    [Sql($"SELECT {UserProps.Level} FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task<uint> GetLevel(string userId);
    [Sql($"SELECT {UserProps.DefaultCity} FROM {UserProps.TableName} WHERE {UserProps.UserID} = @userId")]
    Task<string> GetDefaultCity(string userId);
    
    #endregion // Get Single Values

    /// <summary>Returns a count of {Props.TableName} in the Table, otherwise it fails. </summary>
    [Sql($"SELECT COUNT(*) FROM {UserProps.TableName}")]
    Task<long> TestConnection();
}