using System.Globalization;
using DiscordBot.Domain;

namespace DiscordBot.Services.Fun.Casino;

/// <summary>Builds the Discord messages of the slots: the private result, the public announcement and the info page.</summary>
public static class SlotsMessageBuilder
{
    public const string GifFileName = "spin.gif";
    public const string PictureFileName = "spin.png";
    public const string SpinButtonPrefix = "slots_spin";
    public const string HoldButtonPrefix = "slots_hold";
    public const string RespinButtonPrefix = "slots_respin";

    public static string Symbols(SlotResult result) => string.Join(" ", result.Symbols.Select(s => s.Emoji()));

    // Fixed culture: the same text for every player, wherever the bot runs.
    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static long PotTokens(long potHundredths) => potHundredths / SlotSettlementMath.HundredthsPerToken;

    /// <summary>
    /// The result as text, repeating the symbols: players who turned off GIF autoplay see only the first frame.
    /// </summary>
    public static string ResultText(SlotsPlay play)
    {
        var result = play.Result!;
        var outcome = result switch
        {
            { IsJackpot: true } => $"💎 **JACKPOT!** You won **{N(play.Payout)}** tokens from the shared jackpot.",
            { IsWin: true } when result.IsRespin => $"✅ Free respin win: **{N(play.Payout)}** tokens ({result.Multiplier}× your bet of {N(play.Bet)}).",
            { IsWin: true } => $"✅ You won **{N(play.Payout)}** tokens ({result.Multiplier}× your bet of {N(play.Bet)}).",
            { IsRespin: true } => "❌ The free respin won nothing.",
            _ => $"❌ No win. You lost **{N(play.Bet)}** tokens."
        };

        return $"🎰 **{Symbols(result)}**\n{outcome}\n"
            + $"💰 Balance: **{N(play.Balance)}** tokens · 🏆 Jackpot: **{N(PotTokens(play.PotHundredths))}** tokens";
    }

    public static string RefusalText(SlotsStatus status) => status switch
    {
        SlotsStatus.InvalidBet => $"🚫 Bet between {SlotMachine.MinBet} and {SlotMachine.MaxBet} tokens.",
        SlotsStatus.InsufficientTokens => "💸 You do not have enough tokens for this bet.",
        SlotsStatus.Busy => "⏳ Your previous spin is still being processed.",
        SlotsStatus.NoHold => "⌛ This hold offer is no longer available.",
        _ => "❌ Something went wrong while playing the slots. Please try again."
    };

    /// <summary>The player's private reply. Its image is the GIF attached as <see cref="GifFileName"/>, when there is one.</summary>
    public static Embed BuildResultEmbed(SlotsPlay play)
    {
        var description = ResultText(play);
        if (play.Hold is not null)
            description += "\n\n🔒 Hold the reels you want to keep, then take a **free respin** of the others.";
        if (play.Gif is null)
            description += "\n-# The animation could not be shown.";

        var embed = new EmbedBuilder()
            .WithTitle("🎰 Slots")
            .WithDescription(description)
            .WithColor(play.Result!.IsJackpot ? Color.Gold : play.Result.IsWin ? Color.Green : Color.DarkGrey);

        if (play.Gif is not null)
            embed.WithImageUrl($"attachment://{GifFileName}");
        return embed.Build();
    }

    /// <summary>Spin again at the same bet, or, when a hold offer is open, the hold toggles and the free respin in its place.</summary>
    public static MessageComponent BuildComponents(int bet, SlotsHold? hold)
    {
        var builder = new ComponentBuilder();
        if (hold is not null)
        {
            for (var reel = 0; reel < SlotMachine.ReelCount; reel++)
            {
                var held = hold.Held[reel];
                // The label changes with the state, so it does not rely on the button color alone.
                builder.WithButton(
                    held ? $"🔒 Reel {reel + 1}: Held" : $"Reel {reel + 1}: Hold",
                    $"{HoldButtonPrefix}:{hold.OfferId}:{reel}",
                    held ? ButtonStyle.Success : ButtonStyle.Secondary,
                    row: 0);
            }
            // Takes the place of "Spin again" so the free one cannot be skipped by a misclick.
            builder.WithButton("🔁 Free respin", $"{RespinButtonPrefix}:{hold.OfferId}", ButtonStyle.Primary, row: 1);
        }
        else
        {
            builder.WithButton($"🎰 Spin again ({N(bet)})", $"{SpinButtonPrefix}:{bet}", ButtonStyle.Primary);
        }

        return builder.Build();
    }

    /// <summary>The public announcement of a jackpot or a big win, naming the player without pinging them.</summary>
    public static string PublicText(SlotsPlay play, string playerMention)
    {
        var result = play.Result!;
        return result.IsJackpot
            ? $"💎 **JACKPOT!** {playerMention} hit three diamonds on the slots and won **{N(play.Payout)}** tokens!"
            : $"🎰 **Big win!** {playerMention} won **{N(play.Payout)}** tokens ({result.Multiplier}× the bet) on the slots!";
    }

    /// <summary>
    /// The embed of the public announcement. The bot attaches the GIF; the web casino passes its address and the file name
    /// of the static picture it attaches, which links the title to the site and says where the win was played.
    /// </summary>
    public static Embed BuildPublicEmbed(SlotsPlay play, Uri? webCasino = null, string? pictureFileName = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle("🎰 Slots")
            .WithDescription($"**{Symbols(play.Result!)}**")
            .WithColor(play.Result!.IsJackpot ? Color.Gold : Color.Green)
            .WithFooter(webCasino is null
                ? $"Play with /casino slots spin ({SlotMachine.MinBet}–{SlotMachine.MaxBet} tokens)."
                : $"Won on the web casino · play at {webCasino.Authority} or with /casino slots spin ({SlotMachine.MinBet}–{SlotMachine.MaxBet} tokens).");

        if (webCasino is not null)
            embed.WithUrl(webCasino.AbsoluteUri);

        if (pictureFileName is not null)
            embed.WithImageUrl($"attachment://{pictureFileName}");
        else if (play.Gif is not null)
            embed.WithImageUrl($"attachment://{GifFileName}");
        return embed.Build();
    }

    public static Embed BuildInfoEmbed(long potHundredths)
    {
        var paytable = string.Join("\n", SlotMachine.Paytable.Select(entry => entry.IsJackpot
            ? $"{entry.Pattern} → **Jackpot**: bet ÷ 100 of the pot"
            : $"{entry.Pattern} → **{entry.Multiplier}×** your bet"))
            + "\n\"+ any\" means exactly two of that symbol anywhere on the line. Other pairs pay nothing.";

        return new EmbedBuilder()
            .WithTitle("🎰 Slots")
            .WithColor(Color.Gold)
            .WithDescription($"Three reels, one payline in the middle. Bet {SlotMachine.MinBet}–{SlotMachine.MaxBet} tokens with `/casino slots spin`.")
            .AddField("💰 Paytable", paytable)
            .AddField("🏆 Jackpot",
                $"**{N(PotTokens(potHundredths))}** tokens right now, shared by the whole server. "
                + $"{SlotSettlementMath.ContributionPercent}% of every bet feeds it. "
                + $"Three 💎 pay a 100-token bet the whole pot, a smaller bet its share of it (bet ÷ 100). "
                + $"The pot never drops below {N(SlotSettlementMath.JackpotFloorHundredths / SlotSettlementMath.HundredthsPerToken)} tokens.")
            .AddField("🔒 Hold and respin",
                $"About 1 in {100 / SlotMachine.HoldOfferPercent} losing spins offers a hold: keep the reels you like and respin the others once, for free, at the same bet. "
                + "A respin never offers another hold. The offer ends after 10 minutes or when you spin again.")
            .AddField("📣 Big wins",
                $"Lines that land less than once in {SlotMachine.PublicWinOneIn} spins ("
                + string.Join(" ", SlotMachine.Paytable.Where(e => e.IsAnnounced && !e.IsJackpot).Select(e => e.Pattern))
                + "), and every jackpot, are announced in the channel.")
            .Build();
    }
}
