using Discord.Commands;
using DiscordBot.Services;
using DiscordBot.Settings;

namespace DiscordBot.Modules;

[Group("ReactRole")]
public class ReactionRoleModule : ModuleBase
{
    #region Dependency Injection

    public CommandHandlingService CommandHandlingService { get; set; }
    public ILoggingService LoggingService { get; set; }
    public ReactRoleService ReactRoleService { get; set; }

    #endregion
        
    #region Config

    [RequireUserPermission(GuildPermission.Administrator)]
    [Summary("Duration between bulk role changes in ms. Default: 5000")]
    [Command("Delay")]
    [Priority(99)]
    public async Task SetReactRoleDelay(uint delay)
    {
        if (ReactRoleService.SetReactRoleDelay(delay))
            await Context.Message.AddReactionAsync(new Emoji("👍"));
    }

    [RequireUserPermission(GuildPermission.Administrator)]
    [Summary("Log all updates, gets spammy. Default: false")]
    [Command("Log")]
    [Priority(98)]
    public async Task SetReactLogState(bool state)
    {
        if (ReactRoleService.SetReactLogState(state))
            await Context.Message.AddReactionAsync(new Emoji("👍"));
    }

    #endregion

    #region MessageMaking

    [RequireUserPermission(GuildPermission.Administrator)]
    [Summary("Begins message setup.")]
    [Command("NewMessage")]
    [Priority(0)]
    public async Task NewMessageSetup(IMessageChannel channel, ulong messageId, string description = "")
    {
        if (ReactRoleService.IsPreparingMessage)
            await ReplyAsync("A message is already being prepared, please finish.");
        else
        {
            var linkedMessage = await channel.GetMessageAsync(messageId);
            if (linkedMessage == null)
            {
                await ReplyAsync($"Message ID \"{messageId}\" passed in does not exist");
                return;
            }

            ReactRoleService.NewMessage = new UserReactMessage
            {
                ChannelId = channel.Id,
                Description = description,
                MessageId = messageId
            };
            await ReplyAsync(
                $"Setup began, Reaction roles will be attached to {linkedMessage.GetJumpUrl()}");
            await ReplyAsync(
                "Use `ReactRole Emote` \"Role\" \"Emoji\" \"Name (optional)\" to add emotes.");
        }
    }

    [RequireUserPermission(GuildPermission.Administrator)]
    [Summary("Allows making changes to previously saved messages.")]
    [Command("EditMessage")]
    [Priority(1)]
    public async Task EditMessageSetup(ulong messageId, string description = "")
    {
        if (ReactRoleService.IsPreparingMessage)
            await ReplyAsync("A message is already being prepared, please finish.");
        else
        {
            var foundMessage =
                ReactRoleService.ReactSettings.UserReactRoleList.Find(reactMessage =>
                    reactMessage.MessageId == messageId);
            if (foundMessage == null)
            {
                await ReplyAsync("No message with that ID exists");
                return;
            }

            ReactRoleService.NewMessage = foundMessage;
            await ReplyAsync(
                $"Message linked, future changes will be made to {foundMessage.MessageLinkBack(Context.Guild.Id)}");
        }
    }

    [RequireUserPermission(GuildPermission.Administrator)]
    [Summary("Adds/Removes Roles/Emotes to the prepared message. Removes role if emoteId is 0")]
    [Command("Emote")]
    [Priority(2)]
    public async Task AddEmoteRoles(IRole role, string emoteString = "", string name = "")
    {
        if (!ReactRoleService.IsPreparingMessage)
        {
            await ReplyAsync("No message is being prepared, use NewMessage first!");
            return;
        }

        // If empty we check the message for the role passed in, if it exists we remove that role from the message.
        if (emoteString == string.Empty)
        {
            var reactionRemove =
                ReactRoleService.NewMessage.Reactions?.Find(reactRole => reactRole.RoleId == role.Id);
            if (reactionRemove == null)
            {
                await ReplyAsync($"Role {role.Name} was not attached to this message.");
                return;
            }

            ReactRoleService.NewMessage.Reactions.Remove(reactionRemove);
            await ReplyAsync($"Removed Role {role.Name} from message.");
            return;
        }

        // Try pull the Emote ID from the emote used.
        Emote tempEmote;
        if (!Emote.TryParse(emoteString, out tempEmote))
        {
            await ReplyAsync($"Emote ({emoteString}) does not exist in this server.");
            return;
        }

        // We make sure we have access to it on this server
        var emote = await Context.Guild.GetEmoteAsync(tempEmote.Id);
        if (emote == null)
        {
            await ReplyAsync($"Failed to use ({emoteString}), unknown error.");
            return;
        }

        if (name == string.Empty)
            name = emote.Name;

        // Add our Reaction Role
        ReactRoleService.NewMessage.Reactions ??= new List<ReactRole>();
        var newRole = new ReactRole(name, role.Id, emote.Id);
        ReactRoleService.NewMessage.Reactions.Add(newRole);

        await Context.Message.AddReactionAsync(emote);
        await Context.Message.AddReactionAsync(new Emoji("👍"));
    }

    [RequireUserPermission(GuildPermission.Administrator)]
    [Summary("Preview the message being prepared.")]
    [Command("Preview")]
    [Priority(3)]
    public async Task PreviewNewReactionMessage()
    {
        if (!ReactRoleService.IsPreparingMessage)
        {
            await ReplyAsync("No message is being prepared, use NewMessage first!");
            return;
        }

        var config = ReactRoleService.NewMessage;
        foreach (var configReaction in config.Reactions)
        {
            var emote = await Context.Guild.GetEmoteAsync(configReaction.EmojiId);
            await Context.Message.AddReactionAsync(emote);
        }
    }

    [RequireUserPermission(GuildPermission.Administrator)]
    [Summary("Cancel the current reaction roles.")]
    [Command("Cancel")]
    [Priority(4)]
    public async Task CancelNewReactionMessage()
    {
        if (!ReactRoleService.IsPreparingMessage)
        {
            await ReplyAsync("No message is being prepared!");
            return;
        }

        ReactRoleService.NewMessage = null;
        await Context.Message.AddReactionAsync(new Emoji("👍"));
    }

    [RequireUserPermission(GuildPermission.Administrator)]
    [Summary("Saves the message being prepared.")]
    [Command("Save")]
    [Priority(10)]
    public async Task SaveNewReactionMessage()
    {
        if (!ReactRoleService.IsPreparingMessage)
        {
            await ReplyAsync("No message is being prepared, use NewMessage first!");
            return;
        }

        await ReplyAsync("Saving Values, use ***reactrole restart*** to enable the changes.");
        ReactRoleService.StoreNewMessage();
    }

    #endregion

    #region Additional Functions

    [RequireUserPermission(GuildPermission.Administrator)]
    [Summary("Delete a stored ReactionRole Configuration.")]
    [Command("DeleteConfig")]
    [Priority(11)]
    public async Task DeleteReactionRoleConfig(uint messageId)
    {
        var foundMessage =
            ReactRoleService.ReactSettings.UserReactRoleList.Find(reactMessage =>
                reactMessage.MessageId == messageId);
        if (foundMessage == null)
        {
            await ReplyAsync("No message with that ID exists");
            return;
        }

        ReactRoleService.ReactSettings.UserReactRoleList.Remove(foundMessage);
        await ReplyAsync(
            "Deleted the configuration for that ID, use \"!reactrole restart\" to enable these changes.");
        await LoggingService.LogAction(
            $"{Context.User} deleted the reactionrole configuration for `{foundMessage}`.");
    }

    [RequireUserPermission(GuildPermission.Administrator)]
    [Summary("Lists out all current reaction messages.")]
    [Command("List")]
    [Priority(80)]
    public async Task ListReactionRoleConfig()
    {
        var messageList = ReactRoleService.ReactSettings.UserReactRoleList;
        if (messageList.Count == 0)
        {
            await ReplyAsync("No messages currently stored.");
            return;
        }

        foreach (var reactMessage in messageList)
        {
            var linkedInfoMessage =
                await ReplyAsync(
                    $"Linked Location: {reactMessage.MessageLinkBack(Context.Guild.Id)} which should contain {reactMessage.RoleCount()} emotes.\n");
            foreach (var reactRole in reactMessage.Reactions)
            {
                var emote = await Context.Guild.GetEmoteAsync(reactRole.EmojiId);
                if (emote != null)
                    await linkedInfoMessage.AddReactionAsync(emote);
            }
        }
    }

    [RequireUserPermission(GuildPermission.Administrator)]
    [Summary("Restarts the ReactRoleService.")]
    [Command("Restart")]
    [Priority(90)]
    public async Task ReactRestartService()
    {
        await LoggingService.LogAction($"{Context.User} restarted the ReactionRole service.");
        await ReplyAsync("Reaction role service is restarting.");
        var results = await ReactRoleService.Restart();
        if (results)
            await Context.Message.AddReactionAsync(new Emoji("👍"));
        else
            await ReplyAsync("Failed to restart reaction role service.");
    }

    #endregion

    #region CommandList

    [RequireUserPermission(GuildPermission.Administrator)]
    [Summary("Does what you see now.")]
    [Command("Help")]
    [Priority(100)]
    public async Task ReactionRoleHelp()
    {
        foreach (var textMessage in CommandHandlingService.GetCommandListMessages("ReactRole"))
        {
            await ReplyAsync(textMessage);
        }
    }

    #endregion
}