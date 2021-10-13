using System;
using System.Collections.Generic;
using System.Text;
using Discord;

namespace DiscordBot.Utils
{
    public static class Utils
    {
        public static string FormatTime(uint seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);
            if (span.TotalSeconds == 0) return "0 seconds";

            var parts = new List<string>();
            if (span.Days > 0) parts.Add($"{span.Days} day{(span.Days > 1 ? "s" : "")}");

            if (span.Hours > 0) parts.Add($"{span.Hours} hour{(span.Hours > 1 ? "s" : "")}");

            if (span.Minutes > 0) parts.Add($"{span.Minutes} minute{(span.Minutes > 1 ? "s" : "")}");

            if (span.Seconds > 0) parts.Add($"{span.Seconds} second{(span.Seconds > 1 ? "s" : "")}");

            var finishedTime = string.Empty;
            for (var i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                {
                    if (i == parts.Count - 1)
                        finishedTime += " and ";
                    else
                        finishedTime += ", ";
                }

                finishedTime += parts[i];
            }

            return finishedTime;
        }

        /// <summary>
        ///     Sanitize XML, from https://seattlesoftware.wordpress.com/2008/09/11/hexadecimal-value-0-is-an-invalid-character/
        /// </summary>
        /// <param name="xml"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static string SanitizeXml(string xml)
        {
            if (xml == null) throw new ArgumentNullException("xml");

            var buffer = new StringBuilder(xml.Length);

            foreach (var c in xml)
                if (IsLegalXmlChar(c))
                    buffer.Append(c);

            return buffer.ToString();
        }

        /// <summary>
        ///     Whether a given character is allowed by XML 1.0.
        /// </summary>
        public static bool IsLegalXmlChar(int character) =>
            character == 0x9 /* == '\t' == 9   */ ||
            character == 0xA /* == '\n' == 10  */ ||
            character == 0xD /* == '\r' == 13  */ ||
            character >= 0x20 && character <= 0xD7FF ||
            character >= 0xE000 && character <= 0xFFFD ||
            character >= 0x10000 && character <= 0x10FFFF;

        public static ThreadArchiveDuration GetMaxThreadDuration(ThreadArchiveDuration wantedDuration, IGuild guild)
        {
            var maxDuration = ThreadArchiveDuration.OneDay;
            if (guild.PremiumTier >= PremiumTier.Tier2) maxDuration = ThreadArchiveDuration.OneWeek;
            else if (guild.PremiumTier >= PremiumTier.Tier1) maxDuration = ThreadArchiveDuration.ThreeDays;

            if (wantedDuration > maxDuration) return maxDuration;
            return wantedDuration;
        }
    }
}