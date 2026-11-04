using System.IO;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Domain;

namespace DiscordBot.Modules.Fun.Casino;

public partial class CasinoSlashModule
{
    #region Slots Commands

    [Group("slots", "Slot machine with a jackpot shared by the whole server")]
    public class SlotsCommands : InteractionModuleBase<SocketInteractionContext>
    {
        public CasinoService CasinoService { get; set; } = null!;
        public SlotsService SlotsService { get; set; } = null!;
        public ILoggingService LoggingService { get; set; } = null!;

        private async Task<bool> CheckChannelPermissions()
        {
            if (!CasinoService.IsChannelAllowed(Context.Channel.Id))
            {
                await Context.Interaction.RespondAsync(
                    "🚫 Casino commands are not allowed in this channel.",
                    ephemeral: true);
                return false;
            }
            return true;
        }

        [SlashCommand("spin", "Spin the reels")]
        public async Task Spin(
            [Summary("bet", "Tokens to bet")]
            [MinValue(SlotMachine.MinBet)]
            [MaxValue(SlotMachine.MaxBet)]
            int bet)
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync(ephemeral: true);
            var play = await SlotsService.SpinAsync(Context.User.Id, bet);
            if (play.Status != SlotsStatus.Ok)
            {
                await Context.Interaction.FollowupAsync(SlotsMessageBuilder.RefusalText(play.Status), ephemeral: true);
                return;
            }

            var embed = SlotsMessageBuilder.BuildResultEmbed(play);
            var components = SlotsMessageBuilder.BuildComponents(play.Bet, play.Hold);
            if (play.Gif is null)
                await Context.Interaction.FollowupAsync(embed: embed, components: components, ephemeral: true);
            else
                await Context.Interaction.FollowupWithFileAsync(new MemoryStream(play.Gif), SlotsMessageBuilder.GifFileName, embed: embed, components: components, ephemeral: true);

            await PostPublicly(play);
        }

        [SlashCommand("info", "Paytable, rules and the current jackpot")]
        public async Task Info()
        {
            if (!await CheckChannelPermissions()) return;

            await Context.Interaction.DeferAsync(ephemeral: true);
            var pot = await SlotsService.GetPotHundredths();
            await Context.Interaction.FollowupAsync(embed: SlotsMessageBuilder.BuildInfoEmbed(pot), ephemeral: true);
        }

        [ComponentInteraction($"{SlotsMessageBuilder.SpinButtonPrefix}:*", true)]
        public async Task SpinAgain(string betRaw)
        {
            if (!await CheckChannelPermissions()) return;

            // The id comes from the client, so the bet is checked again like a typed one.
            if (!int.TryParse(betRaw, out var bet))
            {
                await Context.Interaction.RespondAsync(SlotsMessageBuilder.RefusalText(SlotsStatus.InvalidBet), ephemeral: true);
                return;
            }

            await Context.Interaction.DeferAsync();
            await ReplaceMessage(await SlotsService.SpinAsync(Context.User.Id, bet));
        }

        [ComponentInteraction($"{SlotsMessageBuilder.HoldButtonPrefix}:*:*", true)]
        public async Task ToggleHold(string offerRaw, string reelRaw)
        {
            var hold = long.TryParse(offerRaw, out var offerId) && int.TryParse(reelRaw, out var reel)
                ? SlotsService.ToggleHold(Context.User.Id, offerId, reel)
                : null;
            if (hold is null)
            {
                await Context.Interaction.RespondAsync(SlotsMessageBuilder.RefusalText(SlotsStatus.NoHold), ephemeral: true);
                return;
            }

            // Only the buttons change: the embed and the GIF stay as they are.
            await ((SocketMessageComponent)Context.Interaction).UpdateAsync(message =>
                message.Components = SlotsMessageBuilder.BuildComponents(hold.Bet, hold));
        }

        [ComponentInteraction($"{SlotsMessageBuilder.RespinButtonPrefix}:*", true)]
        public async Task Respin(string offerRaw)
        {
            if (!await CheckChannelPermissions()) return;

            if (!long.TryParse(offerRaw, out var offerId))
            {
                await Context.Interaction.RespondAsync(SlotsMessageBuilder.RefusalText(SlotsStatus.NoHold), ephemeral: true);
                return;
            }

            await Context.Interaction.DeferAsync();
            await ReplaceMessage(await SlotsService.RespinAsync(Context.User.Id, offerId));
        }

        /// <summary>Shows a new result in place of the one the clicked button belongs to. A refusal leaves that message alone.</summary>
        private async Task ReplaceMessage(SlotsPlay play)
        {
            if (play.Status != SlotsStatus.Ok)
            {
                await Context.Interaction.FollowupAsync(SlotsMessageBuilder.RefusalText(play.Status), ephemeral: true);
                return;
            }

            var embed = SlotsMessageBuilder.BuildResultEmbed(play);
            var components = SlotsMessageBuilder.BuildComponents(play.Bet, play.Hold);
            await Context.Interaction.ModifyOriginalResponseAsync(message =>
            {
                message.Embed = embed;
                message.Components = components;
                // Replacing the attachments drops the previous GIF, so an old animation never stays under a new result.
                message.Attachments = play.Gif is null
                    ? Array.Empty<FileAttachment>()
                    : new[] { new FileAttachment(new MemoryStream(play.Gif), SlotsMessageBuilder.GifFileName) };
            });

            await PostPublicly(play);
        }

        private async Task PostPublicly(SlotsPlay play)
        {
            if (!play.PostPublicly) return;

            try
            {
                var text = SlotsMessageBuilder.PublicText(play, Context.User.Mention);
                var embed = SlotsMessageBuilder.BuildPublicEmbed(play);
                if (play.Gif is null)
                    await Context.Channel.SendMessageAsync(text, embed: embed, allowedMentions: AllowedMentions.None);
                else
                    await Context.Channel.SendFileAsync(new MemoryStream(play.Gif), SlotsMessageBuilder.GifFileName, text, embed: embed, allowedMentions: AllowedMentions.None);
            }
            catch (Exception ex)
            {
                await LoggingService.LogChannelAndFile($"Casino: could not post the slots win of {Context.User.Username} in the channel: {ex.Message}", ExtendedLogSeverity.Warning);
            }
        }
    }

    #endregion
}
