using System.Globalization;
using Discord.Commands;
using DiscordBot.Attributes;
using DiscordBot.Utils;

namespace DiscordBot.Modules.Profiles;

[Group("UserModule"), Alias("")]
public class BirthdayModule : ModuleBase
{
    public IWebClient WebClient { get; set; } = null!;

    [Command("Birthday"), HideFromHelp]
    [Summary("Display next member birthday.")]
    [Alias("bday")]
    public async Task Birthday()
    {
        const string nextBirthday = "https://docs.google.com/spreadsheets/d/10iGiKcrBl1fjoBNTzdtjEVYEgOfTveRXdI5cybRTnj4/gviz/tq?tqx=out:html&range=C15:C15";

        var tableText = await WebClient.GetHtmlNodeInnerText(nextBirthday, "/html/body/table/tr[2]/td");
        var message = $"**{tableText}**";

        await (ReplyAsync(message).DeleteAfterTime(minutes: 3) ?? Task.CompletedTask);
        await (Context.Message.DeleteAfterTime(minutes: 3) ?? Task.CompletedTask);
    }

    [Command("Birthday"), Priority(27)]
    [Summary("Display birthday of mentioned user. Syntax : !birthday @user")]
    [Alias("bday")]
    public async Task Birthday(IUser user)
    {
        var searchName = user.Username;
        const string birthdayTable = "https://docs.google.com/spreadsheets/d/10iGiKcrBl1fjoBNTzdtjEVYEgOfTveRXdI5cybRTnj4/gviz/tq?tqx=out:html&gid=318080247&range=B:D";
        var relevantNodes = await WebClient.GetHtmlNodes(birthdayTable, "/html/body/table/tr");

        var birthdate = default(DateTime);

        HtmlAgilityPack.HtmlNode? matchedNode = null;
        var matchedLength = int.MaxValue;

        if (relevantNodes != null)
        {
            foreach (var row in relevantNodes)
            {
                var nameNode = row.SelectSingleNode("td[2]");
                if (nameNode == null) continue;
                var name = nameNode.InnerText;

                if (!name.ToLower().Contains(searchName.ToLower()) || name.Length >= matchedLength)
                    continue;

                matchedNode = row;
                matchedLength = name.Length;
                if (name.Length == searchName.Length) break;
            }
        }

        if (matchedNode != null)
        {
            var dateNode = matchedNode.SelectSingleNode("td[1]");
            var yearNode = matchedNode.SelectSingleNode("td[3]");

            if (dateNode != null && yearNode != null)
            {
                var provider = CultureInfo.InvariantCulture;
                var wrongFormat = "M/d/yyyy";

                var dateString = dateNode.InnerText;
                if (!yearNode.InnerText.Contains("&nbsp;")) dateString = dateString + "/" + yearNode.InnerText;

                dateString = dateString.Trim();

                try
                {
                    birthdate = DateTime.ParseExact(dateString, wrongFormat, provider);
                }
                catch (FormatException)
                {
                    birthdate = DateTime.ParseExact(dateString, "M/d", provider);
                }
            }
        }

        if (birthdate == default)
        {
            await (ReplyAsync(
                    $"Sorry, I couldn't find **{searchName}**'s birthday date. They can add it at https://docs.google.com/forms/d/e/1FAIpQLSfUglZtJ3pyMwhRk5jApYpvqT3EtKmLBXijCXYNwHY-v-lKxQ/viewform !")
                .DeleteAfterSeconds(30) ?? Task.CompletedTask);
        }
        else
        {
            var date = birthdate.ToUnixTimestamp();
            var message =
                $"**{searchName}**'s birthdate: __**{birthdate.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture)}**__ " +
                $"({(int)((DateTime.Now - birthdate).TotalDays / 365)}yo)";

            await (ReplyAsync(message).DeleteAfterTime(minutes: 3) ?? Task.CompletedTask);
        }

        await (Context.Message.DeleteAfterTime(minutes: 3) ?? Task.CompletedTask);
    }
}
