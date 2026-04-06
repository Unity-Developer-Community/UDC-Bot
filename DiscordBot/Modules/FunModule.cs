using System.Text;
using Discord.Commands;
using DiscordBot.Services;
using DiscordBot.Settings;
using DiscordBot.Data;

namespace DiscordBot.Modules;

[Group("UserModule"), Alias("")]
public class FunModule : ModuleBase
{
    public ILoggingService LoggingService { get; set; }
    public BotSettings Settings { get; set; }

    private readonly Random _random = new();
    private FuzzTable _slapObjects = new();
    private FuzzTable _slapFails = new();

    [Command("Slap"), Priority(21)]
    [Summary("Slap the specified user(s). Syntax : !slap @user1 [@user2 @user3...]")]
    public async Task SlapUser(params IUser[] users)
    {
        try
        {
            if (_slapObjects.Count == 0)
                _slapObjects.Load(Settings.UserModuleSlapObjectsTable);
        }
        catch (Exception e)
        {
            await LoggingService.LogChannelAndFile($"Error while loading '{Settings.UserModuleSlapObjectsTable}'.\nEx:{e}",
                ExtendedLogSeverity.LowWarning);
            return;
        }
        if (_slapObjects.Count == 0)
            _slapObjects.Add(Settings.UserModuleSlapChoices);
        if (_slapObjects.Count == 0)
            _slapObjects.Add("fish|mallet");

        if (_slapFails.Count == 0)
            _slapFails.Add(Settings.UserModuleSlapFails);
        if (_slapFails.Count == 0)
            _slapFails.Add("hurting themselves");

        var uname = Context.User.GetUserPreferredName();

        if (users == null || users.Length == 0)
        {
            await Context.Channel.SendMessageAsync(
                $"**{uname}** slaps away an invisible pest.");
            await Context.Message.DeleteAfterSeconds(seconds: 1);
            return;
        }

        var sb = new StringBuilder();
        var mentions = users.ToMentionArray().ToCommaList();

        bool fail = (_random.Next(1, 100) < 5);
        if (fail)
        {
            sb.Append($"**{uname}** tries to slap {mentions} ");
            sb.Append("around a bit with a large ");
            sb.Append(_slapObjects.Pick(true));
            sb.Append(", but misses and ends up ");
            sb.Append(_slapFails.Pick(true));
            sb.Append(".");
        }
        else
        {
            sb.Append($"**{uname}** slaps {mentions} ");
            sb.Append("around a bit with a large ");
            sb.Append(_slapObjects.Pick(true));
            sb.Append(".");
        }

        await Context.Channel.SendMessageAsync(sb.ToString());
        await Context.Message.DeleteAfterSeconds(seconds: 1);
    }

    [Command("CoinFlip"), Priority(22)]
    [Summary("Flip a coin and see the result.")]
    [Alias("flipcoin")]
    public async Task CoinFlip()
    {
        var coin = new[] { "Heads", "Tails" };

        var uname = Context.User.GetUserPreferredName();
        await ReplyAsync($"**{uname}** flipped a coin and got **{coin[_random.Next() % 2]}**!");
        await Context.Message.DeleteAfterSeconds(seconds: 1);
    }

    [Command("Roll"), Priority(23)]
    [Summary("Roll a dice. Syntax: !roll [sides]")]
    public async Task RollDice(int sides = 20)
    {
        await RollDice(sides, 0);
    }

    [Command("Roll"), Priority(23)]
    [Summary("Roll a dice. Syntax: !roll [sides] [minimum]")]
    public async Task RollDice(int sides, int number)
    {
        if (sides < 1 || sides > 1000)
        {
            await ReplyAsync("Invalid number of sides. Please choose a number between 1 and 1000.").DeleteAfterSeconds(seconds: 10);
            await Context.Message.DeleteAsync();
            return;
        }

        var uname = Context.User.GetUserPreferredName();
        var roll = _random.Next(1, sides + 1);
        var message = $"**{uname}** rolled a D{sides} and got **{roll}**!";
        if (number < 1)
            message = " :game_die: " + message;
        else if (roll >= number)
            message = " :white_check_mark: " + message + " [Needed: " + number + "]";
        else
            message = " :x: " + message + " [Needed: " + number + "]";

        await ReplyAsync(message);
        await Context.Message.DeleteAfterSeconds(seconds: 1);
    }

    [Command("D20"), Priority(23)]
    [Summary("Roll a D20 dice. Syntax: !d20 [minimum]")]
    public async Task RollD20(int number = 0)
    {
        await RollDice(20, number);
    }
}
