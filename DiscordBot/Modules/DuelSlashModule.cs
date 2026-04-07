using Discord.Interactions;
using DiscordBot.Services;

namespace DiscordBot.Modules;

public class DuelSlashModule : InteractionModuleBase
{
    public DuelService DuelService { get; set; } = null!;
    public ILoggingService LoggingService { get; set; } = null!;

    [SlashCommand("duel", "Challenge another user to a duel!")]
    public async Task Duel(
        [Summary(description: "The user you want to duel")] IUser opponent,
        [Summary(description: "Type of duel")]
        [Choice("Normal", "normal")]
        [Choice("Mute", "mute")]
        string type = "normal")
    {
        if (opponent.Id == Context.User.Id)
        {
            await Context.Interaction.RespondAsync("You cannot duel yourself!", ephemeral: true);
            return;
        }

        if (opponent.IsBot)
        {
            await Context.Interaction.RespondAsync("You cannot duel a bot!", ephemeral: true);
            return;
        }

        string duelKey = $"{Context.User.Id}_{opponent.Id}";

        if (!DuelService.TryStartDuel(duelKey, Context.User.Id, opponent.Id))
        {
            await Context.Interaction.RespondAsync("There's already an active duel between you two!", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithColor(Color.Orange)
            .WithTitle("⚔️ Duel Challenge!")
            .WithDescription($"{Context.User.Mention} has challenged {opponent.Mention} to a duel!")
            .WithFooter($"This challenge will expire in 60 seconds");

        if (type == "mute")
        {
            embed.AddField("Risk", "The loser will be muted for 5 minutes.");
        }

        var components = new ComponentBuilder()
            .WithButton("⚔️ Accept", $"duel_accept:{duelKey}:{type}", ButtonStyle.Success)
            .WithButton("🛡️ Refuse", $"duel_refuse:{duelKey}", ButtonStyle.Danger)
            .WithButton("❌ Cancel", $"duel_cancel:{duelKey}", ButtonStyle.Secondary)
            .Build();

        await Context.Interaction.RespondAsync(embed: embed.Build(), components: components);

        var originalResponse = await Context.Interaction.GetOriginalResponseAsync();

        _ = Task.Run(async () =>
        {
            await Task.Delay(60000);
            var duel = DuelService.GetDuel(duelKey);
            if (duel != null)
            {
                DuelService.TryRemoveDuel(duelKey, out _);

                try
                {
                    var challenger = await Context.Guild.GetUserAsync(duel.Value.challengerId);
                    var challengedUser = await Context.Guild.GetUserAsync(duel.Value.opponentId);

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
                catch (Exception ex)
                {
                    await LoggingService.LogChannelAndFile($"Failed to modify duel timeout message: {ex.Message}", ExtendedLogSeverity.Warning);
                }
            }
        });
    }

    [ComponentInteraction("duel_accept:*:*")]
    public async Task DuelAccept(string duelKey, string type)
    {
        var userIds = duelKey.Split('_');
        if (userIds.Length != 2 || !ulong.TryParse(userIds[0], out var challengerId) || !ulong.TryParse(userIds[1], out var opponentId))
        {
            await Context.Interaction.RespondAsync("Invalid duel data!", ephemeral: true);
            return;
        }

        if (Context.User.Id != opponentId)
        {
            await Context.Interaction.RespondAsync("Only the challenged user can accept this duel!", ephemeral: true);
            return;
        }

        if (!DuelService.TryRemoveDuel(duelKey, out _))
        {
            await Context.Interaction.RespondAsync("This duel is no longer active!", ephemeral: true);
            return;
        }

        await Context.Interaction.DeferAsync();

        var challenger = await Context.Guild.GetUserAsync(challengerId);
        var opponent = await Context.Guild.GetUserAsync(opponentId);

        if (challenger == null || opponent == null)
        {
            await Context.Interaction.FollowupAsync("One of the duel participants is no longer available!");
            return;
        }

        bool challengerWins = DuelService.ChallengerWins();
        var winner = challengerWins ? challenger : opponent;
        var loser = challengerWins ? opponent : challenger;
        if (type == "mute")
        {
            var isChallengerAdmin = challenger.GuildPermissions.Has(GuildPermission.Administrator);
            var isOpponentAdmin = opponent.GuildPermissions.Has(GuildPermission.Administrator);
            if (isChallengerAdmin || isOpponentAdmin)
            {
                type = "friendly";
            }
        }

        string flavorMessage = DuelService.GetWinMessage(winner.Mention, loser.Mention);

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

        if (type == "mute")
        {
            try
            {
                var guildLoser = loser as IGuildUser;
                if (guildLoser != null)
                {
                    await guildLoser.SetTimeOutAsync(TimeSpan.FromMinutes(5), new RequestOptions { AuditLogReason = "Lost /duel" });
                    await Context.Interaction.FollowupAsync($"💀 {loser.Mention} has been timed out for 5 minutes as the duel loser!", ephemeral: false);
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
        var userIds = duelKey.Split('_');
        if (userIds.Length != 2 || !ulong.TryParse(userIds[0], out var challengerId) || !ulong.TryParse(userIds[1], out var opponentId))
        {
            await Context.Interaction.RespondAsync("Invalid duel data!", ephemeral: true);
            return;
        }

        if (Context.User.Id != opponentId)
        {
            await Context.Interaction.RespondAsync("Only the challenged user can refuse this duel!", ephemeral: true);
            return;
        }

        if (!DuelService.TryRemoveDuel(duelKey, out _))
        {
            await Context.Interaction.RespondAsync("This duel is no longer active!", ephemeral: true);
            return;
        }

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
        var userIds = duelKey.Split('_');
        if (userIds.Length != 2 || !ulong.TryParse(userIds[0], out var challengerId) || !ulong.TryParse(userIds[1], out var opponentId))
        {
            await Context.Interaction.RespondAsync("Invalid duel data!", ephemeral: true);
            return;
        }

        if (Context.User.Id != challengerId)
        {
            await Context.Interaction.RespondAsync("Only the challenger can cancel this duel!", ephemeral: true);
            return;
        }

        if (!DuelService.TryRemoveDuel(duelKey, out _))
        {
            await Context.Interaction.RespondAsync("This duel is no longer active!", ephemeral: true);
            return;
        }

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
}
