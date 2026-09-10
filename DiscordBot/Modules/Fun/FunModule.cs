using System.Text;
using Discord.Commands;
using DiscordBot.Services;
using DiscordBot.Settings;
using DiscordBot.Data;

namespace DiscordBot.Modules.Fun;

[Group("UserModule"), Alias("")]
public class FunModule : ModuleBase
{
    public ILoggingService LoggingService { get; set; } = null!;
    public BotSettings Settings { get; set; } = null!;

    private readonly Random _random = new();
    private static FuzzTable _slapBridges = new();
    private static FuzzTable _slapObjects = new();
    private static FuzzTable _slapFails = new();

    [Command("Slap"), Priority(21)]
    [Summary("Slap the specified user(s). Syntax : !slap @user1 [@user2 @user3...]")]
    public async Task SlapUser(params IUser[] users)
    {
        try
        {
            _slapObjects.Unique = true;
            if (_slapObjects.Count == 0)
            {
                _slapObjects.Load(Settings.FunCommands.SlapObjectsTable!);
                await LoggingService.LogChannelAndFile($"Loaded {_slapObjects.Count} slap object entries.");
            }
        }
        catch (Exception)
        {
            await LoggingService.LogChannelAndFile($"Error while loading '{Settings.FunCommands.SlapObjectsTable}'.",
                ExtendedLogSeverity.LowWarning);
            return;
        }

        var uname = Context.User.GetUserPreferredName();

        if (users == null || users.Length == 0)
        {
            await Context.Channel.SendMessageAsync(
                $"**{uname}** slaps away an invisible pest.");
            await Context.Message.DeleteAfterSeconds(seconds: 1)!;
            return;
        }

        if (Context.User.Id == 162189038965489664 ||
            Context.User.Id == 178201477280563200)
        {
            await Context.Channel.SendMessageAsync(
                $"**{uname}** slaps themself.");
            await Context.Message.DeleteAfterSeconds(seconds: 1)!;
            return;
        }

        if (_slapBridges.Count == 0)
        {
            _slapBridges.Add("(around|about) (a bit|by surprise|suddenly) with (a large|a huge|an oversized|their own private|a suspiciously convenient) ");
            _slapBridges.Add("(over the head|from behind|across their shoulders) with (a large|a huge|an oversized|their own private|a suspiciously convenient) ");
        }

        if (_slapObjects.Count == 0)
            _slapObjects.Add(Settings.FunCommands.SlapChoices);
        if (_slapObjects.Count == 0)
            _slapObjects.Add("fish|mallet");

        if (_slapFails.Count == 0)
            _slapFails.Add(Settings.FunCommands.SlapFails);
        if (_slapFails.Count == 0)
            _slapFails.Add("hurting themselves");

        var sb = new StringBuilder();
        var mentions = users.ToMentionArray().ToCommaList();

        bool fail = (_random.Next(1, 100) < 5);
        if (fail)
        {
            sb.Append($"**{uname}** tries to slap {mentions} ");
            sb.Append(_slapBridges.Pick(true));
            sb.Append(_slapObjects.Pick(true));
            sb.Append(", but misses and ends up ");
            sb.Append(_slapFails.Pick(true));
            sb.Append(".");
        }
        else
        {
            sb.Append($"**{uname}** slaps {mentions} ");
            sb.Append(_slapBridges.Pick(true));
            sb.Append(_slapObjects.Pick(true));
            sb.Append(".");
        }

        await Context.Channel.SendMessageAsync(sb.ToString());
        await Context.Message.DeleteAfterSeconds(seconds: 1)!;
    }

    [Command("CoinFlip"), Priority(22)]
    [Summary("Flip a coin and see the result.")]
    [Alias("flipcoin")]
    public async Task CoinFlip()
    {
        var coin = new[] { "Heads", "Tails" };

        var uname = Context.User.GetUserPreferredName();
        await ReplyAsync($"**{uname}** flipped a coin and got **{coin[_random.Next() % 2]}**!");
        await Context.Message.DeleteAfterSeconds(seconds: 1)!;
    }

    // needs enough for highest allowed !roll dice count
    static string[] ordinals = new[]
        { "no", "a", "a pair of", "three", "four", "five", "six", "seven", "eight", "nine",
          "ten", "eleven", "twelve", "13", "14", "15", "16", "17", "18", "19",
          };

    [Command("Roll"), Priority(23)]
    [Summary("Roll one or more equal dice. Syntax: !roll [dice] {in D&D format or just a number of sides}")]
    public async Task RollDice(string? dice = null)
    {
        if (dice == null)
            dice = "1d6";
        await RollDice(dice, 0);
    }

    [Command("Roll"), Priority(23)]
    [Summary("Roll one or more equal dice. Syntax: !roll [dice] [needed] {dice in D&D format or just a number of sides}")]
    public async Task RollDice(string dice, int needed)
    {
        int sides = 0;
        int count = 1;
        if (!TryParseDice(dice, out sides, out count))
        {
            await ReplyAsync(
                "Invalid dice specified. " +
                "Please choose a number of sides between 1 and 1000, " +
                "or use D&D format, eg. 3d6 to give the total of three six-sided dice together.")
                .DeleteAfterSeconds(seconds: 10)!;
            await Context.Message.DeleteAsync();
            return;
        }

        var uname = Context.User.GetUserPreferredName();
        var rolls = new List<string>(count);
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            var roll = _random.Next(1, sides + 1);
            rolls.Add($"{roll}");
            total += roll;
        }
        var message = $"**{uname}** rolled a D{sides} and got **{total}**!";
        if (count == 1 && (total == 1 || total == sides))
            message = $"**{uname}** rolled a D{sides} and got a natural **{total}**!";
        if (count > 1 && count < ordinals.Length)
            message = $"**{uname}** rolled {ordinals[count]} D{sides} showing {rolls.ToArray().ToCommaList()} for a total of **{total}**!";
        if (needed < 1)
            message = " :game_die: " + message;
        else if (total >= needed)
            message = " :white_check_mark: " + message + " [Needed: " + needed + "]";
        else
            message = " :x: " + message + " [Needed: " + needed + "]";

        await ReplyAsync(message);
        await Context.Message.DeleteAfterSeconds(seconds: 1)!;
    }

    [Command("D20"), Priority(23)]
    [Summary("Roll a D20 dice. Syntax: !d20 [needed]")]
    public async Task RollD20(int number = 0)
    {
        await RollDice("1d20", number);
    }

    // Parse a string that describes one or more equal dice.
    // Either a simple integer number of sides of a single die, e.g., "6", or
    // a Dungeons & Dragons standard format of a set of dice, e.g., "3d6" for three six-sided dice.
    //
    public static bool TryParseDice(string dice, out int sides, out int count)
    {
        sides = 6;
        count = 1;
        if (string.IsNullOrEmpty(dice))
            return false;

        // "20"
        dice = dice.Trim();
        if (int.TryParse(dice, out sides))
        {
            if (sides < 2 || sides > 1000)
                return false;
            return true;
        }

        // "d20" or "3d20"
        var separators = new char[] { 'd', 'D' };
        var parts = dice.Split(separators, 2, StringSplitOptions.TrimEntries);
        if (parts == null || parts.Length != 2)
            return false;
        if (!int.TryParse(parts[0], out count))
            count = 1;
        if (!int.TryParse(parts[1], out sides))
            sides = 6;

        if (count < 1 || count > 10)
            return false;
        if (sides < 2 || sides > 1000)
            return false;

        return true;
    }
}
