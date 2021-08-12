﻿using System;
using System.Data.Common;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using DiscordBot.Extensions;
using Insight.Database;
using MySql.Data.MySqlClient;


namespace DiscordBot.Services
{
    public class DatabaseService
    {
        private readonly ILoggingService _logging;
        private string ConnectionString { get; }

        public IServerUserRepo Query() => _connection;
        private readonly IServerUserRepo _connection;

        public DatabaseService(ILoggingService logging, Settings.Deserialized.Settings settings)
        {
            ConnectionString = settings.DbConnectionString;
            _logging = logging;

            DbConnection c = null;
            try
            {
                c = new MySqlConnection(ConnectionString);
                _connection = c.As<IServerUserRepo>();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            try
            {
                _connection.TestConnection();
            }
            catch (Exception)
            {
                Console.WriteLine("Table 'users' does not exist, attempting to generate..");
                c.ExecuteSql(
                    $"CREATE TABLE `users` (`ID` int(11) UNSIGNED  NOT NULL, `UserID` varchar(32) COLLATE utf8mb4_unicode_ci NOT NULL, `Karma` int(11) UNSIGNED  NOT NULL DEFAULT 0, `KarmaWeekly` int(11) UNSIGNED  NOT NULL DEFAULT 0, `KarmaMonthly` int(11) UNSIGNED  NOT NULL DEFAULT 0, `KarmaYearly` int(11) UNSIGNED  NOT NULL DEFAULT 0, `KarmaGiven` int(11) UNSIGNED NOT NULL DEFAULT 0, `Exp` bigint(11) UNSIGNED  NOT NULL DEFAULT 0, `Level` int(11) UNSIGNED NOT NULL DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
                c.ExecuteSql($"ALTER TABLE `users` ADD PRIMARY KEY (`ID`,`UserID`), ADD UNIQUE KEY `UserID` (`UserID`)");
                c.ExecuteSql($"ALTER TABLE `users` MODIFY `ID` int(11) UNSIGNED NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=1");
            }
        }

        public async Task FullDbSync(IGuild guild, IUserMessage message)
        {
            string messageContent = message.Content + " ";
            var userList = await guild.GetUsersAsync(CacheMode.AllowDownload, RequestOptions.Default);
            await message.ModifyAsync(msg =>
            {
                if (msg != null) msg.Content = $"{messageContent}0/{userList.Count.ToString()}";
            });

            int counter = 0, newAdd = 0;
            var updater = Task.Run(function: async () =>
            {
                foreach (var user in userList)
                {
                    var member = await guild.GetUserAsync(user.Id);
                    if (!user.IsBot)
                    {
                        var userIdString = user.Id.ToString();
                        var serverUser = await Query().GetUser(userIdString);
                        if (serverUser == null)
                        {
                            await AddNewUser(user as SocketGuildUser);
                            newAdd++;
                        }
                    }
                    counter++;
                }
            });

            while (!updater.IsCompleted && !updater.IsCanceled)
            {
                await Task.Delay(1000);
                await message.ModifyAsync(properties =>
                {
                    if (properties != null)
                        properties.Content = $"{messageContent}{counter.ToString()}/{userList.Count.ToString()}";
                });
            }

            await _logging.LogAction(
                $"Database Synchronized {counter.ToString()} Users Successfully.\n{newAdd.ToString()} missing users added.");
        }

        public async Task AddNewUser(SocketGuildUser socketUser)
        {
            try
            {
                var user = await Query().GetUser(socketUser.Id.ToString());
                if (user != null)
                    return;

                user = new ServerUser
                {
                    UserID = socketUser.Id.ToString(),
                };

                await _connection.InsertUser(user);

                await _logging.LogAction(
                    $"User {socketUser.Username}#{socketUser.DiscriminatorValue.ToString()} succesfully added to the databse.",
                    true,
                    false);
            }
            catch (Exception e)
            {
                await _logging.LogAction(
                    $"Error when trying to add user {socketUser.Id.ToString()} to the database : {e}", true, false);
            }
        }

        public async Task DeleteUser(ulong id)
        {
            try
            {
                var user = await _connection.GetUser(id.ToString());
                if (user != null)
                    await _connection.RemoveUser(user.UserID);
            }
            catch (Exception e)
            {
                await _logging.LogAction($"Error when trying to delete user {id} from the database : {e}", true, false);
            }
        }

        public async Task<bool> UserExists(ulong id)
        {
            return (await Query().GetUser(id.ToString()) != null);
        }
    }
}