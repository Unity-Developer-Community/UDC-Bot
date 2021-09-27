using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DiscordBot.Extensions
{
    public static class UserServiceExtensions
    {
        /// <summary>
        ///     Safely converts a double to an int without running into exceptions. Number will be reduced to limits of int value.
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        public static int ToInt(this double val)
        {
            if (val > int.MaxValue) return int.MaxValue;

            if (val < int.MinValue) return int.MinValue;

            return (int)val;
        }

        #region Cooldown related

        /// <summary>
        ///     Checks to see if user is on this cooldown list. User is automatically removed from list if their time is up and
        ///     will return false.
        /// </summary>
        /// <param name="cooldowns"></param>
        /// <param name="userId"></param>
        /// <param name="evenIfCooldownNowOver"></param>
        /// <returns></returns>
        public static bool HasUser(this Dictionary<ulong, DateTime> cooldowns, ulong userId, bool evenIfCooldownNowOver = false)
        {
            if (cooldowns.ContainsKey(userId))
            {
                if (cooldowns[userId] > DateTime.Now) return true;

                cooldowns.Remove(userId);

                if (evenIfCooldownNowOver)
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Adds user to cooldown list with given amount of time. If user already on list, time is added to existing time.
        /// </summary>
        /// <param name="cooldowns"></param>
        /// <param name="userId"></param>
        /// <param name="seconds"></param>
        /// <param name="minutes"></param>
        /// <param name="hours"></param>
        /// <param name="days"></param>
        /// <param name="ignoreExisting">Sets the cooldown time absolutely, instead of adding to existing.</param>
        public static void AddCooldown(this Dictionary<ulong, DateTime> cooldowns, ulong userId, int seconds = 0, int minutes = 0,
                                       int hours = 0, int days = 0, bool ignoreExisting = false)
        {
            var cooldownTime = new TimeSpan(days, hours, minutes, seconds);

            if (cooldowns.HasUser(userId))
            {
                if (ignoreExisting)
                {
                    cooldowns[userId] = DateTime.Now.Add(cooldownTime);
                    return;
                }

                cooldowns[userId] = cooldowns[userId].Add(cooldownTime);
                return;
            }

            cooldowns.Add(userId, DateTime.Now.Add(cooldownTime));
        }

        /// <summary>
        ///     Set a max days (permanent) cooldown for the given user, or removes the permanent cooldown if set false.
        /// </summary>
        /// <param name="cooldowns"></param>
        /// <param name="userId"></param>
        /// <param name="enabled">Set to true for permanent, or false to remove it.</param>
        public static void SetPermanent(this Dictionary<ulong, DateTime> cooldowns, ulong userId, bool enabled)
        {
            cooldowns.AddCooldown(userId, days: enabled ? 9999 : 0, ignoreExisting: true);
        }

        public static bool IsPermanent(this Dictionary<ulong, DateTime> cooldowns, ulong userId) => cooldowns.Days(userId) > 5000;

        public static double Days(this Dictionary<ulong, DateTime> cooldowns, ulong userId) => cooldowns.HasUser(userId) ? cooldowns[userId].Subtract(DateTime.Now).TotalDays : 0;

        public static double Hours(this Dictionary<ulong, DateTime> cooldowns, ulong userId) => cooldowns.HasUser(userId) ? cooldowns[userId].Subtract(DateTime.Now).TotalHours : 0;

        public static double Minutes(this Dictionary<ulong, DateTime> cooldowns, ulong userId) => cooldowns.HasUser(userId) ? cooldowns[userId].Subtract(DateTime.Now).TotalMinutes : 0;

        public static double Seconds(this Dictionary<ulong, DateTime> cooldowns, ulong userId) => cooldowns.HasUser(userId) ? cooldowns[userId].Subtract(DateTime.Now).TotalSeconds : 0;

        public static double Milliseconds(this Dictionary<ulong, DateTime> cooldowns, ulong userId) => cooldowns.HasUser(userId) ? cooldowns[userId].Subtract(DateTime.Now).TotalMilliseconds : 0;

        /// <summary>
        ///     Returns when the cooldown list no-longer contains the user.
        /// </summary>
        /// <param name="cooldowns"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        public static async Task AwaitCooldown(this Dictionary<ulong, DateTime> cooldowns, ulong userId)
        {
            while (cooldowns.HasUser(userId))
            {
                var delay = cooldowns.Milliseconds(userId).ToInt() + 100;
                if (delay > 0)
                    await Task.Delay(delay);
                else
                    await Task.Delay(1000);
            }
        }

        #endregion
    }
}