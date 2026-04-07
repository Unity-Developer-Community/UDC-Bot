using Discord.Commands;
using DiscordBot.Attributes;
using DiscordBot.Services;

// ReSharper disable all UnusedMember.Local
namespace DiscordBot.Modules;

[RequireAdmin]
public class EmbedModule : ModuleBase
{
    public EmbedParsingService EmbedParsingService { get; set; } = null!;

    /// <summary>
    /// Generate an embed
    /// </summary>
    [RequireAdmin]
    [Command("embed"), Summary("Generate an embed.")]
    public async Task EmbedCommand(IMessageChannel? channel = null, ulong messageId = 0)
    {
        await Context.Message.DeleteAsync();
        channel ??= Context.Channel;

        if (Context.Message.Attachments.Count < 1)
        {
            await ReplyAsync($"{Context.User.Mention}, you must provide a JSON file or a JSON url.").DeleteAfterSeconds(5);
            return;
        }
        var attachment = Context.Message.Attachments.ElementAt(0);
        var embed = await EmbedParsingService.BuildEmbedFromUrl(attachment.Url);

        await SendEmbedToChannel(embed, channel, messageId);
    }

    [Command("embed"), Summary("Generate an embed from an URL (hastebin).")]
    public async Task EmbedCommand(string url, IMessageChannel? channel = null, ulong messageId = 0)
    {
        await Context.Message.DeleteAsync();
        channel ??= Context.Channel;
        Discord.Embed? builtEmbed = await TryGetEmbedFromUrl(url);
        if (builtEmbed != null)
            await SendEmbedToChannel(builtEmbed, channel, messageId);
    }

    private async Task<Discord.Embed?> TryGetEmbedFromUrl(string url)
    {
        bool result = Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
                      && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        if (!result)
        {
            await ReplyAsync($"{Context.User.Mention}, the parameter is not a valid URL.").DeleteAfterSeconds(5);
            return null;
        }
        if (!EmbedParsingService.IsValidHost(uriResult.Host))
        {
            await ReplyAsync($"{Context.User.Mention}, supported URLs: [https://hastebin.com, https://pastebin.com, https://gdl.space, https://hastepaste.com, http://pastie.org].").DeleteAfterSeconds(5);
            return null;
        }
        string downloadUrl = EmbedParsingService.GetDownloadUrl(uriResult);
        var builtEmbed = await EmbedParsingService.BuildEmbedFromUrl(downloadUrl);
        if (builtEmbed.Length == 0)
        {
            await ReplyAsync("Failed to generate embed from url.").DeleteAfterSeconds(seconds: 10f);
            return null;
        }
        return builtEmbed;
    }

    private readonly IEmote _thumbUpEmote = new Emoji("👍");

    private async Task SendEmbedToChannel(Discord.Embed embed, IMessageChannel channel, ulong messageId = 0)
    {
        if (embed == null || embed.Length <= 0)
        {
            await ReplyAsync("Embed is improperly formatted or corrupt.");
            return;
        }

        // If context.channel is same as channel we don't need to confirm details
        if (Context.Channel != channel)
        {
            // Confirm with user it is correct
            var tempEmbed = await ReplyAsync(embed: embed);
            var message = await ReplyAsync("If correct, react to this message within 20 seconds to continue.");
            await message.AddReactionAsync(_thumbUpEmote);
            // 20 seconds wait?
            bool confirmedEmbed = false;
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(2000);
                var reactions = await message.GetReactionUsersAsync(_thumbUpEmote, 10).FlattenAsync();
                if (reactions.Count() > 1)
                {
                    foreach (var reaction in reactions)
                    {
                        if (reaction.Id == Context.User.Id)
                        {
                            confirmedEmbed = true;
                            break;
                        }
                    }
                }

                if (confirmedEmbed) break;
            }

            await tempEmbed.DeleteAsync();
            await message.DeleteAsync();
            // If no reaction, we assume it was bad and abort
            if (!confirmedEmbed)
            {
                await ReplyAsync("Reaction not detected, embed aborted.").DeleteAfterSeconds(seconds: 5);
                return;
            }
        }

        if (messageId != 0)
        {
            var messageToEdit = await channel.GetMessageAsync(messageId) as IUserMessage;
            if (messageToEdit == null)
            {
                await ReplyAsync($"Bot doesn't own the message ID ``{messageId}`` used").DeleteAfterSeconds(5);
                return;
            }

            // Modify the old message, we clear any text it might have had.
            await messageToEdit.ModifyAsync(x =>
            {
                x.Content = "";
                x.Embed = embed;
            });
            await ReplyAsync("Message replaced!").DeleteAfterSeconds(5);
        }
        else
        {
            await channel.SendMessageAsync(embed: embed);
            if (Context.Channel != channel)
                await ReplyAsync("Embed Posted!").DeleteAfterSeconds(5);
        }
    }
}