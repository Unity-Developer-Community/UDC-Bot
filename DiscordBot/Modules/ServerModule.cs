using Discord.Commands;
using DiscordBot.Attributes;
using DiscordBot.Services;
using DiscordBot.Settings;

namespace DiscordBot.Modules;

[Group("UserModule"), Alias("")]
public class ServerModule : ModuleBase
{
    public CommandHandlingService CommandHandlingService { get; set; } = null!;
    public ServerService ServerService { get; set; } = null!;
    public BotSettings Settings { get; set; } = null!;

    [Command("Help"), Priority(100)]
    [Summary("Does what you see now.")]
    [Alias("command", "commands")]
    public async Task DisplayHelp()
    {
        var commandMessages = CommandHandlingService.GetCommandListMessages("UserModule", false, true, false);
        if (Context.Channel.Id != Settings.BotCommandsChannel.Id)
        {
            try
            {
                foreach (var message in commandMessages)
                {
                    await Context.User.SendMessageAsync(message);
                }
            }
            catch (Exception)
            {
                await ReplyAsync($"Your direct messages are disabled, please use <#{Settings.BotCommandsChannel.Id}> instead!").DeleteAfterSeconds(10)!;
            }
        }
        else
        {
            foreach (var message in commandMessages)
            {
                await ReplyAsync(message);
            }
        }
        await Context.Message.DeleteAsync();
    }

    [Command("Ping"), Priority(98)]
    [Summary("Bot latency.")]
    [Alias("pong")]
    public async Task Ping()
    {
        var message = await ReplyAsync("Pong");
        var time = message.CreatedAt.Subtract(Context.Message.Timestamp);
        await message.ModifyAsync(m =>
            m.Content = $"Pong (**{time.TotalMilliseconds}** *ms* / gateway **{ServerService.GetGatewayPing()}** *ms*)");
        await message.DeleteAfterTime(seconds: 10)!;

        await Context.Message.DeleteAfterTime(seconds: 5)!;
    }

    [Command("Members"), Priority(90)]
    [Summary("Current member count.")]
    [Alias("MemberCount")]
    public async Task MemberCount()
    {
        await ReplyAsync(
            $"We currently have {(await Context.Guild.GetUsersAsync()).Count - 1} members. Let's keep on growing as the strong community we are :muscle:");
    }
}
