using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using MySql.Data.MySqlClient;

namespace DiscordBot.Services
{
    public class DatabaseService
    {
        private string _connection { get; }

        private readonly ILoggingService _logging;

        private readonly Settings.Deserialized.Settings _settings;

        public DatabaseService(ILoggingService logging, Settings.Deserialized.Settings settings)
        {
            _settings = settings;
            _connection = _settings.DbConnectionString;
            _logging = logging;

            using (var connection = new MySqlConnection(_connection))
            {
                try
                {
                    connection.Open();
                    ConsoleLogger.Log(message: "Connected to Database.", severity: Severity.Pass );
                    var command = new MySqlCommand($"SELECT COUNT(*) FROM `users`", connection);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read()) 
                        {
                            ConsoleLogger.Log(message: $"Database Test Pass: {reader} users stored.", severity: Severity.Pass );
                        }
                    }
                }
                catch (MySqlException ex)
                {
                    ConsoleLogger.Log(message: $"Error:\t{ex.Message}\nStack:\n{ex.StackTrace}", severity: Severity.Error );
                }
            }
        }

        /*
        **Publisher Stuff
        */
        public uint GetPublisherAdCount()
        {
            using (var connection = new MySqlConnection(_connection))
            {
                var command = new MySqlCommand("SELECT COUNT(*) FROM advertisment", connection);
                connection.Open();
                return Convert.ToUInt32(command.ExecuteScalar());
            }
        }

        public (uint pkgId, ulong userId) GetPublisherAd(uint id)
        {
            using (var connection = new MySqlConnection(_connection))
            {
                var command = new MySqlCommand($"Select username, userid, packageID FROM advertisment WHERE id='{id}'", connection);
                connection.Open();
                MySqlDataReader reader;
                using (reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        return (Convert.ToUInt32(reader["packageID"]), Convert.ToUInt64(reader["userid"]));
                    }
                }
            }

            return (0, 0);
        }

        public async Task AddPublisherPackage(string username, string discriminator, string userid, uint packageID)
        {
            try
            {
                using (var connection = new MySqlConnection(_connection))
                {
                    var command = new MySqlCommand(
                        $"INSERT INTO advertisment SET username='{username}', discriminator='{discriminator}', userid='{userid}', packageID='{packageID}', date='{DateTime.Now:yyyy-MM-dd HH:mm:ss}'",
                        connection);
                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception e)
            {
                await _logging.LogAction($"Error when trying to add package {packageID} from {username}#{discriminator} - {userid} : {e}");
            }
        }

        /*
        Update Service
        */
        public void UpdateUserRanks()
        {
            try
            {
                using (var connection = new MySqlConnection(_connection))
                {
                    var command = new MySqlCommand(
                        "SET @prev_value = NULL; SET @rank_count = 0; " +
                        "UPDATE users SET `rank` = @rank_count := IF(@prev_value = `rank`, @rank_count, @rank_count + 1) " +
                        "ORDER BY exp DESC", connection);
                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception e)
            {
                _logging.LogAction($"Error when trying to update ranks : {e}", true, false);
            }
        }


        public void AddUserXp(ulong id, int xp)
        {
            int oldXp;
            string reader = GetAttributeFromUser(id, "exp");

            oldXp = Convert.ToInt32(reader);
            UpdateAttributeFromUser(id, "exp", oldXp + xp);
        }

        public void AddUserLevel(ulong id, uint level)
        {
            uint oldLevel;
            string reader = GetAttributeFromUser(id, "level");

            oldLevel = Convert.ToUInt32(reader);
            UpdateAttributeFromUser(id, "level", oldLevel + level);
        }

        public void AddUserKarma(ulong id, int karma)
        {
            int oldKarma;
            string reader = GetAttributeFromUser(id, "karma");

            oldKarma = Convert.ToInt32(reader);
            UpdateAttributeFromUser(id, "karma", oldKarma + karma);
        }

        public uint GetUserXp(ulong id)
        {
            uint xp;
            string reader = GetAttributeFromUser(id, "exp");

            xp = Convert.ToUInt32(reader);

            return xp;
        }

        public int GetUserKarma(ulong id)
        {
            int karma;
            string reader = GetAttributeFromUser(id, "karma");

            karma = Convert.ToInt32(reader);

            return karma;
        }

        public uint GetUserRank(ulong id)
        {
            uint rank;
            string reader = GetAttributeFromUser(id, "rank");

            rank = Convert.ToUInt32(reader);

            return rank;
        }

        public uint GetUserLevel(ulong id)
        {
            uint level;
            string reader = GetAttributeFromUser(id, "level");

            level = Convert.ToUInt32(reader);

            return level;
        }

        public string GetUserJoinDate(ulong id)
        {
            return GetAttributeFromUser(id, "joinDate");
        }

        public void UpdateUserName(ulong id, string name)
        {
            UpdateAttributeFromUser(id, "username", name);
        }

        public void UpdateUserAvatar(ulong id, string avatar)
        {
            UpdateAttributeFromUser(id, "avatarUrl", avatar);
        }

        public void AddUserUdc(ulong id, int udc)
        {
            int oldUdc;
            string reader = GetAttributeFromUser(id, "udc");

            oldUdc = Convert.ToInt32(reader);
            UpdateAttributeFromUser(id, "udc", oldUdc + udc);
        }

        public int GetUserUdc(ulong id)
        {
            return Convert.ToInt32(GetAttributeFromUser(id, "udc"));
        }

        public uint GetUserKarmaRank(ulong id)
        {
            //int karma;

            using (var connection = new MySqlConnection(_connection))
            {
                var command = new MySqlCommand(
                    $"SELECT COUNT(1)+1 as `rank` FROM `users` WHERE karma > (SELECT karma FROM users WHERE userid='{id}')", connection);
                connection.Open();
                MySqlDataReader reader;
                using (reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        return (uint) System.Convert.ToInt32(reader["rank"]);
                    }
                }
            }

            return 0;
        }

        public List<(ulong userId, int level)> GetTopLevel()
        {
            List<(ulong userId, int level)> users = new List<(ulong userId, int level)>();

            using (var connection = new MySqlConnection(_connection))
            {
                var command = new MySqlCommand("SELECT userid, level FROM `users` ORDER BY exp DESC LIMIT 10", connection);
                connection.Open();
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    users.Add((reader.GetUInt64(0), reader.GetInt32(1)));
                }
            }


            return users;
        }

        public List<(ulong userId, int karma)> GetTopKarma()
        {
            List<(ulong userId, int karma)> users = new List<(ulong userId, int karma)>();

            using (var connection = new MySqlConnection(_connection))
            {
                var command = new MySqlCommand("SELECT userid, karma FROM `users` ORDER BY karma DESC LIMIT 10", connection);
                connection.Open();
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    users.Add((reader.GetUInt64(0), reader.GetInt32(1)));
                }
            }

            return users;
        }

        public List<(ulong userId, int udc)> GetTopUdc()
        {
            List<(ulong userId, int udc)> users = new List<(ulong userId, int udc)>();

            using (var connection = new MySqlConnection(_connection))
            {
                var command = new MySqlCommand("SELECT userid, udc FROM `users` ORDER BY udc DESC LIMIT 10", connection);
                connection.Open();
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    users.Add((reader.GetUInt64(0), reader.GetInt32(1)));
                }
            }

            return users;
        }

        public async void AddNewUser(SocketGuildUser user)
        {
            try
            {
                using (var connection = new MySqlConnection(_connection))
                {
                    var command = new MySqlCommand(
                        $"INSERT INTO users SET username=@Username, userid='{user.Id}', discriminator='{user.DiscriminatorValue}'," +
                        $"avatar='{user.AvatarId}', " +
                        $"avatarURL='{user.GetAvatarUrl()}'," +
                        $"bot='{(user.IsBot ? 1 : 0)}', status=@Status, joinDate='{DateTime.Now:yyyy-MM-dd HH:mm:ss}', udc=0", connection);
                    command.Parameters.AddWithValue("@Username", user.Username);
                    command.Parameters.AddWithValue("@Status", user.Status);
                    connection.Open();
                    command.ExecuteNonQuery();
                    await _logging.LogAction($"User {user.Username}#{user.DiscriminatorValue} succesfully added to the databse.",
                        true,
                        false);
                }
            }
            catch (Exception e)
            {
                await _logging.LogAction($"Error when trying to add user {user.Id} to the database : {e}", true, false);
            }
        }

        public async void DeleteUser(ulong id)
        {
            try
            {
                using (var connection = new MySqlConnection(_connection))
                {
                    var command = new MySqlCommand($"DELETE FROM users WHERE userid='{id}'", connection);
                    var command2 = new MySqlCommand($"INSERT users_remove SELECT * FROM users WHERE userid='{id}'", connection);
                    connection.Open();
                    command2.ExecuteNonQuery();
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception e)
            {
                await _logging.LogAction($"Error when trying to delete user {id} from the database : {e}", true, false);
            }
        }

        private async void UpdateAttributeFromUser(ulong id, string attribute, int value)
        {
            try
            {
                using (var connection = new MySqlConnection(_connection))
                {
                    var command = new MySqlCommand($"UPDATE users SET `{attribute}`=@Value WHERE userid='{id}'", connection);
                    command.Parameters.AddWithValue("@Value", value);
                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception e)
            {
                await _logging.LogAction($"Error when trying to edit attribute {attribute} from user {id} with value {value} : {e}",
                    true,
                    false);
            }
        }

        public async Task<bool> UserExists(ulong id)
        {
            try
            {
                using (var connection = new MySqlConnection(_connection))
                {
                    var command = new MySqlCommand($"SELECT * FROM users where userid='{id}'", connection);
                    connection.Open();
                    return (command.ExecuteScalar() != null);
                }
            }
            catch (Exception e)
            {
                await _logging.LogAction($"Error when trying to retrieve user {id} : {e}",
                    true,
                    false);
            }

            return false;
        }

        private async void UpdateAttributeFromUser(ulong id, string attribute, uint value)
        {
            try
            {
                using (var connection = new MySqlConnection(_connection))
                {
                    var command = new MySqlCommand($"UPDATE users SET `{attribute}`=@Value WHERE userid='{id}'", connection);
                    command.Parameters.AddWithValue("@Value", value);
                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception e)
            {
                await _logging.LogAction($"Error when trying to edit attribute {attribute} from user {id} with value {value} : {e}",
                    true,
                    false);
            }
        }

        private async void UpdateAttributeFromUser(ulong id, string attribute, string value)
        {
            try
            {
                value = MySqlHelper.EscapeString(value);
                using (var connection = new MySqlConnection(_connection))
                {
                    var command = new MySqlCommand($"UPDATE users SET `{attribute}`=@Value WHERE userid='{id}'", connection);
                    command.Parameters.AddWithValue("@Value", value);
                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception e)
            {
                await _logging.LogAction($"Error when trying to edit attribute {attribute} from user {id} with value {value} : {e}",
                    true,
                    false);
            }
        }

        private string GetAttributeFromUser(ulong id, string attribute)
        {
            try
            {
                using (var connection = new MySqlConnection(_connection))
                {
                    var command = new MySqlCommand($"Select `{attribute}` FROM users WHERE userid='{id}'", connection);
                    connection.Open();
                    MySqlDataReader reader;
                    using (reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            return reader[attribute].ToString();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logging.LogAction($"Error when trying to get attribute {attribute} from user {id} : {e}", true,
                    false);
            }

            return null;
        }
    }
}