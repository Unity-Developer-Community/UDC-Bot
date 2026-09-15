using System.Text;
using Discord.Interactions;
using DiscordBot.Data;
using DiscordBot.Domain.Dice;
using DiscordBot.Settings;

namespace DiscordBot.Modules.Fun;

public class FunSlashModule : InteractionModuleBase<SocketInteractionContext>
{
    private const string DefaultDice = "1d6";
    private const int SlapFailChancePercent = 5;

    private static readonly string[] CoinSides = ["Heads", "Tails"];

    /// <summary>Legacy easter egg: these users always end up slapping themselves.</summary>
    private static readonly ulong[] SelfSlapUserIds = [162189038965489664, 178201477280563200];

    private static readonly FuzzTable SlapBridges = new();
    private static readonly FuzzTable SlapObjects = new();
    private static readonly FuzzTable SlapFails = new();

    // Modules are instantiated per invocation, so this must be static to survive across calls.
    private static bool _slapObjectsLoaded;

    private readonly Random _random = new();

    public ILoggingService LoggingService { get; set; } = null!;
    public BotSettings Settings { get; set; } = null!;

    [SlashCommand("coinflip", "Flip a coin and see the result.")]
    public async Task CoinFlip()
    {
        var userName = Context.User.GetUserPreferredName();

        await Context.Interaction.RespondAsync(
            $"**{userName}** flipped a coin and got **{CoinSides[_random.Next(CoinSides.Length)]}**!");
    }

    [SlashCommand("roll", "Roll dice, for example 2d6, d20 or 2d6+4")]
    public async Task Roll(
        [Summary("dice", "Dice to roll, for example 2d6, d20 or 2d6+4 (default: 1d6)")] string? dice = null,
        [Summary("needed", "Optional total the roll needs to match or beat")] int? needed = null)
    {
        if (!DiceExpression.TryParse(dice ?? DefaultDice, out var expression, out var error))
        {
            await Context.Interaction.RespondAsync($"❌ {error}", ephemeral: true);
            return;
        }

        var result = expression.Roll(_random);
        var message = FunRollFormatter.Format(Context.User.GetUserPreferredName(), result);

        var target = needed ?? 0;
        if (target < 1)
            message = " :game_die: " + message;
        else
            message = $"{(result.Total >= target ? " :white_check_mark: " : " :x: ")}{message} [Needed: {target}]";

        await Context.Interaction.RespondAsync(message);
    }

    [SlashCommand("slap", "Slap one or more users with a random object")]
    public async Task Slap(
        [Summary("target1", "First user to slap")] IUser? target1 = null,
        [Summary("target2", "Second user to slap")] IUser? target2 = null,
        [Summary("target3", "Third user to slap")] IUser? target3 = null,
        [Summary("target4", "Fourth user to slap")] IUser? target4 = null,
        [Summary("target5", "Fifth user to slap")] IUser? target5 = null)
    {
        IUser?[] selected = [target1, target2, target3, target4, target5];

        await SlapUsersAsync(selected.Where(user => user is not null).Select(user => user!).ToArray());
    }

    private async Task SlapUsersAsync(IUser[] targets)
    {
        if (!await TryLoadSlapObjectsAsync())
        {
            await Context.Interaction.RespondAsync("❌ Could not slap anyone right now.", ephemeral: true);
            return;
        }

        var userName = Context.User.GetUserPreferredName();

        if (targets.Length == 0)
        {
            await Context.Interaction.RespondAsync($"**{userName}** slaps away an invisible pest.");
            return;
        }

        if (SelfSlapUserIds.Contains(Context.User.Id))
        {
            await Context.Interaction.RespondAsync($"**{userName}** slaps themself.");
            return;
        }

        EnsureSlapTables();

        var mentions = targets.ToMentionArray().ToCommaList();
        var builder = new StringBuilder();

        if (_random.Next(1, 100) < SlapFailChancePercent)
        {
            builder.Append($"**{userName}** tries to slap {mentions} ");
            builder.Append(SlapBridges.Pick(true));
            builder.Append(SlapObjects.Pick(true));
            builder.Append(", but misses and ends up ");
            builder.Append(SlapFails.Pick(true));
            builder.Append('.');
        }
        else
        {
            builder.Append($"**{userName}** slaps {mentions} ");
            builder.Append(SlapBridges.Pick(true));
            builder.Append(SlapObjects.Pick(true));
            builder.Append('.');
        }

        await Context.Interaction.RespondAsync(builder.ToString());
    }

    private async Task<bool> TryLoadSlapObjectsAsync()
    {
        // The table is static configuration, so it is only read once per process: retrying it on every
        // invocation would spend Discord API calls inside the three second interaction window.
        if (_slapObjectsLoaded)
            return true;

        try
        {
            SlapObjects.Unique = true;
            SlapObjects.Load(Settings.FunCommands.SlapObjectsTable!);
            _slapObjectsLoaded = true;

            await LoggingService.LogChannelAndFile($"Loaded {SlapObjects.Count} slap object entries.");
            return true;
        }
        catch (Exception)
        {
            _slapObjectsLoaded = true;

            await LoggingService.LogChannelAndFile($"Error while loading '{Settings.FunCommands.SlapObjectsTable}'.",
                ExtendedLogSeverity.LowWarning);
            return false;
        }
    }

    private void EnsureSlapTables()
    {
        if (SlapBridges.Count == 0)
        {
            SlapBridges.Add("(around|about) (a bit|by surprise|suddenly) with (a large|a huge|an oversized|their own private|a suspiciously convenient) ");
            SlapBridges.Add("(over the head|from behind|across their shoulders) with (a large|a huge|an oversized|their own private|a suspiciously convenient) ");
        }

        if (SlapObjects.Count == 0)
            SlapObjects.Add(Settings.FunCommands.SlapChoices);
        if (SlapObjects.Count == 0)
            SlapObjects.Add("fish|mallet");

        if (SlapFails.Count == 0)
            SlapFails.Add(Settings.FunCommands.SlapFails);
        if (SlapFails.Count == 0)
            SlapFails.Add("hurting themselves");
    }
}
