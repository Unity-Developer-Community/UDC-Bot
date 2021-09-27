using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Insight.Database;

namespace DiscordBot.Extensions
{
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
    }

    public interface IServerUserRepo
    {
        [Sql("INSERT INTO users (UserID) VALUES (@UserID)")]
        Task InsertUser(ServerUser user);
        [Sql("DELETE FROM users WHERE UserID = @userId")]
        Task RemoveUser(string userId);

        [Sql("SELECT * FROM users WHERE UserID = @userId")]
        Task<ServerUser> GetUser(string userId);

        // Rank Stuff
        [Sql("SELECT UserID, Karma, Level, Exp FROM users ORDER BY Level DESC, RAND() LIMIT @n")]
        Task<IList<ServerUser>> GetTopLevel(int n);
        [Sql("SELECT UserID, Karma, KarmaGiven FROM users ORDER BY Karma DESC, RAND() LIMIT @n")]
        Task<IList<ServerUser>> GetTopKarma(int n);
        [Sql("SELECT UserID, KarmaWeekly FROM users ORDER BY KarmaWeekly DESC, RAND() LIMIT @n")]
        Task<IList<ServerUser>> GetTopKarmaWeekly(int n);
        [Sql("SELECT UserID, KarmaMonthly FROM users ORDER BY KarmaMonthly DESC, RAND() LIMIT @n")]
        Task<IList<ServerUser>> GetTopKarmaMonthly(int n);
        [Sql("SELECT UserID, KarmaYearly FROM users ORDER BY KarmaYearly DESC, RAND() LIMIT @n")]
        Task<IList<ServerUser>> GetTopKarmaYearly(int n);
        [Sql("SELECT COUNT(UserID)+1 FROM users WHERE Level > @level")]
        Task<long> GetLevelRank(string userId, uint level);
        [Sql("SELECT COUNT(UserID)+1 FROM users WHERE Karma > @karma")]
        Task<long> GetKarmaRank(string userId, uint karma);

        // Update Values
        [Sql("UPDATE users SET Karma = @karma WHERE UserID = @userId")]
        Task UpdateKarma(string userId, uint karma);
        [Sql("UPDATE users SET Karma = Karma + 1, KarmaWeekly = KarmaWeekly + 1, KarmaMonthly = KarmaMonthly + 1, KarmaYearly = KarmaYearly + 1 WHERE UserID = @userId")]
        Task IncrementKarma(string userId);
        [Sql("UPDATE users SET KarmaGiven = @karmaGiven WHERE UserID = @userId")]
        Task UpdateKarmaGiven(string userId, uint karmaGiven);
        [Sql("UPDATE users SET Exp = @xp WHERE UserID = @userId")]
        Task UpdateXp(string userId, ulong xp);
        [Sql("UPDATE users SET Level = @level WHERE UserID = @userId")]
        Task UpdateLevel(string userId, uint level);

        // Get Single Values
        [Sql("SELECT Karma FROM users WHERE UserID = @userId")]
        Task<uint> GetKarma(string userId);
        [Sql("SELECT KarmaGiven FROM users WHERE UserID = @userId")]
        Task<uint> GetKarmaGiven(string userId);
        [Sql("SELECT Exp FROM users WHERE UserID = @userId")]
        Task<ulong> GetXp(string userId);
        [Sql("SELECT Level FROM users WHERE UserID = @userId")]
        Task<uint> GetLevel(string userId);

        /// <summary>Returns a count of users in the Table, otherwise it fails. </summary>
        [Sql("SELECT COUNT(*) FROM users")]
        long TestConnection();
    }
}