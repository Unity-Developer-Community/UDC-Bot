using Discord.Commands;
using DiscordBot.Services;
using DiscordBot.Settings;

// ReSharper disable all UnusedMember.Local
namespace DiscordBot.Modules;

public class TicketModule : ModuleBase
{
    #region Dependency Injection

    public CommandHandlingService CommandHandlingService { get; set; }
    public BotSettings Settings { get; set; }
        
    #endregion

    /// <summary>
    ///     Creates a private channel only accessable by the mods, admins, and the user who used the command.
    ///     One command, no args, simple.
    /// </summary>
    [Command("Complain"), Alias("complains", "complaint"), Summary("Opens a private channel to complain.")]
    public async Task Complaint()
    {
        await Context.Message.DeleteAsync();

        var categoryExist = (await Context.Guild.GetCategoriesAsync()).Any(category => category.Id == Settings.ComplaintCategoryId);

        var hash = Context.User.Id.ToString().GetSha256().Substring(0, 8);
        var channelName = ParseToDiscordChannel($"{Settings.ComplaintChannelPrefix}-{hash}");

        var channels = await Context.Guild.GetChannelsAsync();
        // Check if channel with same name already exist in the Complaint Category (if it exists).
        if (channels.Any(channel => channel.Name == channelName && (!categoryExist || ((INestedChannel)channel).CategoryId == Settings.ComplaintCategoryId)))
        {
            await ReplyAsync($"{Context.User.Mention}, you already have an open complaint! Please use that channel!")
                .DeleteAfterSeconds(15);
            return;
        }

        var newChannel = await Context.Guild.CreateTextChannelAsync(channelName, x =>
        {
            if (categoryExist) x.CategoryId = Settings.ComplaintCategoryId;
        });

        var userPerms = new OverwritePermissions(viewChannel: PermValue.Allow);
        var modRole = Context.Guild.Roles.First(r => r.Id == Settings.ModeratorRoleId);
        await newChannel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, new OverwritePermissions(viewChannel: PermValue.Deny));
        await newChannel.AddPermissionOverwriteAsync(Context.User, userPerms);
        await newChannel.AddPermissionOverwriteAsync(modRole, userPerms);
        await newChannel.AddPermissionOverwriteAsync(Context.Client.CurrentUser, userPerms);

        await newChannel.SendMessageAsync(
            $"The content of this conversation will stay strictly between you {Context.User.Mention} and the {modRole.Mention}.\n" +
            "Please stay civil, any insults or offensive language could see you punished.\n" +
            "Do not ping anyone and wait until a staff member is free to examine your complaint.");
        await newChannel.SendMessageAsync($"A staff member will be able to close this chat by doing `!ticket close`.");
    }

    /// <summary>
    ///     Archives the ticket.
    /// </summary>
    [Command("Ticket Close"), Alias("Ticket end", "Ticket done", "Ticket archive"), Summary("Closes the ticket.")]
    [RequireModerator]
    public async Task Close()
    {
        await Context.Message.DeleteAsync();

        if (!Context.Channel.Name.StartsWith(Settings.ComplaintChannelPrefix.ToLower())) return;

        var categoryExist = (await Context.Guild.GetCategoriesAsync()).Any(category => category.Id == Settings.ClosedComplaintCategoryId);

        var currentChannel = await Context.Guild.GetChannelAsync(Context.Channel.Id);

        // Remove the override permissions for the user who opened the complaint.
        foreach (var a in currentChannel.PermissionOverwrites)
        {
            if (a.TargetType != PermissionTarget.User) continue;

            var user = await Context.Guild.GetUserAsync(a.TargetId);
            await currentChannel.RemovePermissionOverwriteAsync(user);
        }

        var newName = Settings.ClosedComplaintChannelPrefix + currentChannel.Name;
        await currentChannel.ModifyAsync(x =>
        {
            if (categoryExist) x.CategoryId = Settings.ClosedComplaintCategoryId;
            x.Name = newName;
        });
    }

    /// <summary>
    ///     Delete the ticket.
    /// </summary>
    [Command("Ticket Delete"), Summary("Deletes the ticket.")]
    [RequireAdmin]
    private async Task Delete()
    {
        await Context.Message.DeleteAsync();

        if (Context.Channel.Name.StartsWith(Settings.ComplaintChannelPrefix.ToLower()) ||
            Context.Channel.Name.StartsWith(Settings.ClosedComplaintChannelPrefix.ToLower()))
        {
            await Context.Guild.GetChannelAsync(Context.Channel.Id).Result.DeleteAsync();
        }
    }

    private string ParseToDiscordChannel(string channelName) => channelName.ToLower().Replace(" ", "-");

    #region CommandList
    [RequireModerator]
    [Summary("Does what you see now.")]
    [Command("Ticket Help")]
    public async Task TicketHelp()
    {
        foreach (var message in CommandHandlingService.GetCommandListMessages("TicketModule", true, true, false))
        {
            await ReplyAsync(message);
        }
    }
    #endregion
}