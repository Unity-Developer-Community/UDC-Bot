using Discord.Interactions;
using DiscordBot.Services;
using DiscordBot.Settings;

namespace DiscordBot.Modules;

public class ServerSlashModule : InteractionModuleBase
{
    public CommandHandlingService CommandHandlingService { get; set; }
    public WelcomeService WelcomeService { get; set; }
    public ServerService ServerService { get; set; }
    public BotSettings BotSettings { get; set; }

    #region Help

    [SlashCommand("help", "Shows available commands")]
    private async Task Help(string search = "")
    {
        await Context.Interaction.DeferAsync(ephemeral: true);

        var helpEmbed = HelpEmbed(0, search);
        if (helpEmbed.Item1 >= 0)
        {
            ComponentBuilder builder = new();
            builder.WithButton("Next Page", $"user_module_help_next:{0}");

            await Context.Interaction.FollowupAsync(embed: helpEmbed.Item2, ephemeral: true,
                components: builder.Build());
        }
        else
        {
            await Context.Interaction.FollowupAsync(embed: helpEmbed.Item2, ephemeral: true);
        }
    }

    [ComponentInteraction("user_module_help_next:*")]
    private async Task InteractionHelp(string pageString)
    {
        await Context.Interaction.DeferAsync(ephemeral: true);

        int page = int.Parse(pageString);

        var helpEmbed = HelpEmbed(page + 1);
        ComponentBuilder builder = new();
        builder.WithButton("Next Page", $"user_module_help_next:{helpEmbed.Item1}");

        await Context.Interaction.ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = builder.Build();
            msg.Embed = helpEmbed.Item2;
        });
    }

    private (int, Embed) HelpEmbed(int page, string search = "")
    {
        EmbedBuilder embedBuilder = new();
        embedBuilder.Title = "User Module Commands";
        embedBuilder.Color = Color.LighterGrey;

        List<string> helpMessages = null;
        if (search == string.Empty)
        {
            helpMessages = CommandHandlingService.GetCommandListMessages("UserModule", false, true, false);

            if (page >= helpMessages.Count)
                page = 0;
            else if (page < 0)
                page = helpMessages.Count - 1;

            embedBuilder.WithFooter(text: $"Page {page + 1} of {helpMessages.Count}");
            embedBuilder.Description = helpMessages[page];
        }
        else
        {
            page = -1;
            helpMessages = CommandHandlingService.SearchForCommand(("UserModule", false, true, false), search);
            if (helpMessages[0].Length > 0)
            {
                embedBuilder.WithFooter(text: $"Search results for {search}");
                embedBuilder.Description = helpMessages[0];
            }
            else
            {
                embedBuilder.WithFooter(text: $"No results for {search}");
                embedBuilder.Description = "No commands found";
            }
        }

        return (page, embedBuilder.Build());
    }

    #endregion

    [SlashCommand("welcome", "An introduction to the server!")]
    public async Task SlashWelcome()
    {
        await Context.Interaction.RespondAsync(string.Empty,
            embed: WelcomeService.GetWelcomeEmbed(Context.User.Username), ephemeral: true);
    }

    [SlashCommand("ping", "Bot latency")]
    public async Task Ping()
    {
        await Context.Interaction.RespondAsync("Bot latency: ...", ephemeral: true);
        await Context.Interaction.ModifyOriginalResponseAsync(m =>
            m.Content = $"Bot latency: {ServerService.GetGatewayPing().ToString()}ms");
    }

    [SlashCommand("invite", "Returns the invite link for the server.")]
    public async Task ReturnInvite()
    {
        await Context.Interaction.RespondAsync(text: BotSettings.Invite, ephemeral: true);
    }
}
