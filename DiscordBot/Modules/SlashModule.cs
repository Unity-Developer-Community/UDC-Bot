using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using DiscordBot.Extensions;
using DiscordBot.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Modules
{
    public class SlashModule : InteractionModuleBase
    {
        private List<string> userModuleHelp;
        private Embed[] userModuleHelpEmbed;

        private List<string> userModuleHelpCommands;
        public SlashModule( IServiceProvider serviceProvider)
        {
            Task.Run(async () =>
            {
                var commandHandling = serviceProvider.GetService<CommandHandlingService>();
                if (commandHandling != null)
                {
                    var userModuleHelpString = await commandHandling.GetCommandList(nameof(UserModule), includeModuleName: false);
                    userModuleHelp = userModuleHelpString.MessageSplitToSize();
                    
                    List<Embed> tempEmbed = new List<Embed>();
                    foreach (var help in userModuleHelp)    
                    {
                        EmbedBuilder embed = new EmbedBuilder();
                        embed.WithTitle($"User Commands\t Page #{tempEmbed.Count + 1}");
                        embed.Description = help;
                        tempEmbed.Add(embed.Build());
                    }
                    userModuleHelpEmbed = tempEmbed.ToArray();

                    userModuleHelpCommands =
                        await commandHandling.GetCommandsAsList(nameof(UserModule), includeModuleName: false);
                }
            });
        }
        
        [SlashCommand("help", "Show commands to user")]
        private async Task Help(string search = "")
        {
            await Context.Interaction.DeferAsync();
            if (search == string.Empty)
                await Context.Interaction.FollowupAsync(embeds: userModuleHelpEmbed, ephemeral: true);
            else
            {
                StringBuilder sb = new StringBuilder();
                foreach (var command in userModuleHelpCommands)
                {
                    if (command.Contains(search))
                        sb.Append(command);
                }
                
                if (sb.Length == 0)
                    await Context.Interaction.FollowupAsync("No commands found");
                else
                {
                    EmbedBuilder em = new EmbedBuilder();
                    em.Title = $"Help '{search}' Results";
                    em.Description = sb.ToString();
                    await Context.Interaction.FollowupAsync(embed: em.Build());
                }
            }
        }
    }
}