using System.Text.RegularExpressions;
using Discord.Commands;
using DiscordBot.Attributes;

namespace DiscordBot.Modules;

[Group("UserModule"), Alias("")]
public class QuoteModule : ModuleBase
{
    [Command("Quote"), HideFromHelp]
    public async Task QuoteMessageCommand(IMessageChannel channel, ulong messageId)
    {
        await QuoteMessage(messageId: messageId, channel: channel);
    }

    [Command("Quote"), Priority(10)]
    [Summary("Quote a message. Syntax : !quote messageid (#channel)")]
    public async Task QuoteMessageCommand(ulong messageId, ulong channel)
    {
        IMessageChannel targetChannel = (IMessageChannel)await Context.Client.GetChannelAsync(channel) ?? (IMessageChannel)await Context.Client.GetChannelAsync(messageId);
        if (targetChannel == null)
        {
            await ReplyAsync("Channel or MessageID does not exist").DeleteAfterSeconds(seconds: 5);
            return;
        }

        if (targetChannel.Id == channel)
            await QuoteMessage(messageId, targetChannel);
        else
            await QuoteMessage(channel, targetChannel);
    }

    [Command("Quote"), HideFromHelp]
    [Summary("Quote a message. Syntax : !quote messageid (#channel)")]
    public async Task QuoteMessage(ulong messageId, IMessageChannel channel = null)
    {
        channel ??= Context.Channel;
        var message = await channel.GetMessageAsync(messageId);
        if (message == null)
        {
            await Context.Message.DeleteAfterSeconds(seconds: 1);
            await ReplyAsync("No message with that id found.").DeleteAfterSeconds(seconds: 4);
            return;
        }
        if (message.Author.IsBot)
        {
            await Context.Message.DeleteAfterSeconds(seconds: 2);
            return;
        }

        var messageLink = "https://discordapp.com/channels/" + Context.Guild.Id + "/" + channel.Id + "/" + messageId;

        var msgContent = message.Content;

        if (msgContent != null)
        {
            msgContent = msgContent.Truncate(1020);

            var regex = new Regex(@"\[([^\[\]\(\)]*)\]\((.*?)\)");
            var matches = regex.Matches(msgContent);

            foreach (var match in matches as IEnumerable<Match>)
            {
                msgContent = msgContent.Replace(match.Value, $"\\{match.Value}");
            }
        }

        var msgAttachment = string.Empty;
        if (message.Attachments?.Count > 0) msgAttachment = "\t📸";
        var builder = new EmbedBuilder()
            .WithColor(new Color(200, 128, 128))
            .WithTimestamp(message.Timestamp)
            .FooterQuoteBy(Context.User, message.Channel)
            .AddAuthor(message.Author);
        if (msgContent == string.Empty && msgAttachment != string.Empty) msgContent = "📸";

        msgContent += $"\n\n***[Linkback]({messageLink})***";
        builder.Description = msgContent;

        await ReplyAsync(embed: builder.Build());
        await Context.Message.DeleteAfterSeconds(1.0);
    }
}
