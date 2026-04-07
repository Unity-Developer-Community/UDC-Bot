using System.Text;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Services;
using DiscordBot.Settings;
using DiscordBot.Attributes;

namespace DiscordBot.Modules;

[Group("UserModule"), Alias("")]
public class RulesModule : ModuleBase
{
    public WelcomeService WelcomeService { get; set; } = null!;
    public UpdateService UpdateService { get; set; } = null!;
    public Rules Rules { get; set; } = null!;

    [Command("Rules"), Priority(1)]
    [Summary("Rules of current channel by DM.")]
    public async Task RulesCommand()
    {
        await RulesCommand(Context.Channel);
        await Context.Message.DeleteAsync();
    }

    [Command("Rules"), Priority(99)]
    [Summary("Rules of the mentioned channel by DM. !rules #channel")]
    [Alias("rule")]
    public async Task RulesCommand(IMessageChannel channel)
    {
        var rule = Rules.Channel.First(x => x.Id == channel.Id);
        var dm = await Context.User.CreateDMChannelAsync();
        bool sentMessage = false;

        sentMessage = await dm.TrySendMessage($"{rule.Header}{(rule.Content.Length > 0 ? rule.Content : $"There is no special rule for {channel.Name} channel.\nPlease follow global rules (you can get them by typing `!globalrules`)")}");
        if (!sentMessage)
            await ReplyAsync("Could not send rules, your DMs are disabled.").DeleteAfterSeconds(seconds: 10);
    }

    [Command("GlobalRules"), Priority(99)]
    [Summary("Global Rules by DM.")]
    public async Task GlobalRules(int seconds = 60)
    {
        var globalRules = Rules.Channel.First(x => x.Id == 0).Content;
        var dm = await Context.User.CreateDMChannelAsync();
        await Context.Message.DeleteAsync();
        if (!await dm.TrySendMessage(globalRules))
        {
            await ReplyAsync("Could not send rules, your DMs are disabled.").DeleteAfterSeconds(seconds: 10);
        }
    }

    [Command("Welcome"), Priority(1)]
    [Summary("Condensed version of the rules and links to quality resources.")]
    public async Task ServerWelcome()
    {
        if (!await WelcomeService.DMFormattedWelcome(Context.User as SocketGuildUser))
        {
            await ReplyAsync("Could not send welcome, your DMs are disabled.").DeleteAfterSeconds(seconds: 2);
        }
        await Context.Message.DeleteAfterSeconds(seconds: 4);
    }

    [Command("Channels"), Priority(92)]
    [Summary("Description of the channels by DM.")]
    public async Task ChannelsDescription()
    {
        var channelData = Rules.Channel;
        var sb = new StringBuilder();
        foreach (var c in channelData)
            sb.Append((await Context.Guild.GetTextChannelAsync(c.Id))?.Mention).Append(" - ").Append(c.Header).Append("\n");

        var dm = await Context.User.CreateDMChannelAsync();

        var messages = sb.ToString().MessageSplitToSize();
        await Context.Message.DeleteAsync();
        foreach (var message in messages)
        {
            if (!await dm.TrySendMessage(message))
            {
                await ReplyAsync("Could not send channel descriptions, your DMs are disabled.").DeleteAfterSeconds(seconds: 10);
                break;
            }
        }
    }

    [Command("FAQ")]
    [Summary("Searches UDC FAQs. Syntax : !faq \"query\"")]
    public async Task SearchFaqs(params string[] queries)
    {
        var faqDataList = UpdateService.GetFaqData();

        if (queries.Length == 1 && ParseNumber(queries[0]) > 0)
        {
            var id = ParseNumber(queries[0]) - 1;
            if (id < faqDataList.Count)
                await ReplyAsync(embed: GetFaqEmbed(faqDataList[id]));
            else
                await ReplyAsync("Invalid FAQ ID selected.");
        }
        else if (queries.Length > 0 && !(queries.Length == 1 && queries[0].Equals("list")))
        {
            var minimumScore = double.MaxValue;
            FaqData? mostSimilarFaq = null;
            var query = string.Join(" ", queries);

            foreach (var faq in faqDataList)
            {
                foreach (var keyword in faq.Keywords)
                {
                    var curScore = CalculateScore(keyword, query);
                    if (curScore < minimumScore)
                    {
                        minimumScore = curScore;
                        mostSimilarFaq = faq;
                    }
                }
            }

            if (mostSimilarFaq != null)
                await ReplyAsync(embed: GetFaqEmbed(mostSimilarFaq));
            else
                await ReplyAsync("No FAQs Found.");
        }
        else
            await ListFaqs(faqDataList);
    }

    private async Task ListFaqs(List<FaqData> faqs)
    {
        var sb = new StringBuilder(faqs.Count);
        var index = 1;
        var keywordSb = new StringBuilder();
        foreach (var faq in faqs)
        {
            sb.Append(FormatFaq(index, faq) + "\n");
            keywordSb.Append("[");
            for (var i = 0; i < faq.Keywords.Length; i++)
            {
                keywordSb.Append(faq.Keywords[i]);
                keywordSb.Append(i < faq.Keywords.Length - 1 ? ", " : "]\n\n");
            }

            index++;
            sb.Append(keywordSb);
            keywordSb.Clear();
        }

        await ReplyAsync(sb.ToString()).DeleteAfterTime(minutes: 3);
    }

    private Embed GetFaqEmbed(FaqData faq)
    {
        var builder = new EmbedBuilder()
            .WithTitle($"{faq.Question}")
            .WithDescription($"{faq.Answer}")
            .WithColor(new Color(0x33CC00));
        return builder.Build();
    }

    private string FormatFaq(int id, FaqData faq) => $"{id}. **{faq.Question}** - {faq.Answer}";

    private double CalculateScore(string s1, string s2)
    {
        double curScore = 0;
        var i = 0;

        foreach (var q in s1.Split(' '))
        {
            foreach (var x in s2.Split(' '))
            {
                i++;
                if (x.Equals(q))
                    curScore -= 50;
                else
                    curScore += x.CalculateLevenshteinDistance(q);
            }
        }

        curScore /= i;
        return curScore;
    }

    private int ParseNumber(string s)
    {
        if (int.TryParse(s, out int id)) return id;
        return -1;
    }
}
