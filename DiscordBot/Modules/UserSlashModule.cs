using Discord.Interactions;
using DiscordBot.Services;
using DiscordBot.Settings;

namespace DiscordBot.Modules;

// For commands that only require a single interaction, these can be done automatically and don't require complex setup or configuration.
// ie; A command that might just return the result of a service method such as Ping, or Welcome
public class UserSlashModule : InteractionModuleBase
{
    #region Dependency Injection

    public CommandHandlingService CommandHandlingService { get; set; }
    public UserService UserService { get; set; }
    public BotSettings BotSettings { get; set; }
    public ILoggingService LoggingService { get; set; }

    #endregion

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

    // Returns an embed with the help text for a module, if the page is outside the bounds (high) it will return to the first page.
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
            // We need search results which we don't cache, so we don't want to provide a page number
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
            embed: UserService.GetWelcomeEmbed(Context.User.Username), ephemeral: true);
    }

    [SlashCommand("ping", "Bot latency")]
    public async Task Ping()
    {
        await Context.Interaction.RespondAsync("Bot latency: ...", ephemeral: true);
        await Context.Interaction.ModifyOriginalResponseAsync(m =>
            m.Content = $"Bot latency: {UserService.GetGatewayPing().ToString()}ms");
    }

    [SlashCommand("invite", "Returns the invite link for the server.")]
    public async Task ReturnInvite()
    {
        await Context.Interaction.RespondAsync(text: BotSettings.Invite, ephemeral: true);
    }

    #region Moderation

    [MessageCommand("Report Message")]
    public async Task ReportMessage(IMessage reportedMessage)
    {
        if (reportedMessage.Author.Id == Context.User.Id)
        {
            await Context.Interaction.RespondAsync(text: "You can't report your own messages!", ephemeral: true);
            return;
        }
        if (reportedMessage.Author.IsBot) // Don't report bots
        {
            await Context.Interaction.RespondAsync(text: "You can't report bot messages!", ephemeral: true);
            return;
        }
        if (reportedMessage.Author.IsWebhook) // Don't report webhooks
        {
            await Context.Interaction.RespondAsync(text: "You can't report webhook messages!", ephemeral: true);
            return;
        }
        await Context.Interaction.RespondWithModalAsync<ReportMessageModal>($"report_{reportedMessage.Id}");
    }

    // Defines the modal that will be sent.
    public class ReportMessageModal : IModal
    {
        public string Title => "Report a message";

        // Additional parameters can be specified to further customize the input.
        [InputLabel("Reason")]
        [ModalTextInput("report_reason", TextInputStyle.Paragraph, maxLength: 500)]
        public string Reason { get; set; }
    }

    // Responds to the modal.
    [ModalInteraction("report_*")]
    public async Task ModalResponse(ulong id, ReportMessageModal modal)
    {
        var reportedMessage = await Context.Channel.GetMessageAsync(id);

        var reportedMessageChannel = await Context.Guild.GetTextChannelAsync(BotSettings.ReportedMessageChannel.Id);
        if (reportedMessageChannel == null)
            return;

        var embed = new EmbedBuilder()
        .WithColor(new Color(0xFF0000))
        .WithDescription(reportedMessage.Content)
        .WithTimestamp(reportedMessage.Timestamp)
        .WithFooter(footer =>
        {
            footer
                .WithText($"Reported by {Context.User.GetPreferredAndUsername()} • From channel {reportedMessage.Channel.Name}")
                .WithIconUrl(Context.User.GetAvatarUrl());
        })
        .AddAuthor(reportedMessage.Author);
        
        embed.Description += $"\n\n***[Linkback]({reportedMessage.GetJumpUrl()})***";

        if (reportedMessage.Attachments.Count > 0)
        {
            var attachments = reportedMessage.Attachments.Select(a => a.Url).ToList();
            string attachmentString = string.Empty;
            for (int i = 0; i < attachments.Count; i++)
            {
                attachmentString += $"• {attachments[i]}";
                if (i < attachments.Count - 1)
                    attachmentString += "\n";
            }
            embed.AddField("Attachments", attachmentString);
        }
        embed.AddField("Reason", modal.Reason);

        await reportedMessageChannel.SendMessageAsync(string.Empty, embed: embed.Build());
        await RespondAsync("Message has been reported.", ephemeral: true);
    }

    #endregion // Moderation

    #region User Roles

    [SlashCommand("roles", "Give or Remove roles for yourself (Programmer, Artist, Designer, etc)")]
    public async Task UserRoles()
    {
        await Context.Interaction.DeferAsync(ephemeral: true);

        ComponentBuilder builder = new();

        foreach (var userRole in BotSettings.UserAssignableRoles.Roles)
        {
            builder.WithButton(userRole, $"user_role_add:{userRole}");
        }

        builder.Build();

        await Context.Interaction.FollowupAsync(text: "Click any role that applies to you!", embed: null,
            ephemeral: true, components: builder.Build());
    }

    [ComponentInteraction("user_role_add:*")]
    public async Task UserRoleAdd(string role)
    {
        await Context.Interaction.DeferAsync(ephemeral: true);

        var user = Context.User as IGuildUser;
        var guild = Context.Guild;

        // Try get the role from the guild
        var roleObj = guild.Roles.FirstOrDefault(r => r.Name == role);
        if (roleObj == null)
        {
            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                msg.Content = $"Failed to add role {role}, role not found.");
            return;
        }
        // We make sure the role is in our UserAssignableRoles just in case
        if (BotSettings.UserAssignableRoles.Roles.Contains(roleObj.Name))
        {
            if (user.RoleIds.Contains(roleObj.Id))
            {
                await user.RemoveRoleAsync(roleObj);
                await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                    msg.Content = $"{roleObj.Name} has been removed!");
            }
            else
            {
                await user.AddRoleAsync(roleObj);
                await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                    msg.Content = $"You now have the {roleObj.Name} role!");
            }
        }
    }

    #endregion

    #region Duel System

    private static readonly Dictionary<string, (ulong challengerId, ulong opponentId)> _activeDuels = new Dictionary<string, (ulong, ulong)>();
    private static readonly Random _random = new Random();

    private static readonly string[] _normalWinMessages = 
    {
        "{winner} lands a solid hit on {loser} and wins the duel!",
        "{winner} uses their sword to attack {loser}, but {loser} fails to dodge and {winner} wins!",
        "{winner} outmaneuvers {loser} with a swift strike and claims victory!",
        "{winner} blocks {loser}'s attack and counters with a decisive blow!",
        "{winner} dodges {loser}'s clumsy swing and delivers the winning hit!",
        "{winner} parries {loser}'s blade and strikes back to win the duel!",
        "{winner} feints left, strikes right, and defeats {loser}!",
        "{winner} overwhelms {loser} with superior technique and emerges victorious!"
    };

    [SlashCommand("duel", "Challenge another user to a duel!")]
    public async Task Duel(
        [Summary(description: "The user you want to duel")] IUser opponent,
        [Summary(description: "Type of duel")]
        [Choice("Normal", "normal")]
        [Choice("Mute", "mute")]
        string type = "normal")
    {
        // Prevent self-dueling
        if (opponent.Id == Context.User.Id)
        {
            await Context.Interaction.RespondAsync("You cannot duel yourself!", ephemeral: true);
            return;
        }

        // Prevent dueling bots
        if (opponent.IsBot)
        {
            await Context.Interaction.RespondAsync("You cannot duel a bot!", ephemeral: true);
            return;
        }

        // Check for active duel
        string duelKey = $"{Context.User.Id}_{opponent.Id}";
        string reverseDuelKey = $"{opponent.Id}_{Context.User.Id}";
        
        if (_activeDuels.ContainsKey(duelKey) || _activeDuels.ContainsKey(reverseDuelKey))
        {
            await Context.Interaction.RespondAsync("There's already an active duel between you two!", ephemeral: true);
            return;
        }

        // Store the duel with both user IDs for timeout tracking
        _activeDuels[duelKey] = (Context.User.Id, opponent.Id);

        var embed = new EmbedBuilder()
            .WithColor(Color.Orange)
            .WithTitle("⚔️ Duel Challenge!")
            .WithDescription($"{Context.User.Mention} has challenged {opponent.Mention} to a {type} duel!")
            .WithFooter($"This challenge will expire in 60 seconds")
            .Build();

        var components = new ComponentBuilder()
            .WithButton("⚔️ Accept", $"duel_accept:{duelKey}:{type}", ButtonStyle.Success)
            .WithButton("🛡️ Refuse", $"duel_refuse:{duelKey}", ButtonStyle.Danger)
            .WithButton("❌ Cancel", $"duel_cancel:{duelKey}", ButtonStyle.Secondary)
            .Build();

        await Context.Interaction.RespondAsync(embed: embed, components: components);

        // Store the message reference for timeout
        var originalResponse = await Context.Interaction.GetOriginalResponseAsync();
        
        // Auto-timeout after 60 seconds
        _ = Task.Run(async () =>
        {
            await Task.Delay(60000); // 60 seconds
            if (_activeDuels.ContainsKey(duelKey))
            {
                var (challengerId, opponentId) = _activeDuels[duelKey];
                _activeDuels.Remove(duelKey);
                
                try
                {
                    var challenger = await Context.Guild.GetUserAsync(challengerId);
                    var challengedUser = await Context.Guild.GetUserAsync(opponentId);
                    
                    string timeoutMessage = challengedUser != null 
                        ? $"⏰ Duel challenge to {challengedUser.Mention} expired."
                        : "⏰ Duel challenge expired.";

                    await originalResponse.ModifyAsync(msg =>
                    {
                        msg.Content = string.Empty;
                        msg.Embed = new EmbedBuilder()
                            .WithColor(Color.LightGrey)
                            .WithDescription(timeoutMessage)
                            .Build();
                        msg.Components = new ComponentBuilder().Build();
                    });
                }
                catch
                {
                    // Ignore errors if message was already modified/deleted
                }
            }
        });
    }

    [ComponentInteraction("duel_accept:*:*")]
    public async Task DuelAccept(string duelKey, string type)
    {
        // Extract user IDs from the duel key
        var userIds = duelKey.Split('_');
        if (userIds.Length != 2 || !ulong.TryParse(userIds[0], out var challengerId) || !ulong.TryParse(userIds[1], out var opponentId))
        {
            await Context.Interaction.RespondAsync("Invalid duel data!", ephemeral: true);
            return;
        }

        // Only the challenged user can accept
        if (Context.User.Id != opponentId)
        {
            await Context.Interaction.RespondAsync("Only the challenged user can accept this duel!", ephemeral: true);
            return;
        }

        // Check if duel is still active
        if (!_activeDuels.ContainsKey(duelKey))
        {
            await Context.Interaction.RespondAsync("This duel is no longer active!", ephemeral: true);
            return;
        }

        // Remove from active duels
        _activeDuels.Remove(duelKey);

        await Context.Interaction.DeferAsync();

        // Get users
        var challenger = await Context.Guild.GetUserAsync(challengerId);
        var opponent = await Context.Guild.GetUserAsync(opponentId);

        if (challenger == null || opponent == null)
        {
            await Context.Interaction.FollowupAsync("One of the duel participants is no longer available!");
            return;
        }

        // Randomly select winner (50/50)
        bool challengerWins = _random.Next(2) == 0;
        var winner = challengerWins ? challenger : opponent;
        var loser = challengerWins ? opponent : challenger;

        // Generate flavor message
        string flavorMessage = _normalWinMessages[_random.Next(_normalWinMessages.Length)];
        flavorMessage = flavorMessage.Replace("{winner}", winner.Mention).Replace("{loser}", loser.Mention);

        var resultEmbed = new EmbedBuilder()
            .WithColor(Color.Gold)
            .WithTitle("⚔️ Duel Results!")
            .WithDescription(flavorMessage)
            .AddField("Winner", winner.Mention, inline: true)
            .Build();

        await Context.Interaction.ModifyOriginalResponseAsync(msg =>
        {
            msg.Embed = resultEmbed;
            msg.Components = new ComponentBuilder().Build();
        });

        // Handle mute duel using Discord timeout
        if (type == "mute")
        {
            try
            {
                var guildLoser = loser as IGuildUser;
                if (guildLoser != null)
                {
                    // Use Discord's timeout feature for 10 minutes
                    await guildLoser.SetTimeOutAsync(TimeSpan.FromMinutes(10));
                    await Context.Interaction.FollowupAsync($"💀 {loser.Mention} has been timed out for 10 minutes as the duel loser!", ephemeral: false);
                }
            }
            catch (Exception ex)
            {
                await LoggingService.LogChannelAndFile($"Failed to timeout the loser of the duel: {ex.Message}", ExtendedLogSeverity.Error);
                await Context.Interaction.FollowupAsync("Failed to timeout the loser.", ephemeral: false);
            }
        }
    }

    [ComponentInteraction("duel_refuse:*")]
    public async Task DuelRefuse(string duelKey)
    {
        // Extract user IDs from the duel key
        var userIds = duelKey.Split('_');
        if (userIds.Length != 2 || !ulong.TryParse(userIds[0], out var challengerId) || !ulong.TryParse(userIds[1], out var opponentId))
        {
            await Context.Interaction.RespondAsync("Invalid duel data!", ephemeral: true);
            return;
        }

        // Only the challenged user can refuse
        if (Context.User.Id != opponentId)
        {
            await Context.Interaction.RespondAsync("Only the challenged user can refuse this duel!", ephemeral: true);
            return;
        }

        // Check if duel is still active
        if (!_activeDuels.ContainsKey(duelKey))
        {
            await Context.Interaction.RespondAsync("This duel is no longer active!", ephemeral: true);
            return;
        }

        // Remove from active duels
        _activeDuels.Remove(duelKey);

        // Edit the embed to show refusal instead of deleting
        await Context.Interaction.DeferAsync();
        await Context.Interaction.ModifyOriginalResponseAsync(msg =>
        {
            msg.Content = string.Empty;
            msg.Embed = new EmbedBuilder()
                .WithColor(Color.LightGrey)
                .WithDescription("🛡️ Duel challenge was refused.")
                .Build();
            msg.Components = new ComponentBuilder().Build();
        });
    }

    [ComponentInteraction("duel_cancel:*")]
    public async Task DuelCancel(string duelKey)
    {
        // Extract user IDs from the duel key
        var userIds = duelKey.Split('_');
        if (userIds.Length != 2 || !ulong.TryParse(userIds[0], out var challengerId) || !ulong.TryParse(userIds[1], out var opponentId))
        {
            await Context.Interaction.RespondAsync("Invalid duel data!", ephemeral: true);
            return;
        }

        // Only the challenger can cancel
        if (Context.User.Id != challengerId)
        {
            await Context.Interaction.RespondAsync("Only the challenger can cancel this duel!", ephemeral: true);
            return;
        }

        // Check if duel is still active
        if (!_activeDuels.ContainsKey(duelKey))
        {
            await Context.Interaction.RespondAsync("This duel is no longer active!", ephemeral: true);
            return;
        }

        // Remove from active duels
        _activeDuels.Remove(duelKey);

        // Edit the embed to show cancellation
        await Context.Interaction.DeferAsync();
        await Context.Interaction.ModifyOriginalResponseAsync(msg =>
        {
            msg.Content = string.Empty;
            msg.Embed = new EmbedBuilder()
                .WithColor(Color.LightGrey)
                .WithDescription("❌ Duel challenge was cancelled by the challenger.")
                .Build();
            msg.Components = new ComponentBuilder().Build();
        });
    }

    #endregion
}
