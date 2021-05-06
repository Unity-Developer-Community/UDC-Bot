using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using DiscordBot.Data;
using DiscordBot.Services;

namespace DiscordBot.Modules
{
    [Group("Announce")]
    public class BotAnnouncementModule : ModuleBase
    {
        private static string _commandList;
        
        private readonly ILoggingService _logging;
        private readonly BotAnnouncementService _announcementService;

        public BotAnnouncementModule(ILoggingService loggingService, BotAnnouncementService announcementService, CommandService commandService)
        {
            _logging = loggingService;
            _announcementService = announcementService;
            
            GenerateCommandList(commandService);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [Summary("Copies the message from the ID provided and makes a new announcement.")]
        [Command("New"), Priority(0)]
        public async Task NewAnnouncement(ulong messageId, bool sendNow = false)
        {
            await Context.Message.DeleteAsync();
            var linkedMessage = await Context.Channel.GetMessageAsync(messageId);
            if (linkedMessage == null)
            {
                await ReplyAsync($"Message with ID: `{messageId}` does not exist.");
                return;
            }
            
            await _announcementService.AddAnnouncement(linkedMessage.Content, Context.User, sendNow);
            if (sendNow)
                await ReplyAsync("Announcement sent.");
            else
                await ReplyAsync("Announcement added.");
        }
        
        [RequireUserPermission(GuildPermission.Administrator)]
        [Summary("Shows the stored announcements in current channel.")]
        [Command("Preview"), Priority(1)]
        public async Task PreviewAnnouncement()
        {
            await Context.Message.DeleteAsync();
            if (!await _announcementService.PreviewAnnouncements(Context.Channel))
            {
                await ReplyAsync($"No announcements to preview");
            }
        }
        
        [RequireUserPermission(GuildPermission.Administrator)]
        [Summary("Clears all stored announcements.")]
        [Command("Clear"), Priority(2)]
        public async Task ClearAnnouncements()
        {
            await Context.Message.DeleteAsync();
            var count = await _announcementService.ClearAnnouncements(Context.User);
            await ReplyAsync($"Cleared {count} stored announcements");
        }
        
        [RequireUserPermission(GuildPermission.Administrator)]
        [Summary("Does what you see now.")]
        [Command("Help"), Priority(100)]
        public async Task AnnounceHelp()
        {
            await ReplyAsync(_commandList);
        }
        
        //TODO This can be made into an extension without much additional work. This would allow greater flexibility for command lists for modules. (ReactRole uses the same code below)
        
        /// <summary> Generates a command list that attempts to give the user valuable information, also provides argument information. </summary>
        public static void GenerateCommandList(CommandService commandService)
        {
            StringBuilder commandList = new StringBuilder();

            commandList.Append("__Bot Announcement Commands__\n");
            foreach (var c in commandService.Commands.Where(x => x.Module.Name == "Announce").OrderBy(c => c.Priority))
            {
                string args = "";
                foreach (var info in c.Parameters)
                {
                    args += $"`{info.Name}`{(info.IsOptional ? "\\*" : String.Empty)} ";
                }
                if (args.Length > 0)
                    args = $"- args: *( {args})*";

                commandList.Append($"**Announce {c.Name}** : {c.Summary} {args}\n");
            }

            _commandList = commandList.ToString();
        }
    }
}