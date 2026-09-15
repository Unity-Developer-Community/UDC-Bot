using Discord.Interactions;
using DiscordBot.Services;

namespace DiscordBot.Modules.Profiles;

public class RankSlashModule : InteractionModuleBase<SocketInteractionContext>
{
    private const int TopCount = 10;
    private const string UnknownUser = "Unknown User";

    private const string IntervalAllTime = "all";
    private const string IntervalWeek = "week";
    private const string IntervalMonth = "month";
    private const string IntervalYear = "year";

    public DatabaseService DatabaseService { get; set; } = null!;
    public ILoggingService LoggingService { get; set; } = null!;

    [SlashCommand("top", "Show the top 10 users by level")]
    public async Task TopLevel()
    {
        var query = DatabaseService.Query;
        if (query == null)
        {
            await Context.Interaction.RespondAsync("❌ The leaderboard is unavailable right now.", ephemeral: true);
            return;
        }

        await SendRankingAsync(async () =>
        {
            var users = await query.GetTopLevel(TopCount);
            return ("Level", users.Select(user => (ulong.Parse(user.UserID), user.Level)).ToList());
        });
    }

    [SlashCommand("top-karma", "Show the top 10 users by karma")]
    public async Task TopKarma(
        [Summary("interval", "Karma interval to rank (default: all time)")]
        [Choice("All time", IntervalAllTime)]
        [Choice("Week", IntervalWeek)]
        [Choice("Month", IntervalMonth)]
        [Choice("Year", IntervalYear)]
        string interval = IntervalAllTime)
    {
        var query = DatabaseService.Query;
        if (query == null)
        {
            await Context.Interaction.RespondAsync("❌ The leaderboard is unavailable right now.", ephemeral: true);
            return;
        }

        await SendRankingAsync(() => FetchKarmaRankingAsync(query, interval));
    }

    private static async Task<(string Label, List<(ulong UserId, int Value)> Rows)> FetchKarmaRankingAsync(
        IServerUserRepo query, string interval)
    {
        switch (interval)
        {
            case IntervalWeek:
            {
                var users = await query.GetTopKarmaWeekly(TopCount);
                return ("Weekly Karma", users.Select(user => (ulong.Parse(user.UserID), user.KarmaWeekly)).ToList());
            }
            case IntervalMonth:
            {
                var users = await query.GetTopKarmaMonthly(TopCount);
                return ("Monthly Karma", users.Select(user => (ulong.Parse(user.UserID), user.KarmaMonthly)).ToList());
            }
            case IntervalYear:
            {
                var users = await query.GetTopKarmaYearly(TopCount);
                return ("Yearly Karma", users.Select(user => (ulong.Parse(user.UserID), user.KarmaYearly)).ToList());
            }
            default:
            {
                var users = await query.GetTopKarma(TopCount);
                return ("Karma", users.Select(user => (ulong.Parse(user.UserID), user.Karma)).ToList());
            }
        }
    }

    private async Task SendRankingAsync(Func<Task<(string Label, List<(ulong UserId, int Value)> Rows)>> fetch)
    {
        Embed embed;
        try
        {
            var (label, rows) = await fetch();
            embed = await BuildRankEmbedAsync(rows, label);
        }
        catch (Exception e)
        {
            // Without this the interaction would never be answered and Discord would show a timeout.
            await LoggingService.LogChannelAndFile($"Failed to generate a leaderboard embed.\n{e}",
                ExtendedLogSeverity.LowWarning);
            await Context.Interaction.RespondAsync("❌ Could not build the leaderboard right now.", ephemeral: true);
            return;
        }

        // Neither RespondAsync overload hands back the message, so the original response is fetched to
        // schedule the same one minute cleanup the text command used.
        await Context.Interaction.RespondAsync(embed: embed);
        var message = await Context.Interaction.GetOriginalResponseAsync();
        await (message.DeleteAfterTime(minutes: 1) ?? Task.CompletedTask);
    }

    private async Task<Embed> BuildRankEmbedAsync(IReadOnlyList<(ulong UserId, int Value)> rows, string labelName)
    {
        var formatted = new List<(string Name, int Value)>(rows.Count);
        foreach (var (userId, value) in rows)
        {
            // SocketGuild only exposes the asynchronous lookup through IGuild.
            var user = await ((IGuild)Context.Guild).GetUserAsync(userId);
            formatted.Add((user?.Username ?? UnknownUser, value));
        }

        return new EmbedBuilder
        {
            Title = $"Top {TopCount} Users by {labelName}",
            Description = RankEmbedFormatter.BuildDescription(formatted, labelName),
            Footer = new EmbedFooterBuilder
            {
                Text = $"The best of the best, by {labelName}."
            }
        }.Build();
    }
}
