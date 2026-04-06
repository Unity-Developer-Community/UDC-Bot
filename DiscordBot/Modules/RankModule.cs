using Discord.Commands;
using DiscordBot.Services;

namespace DiscordBot.Modules;

[Group("UserModule"), Alias("")]
public class RankModule : ModuleBase
{
    public DatabaseService DatabaseService { get; set; }
    public ILoggingService LoggingService { get; set; }

    [Command("Top"), Priority(6)]
    [Summary("Display top 10 users by level.")]
    [Alias("toplevel", "ranking")]
    public async Task TopLevel()
    {
        var users = await DatabaseService.Query.GetTopLevel(10);
        var userList = users.Select(user => (ulong.Parse(user.UserID), user.Level)).ToList();

        var embed = await GenerateRankEmbedFromList(userList, "Level");
        await ReplyAsync(embed: embed).DeleteAfterTime(minutes: 1);
    }

    [Command("TopKarma"), Priority(5)]
    [Summary("Display top 10 users by karma.")]
    [Alias("karmarank", "rankingkarma", "topk")]
    public async Task TopKarma()
    {
        var users = await DatabaseService.Query.GetTopKarma(10);
        var userList = users.Select(user => (ulong.Parse(user.UserID), user.Karma)).ToList();

        var embed = await GenerateRankEmbedFromList(userList, "Karma");
        await ReplyAsync(embed: embed).DeleteAfterTime(minutes: 1);
    }

    [Command("TopKarmaWeekly"), Priority(5)]
    [Summary("Display weekly top 10 users by karma.")]
    [Alias("karmarankweekly", "rankingkarmaweekly", "topkw")]
    public async Task TopKarmaWeekly()
    {
        var users = await DatabaseService.Query.GetTopKarmaWeekly(10);
        var userList = users.Select(user => (ulong.Parse(user.UserID), user.KarmaWeekly)).ToList();

        var embed = await GenerateRankEmbedFromList(userList, "Weekly Karma");
        await ReplyAsync(embed: embed).DeleteAfterTime(minutes: 1);
    }

    [Command("TopKarmaMonthly"), Priority(5)]
    [Summary("Display monthly top 10 users by karma.")]
    [Alias("karmarankmonthly", "rankingkarmamonthly", "topkm")]
    public async Task TopKarmaMonthly()
    {
        var users = await DatabaseService.Query.GetTopKarmaMonthly(10);
        var userList = users.Select(user => (ulong.Parse(user.UserID), user.KarmaMonthly)).ToList();

        var embed = await GenerateRankEmbedFromList(userList, "Monthly Karma");
        await ReplyAsync(embed: embed).DeleteAfterTime(minutes: 1);
    }

    [Command("TopKarmaYearly"), Priority(5)]
    [Summary("Display tearly top 10 users by karma.")]
    [Alias("karmaranktearly", "rankingkarmayearly", "topky")]
    public async Task TopKarmaYearly()
    {
        var users = await DatabaseService.Query.GetTopKarmaYearly(10);
        var userList = users.Select(user => (ulong.Parse(user.UserID), user.KarmaYearly)).ToList();

        var embed = await GenerateRankEmbedFromList(userList, "Yearly Karma");
        await ReplyAsync(embed: embed).DeleteAfterTime(minutes: 1);
    }

    private async Task<Embed> GenerateRankEmbedFromList(List<(ulong userID, int value)> data, string labelName)
    {
        var embedBuilder = new EmbedBuilder
        {
            Title = $"Top 10 Users by {labelName}",
            Footer = new EmbedFooterBuilder
            {
                Text = $"The best of the best, by {labelName}."
            }
        };

        try
        {
            var maxUsernameLength = data
                .Select(async x => await Context.Guild.GetUserAsync(x.userID))
                .Select(x => x.Result)
                .Max(x => (x?.Username ?? "Unknown User").Length);

            var str = "";
            for (var i = 0; i < data.Count; i++)
            {
                var user = await Context.Guild.GetUserAsync(data[i].userID);
                var username = user?.Username ?? "Unknown User";
                int rankPadding = (int)Math.Floor(Math.Log10(data.Count));

                str +=
                    $"`{(i + 1).ToString().PadLeft(rankPadding + 1)}.` **`{username.PadRight(maxUsernameLength, '\u2000')}`** `{labelName}: {data[i].value}`\n";
            }

            embedBuilder.Description = str;
        }
        catch (Exception e)
        {
            await LoggingService.LogChannelAndFile($"Failed to generate top 10 embed.\n{e}", ExtendedLogSeverity.LowWarning);
            embedBuilder.Description = "Failed to generate top 10 embed.";
        }

        return embedBuilder.Build();
    }
}
