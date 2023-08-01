using System.Text;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Services;
using DiscordBot.Settings;
using Pathoschild.NaturalTimeParser.Parser;
using DiscordBot.Attributes;
using DiscordBot.Utils;
using Org.BouncyCastle.Asn1.Cms;

namespace DiscordBot.Modules;

public class ModerationModule : ModuleBase
{
    #region Dependency Injection

    public CommandHandlingService CommandHandlingService { get; set; }
    public DatabaseService DatabaseService { get; set; }
    public ILoggingService LoggingService { get; set; }
    public Rules Rules { get; set; }
    public BotSettings Settings { get; set; }
    public UserService UserService { get; set; }
    public ModerationService ModerationService { get; set; }
    
    #endregion
    
    private async Task<bool> IsModerationEnabled()
    {
        if (Settings.ModeratorCommandsEnabled) return true;
        if (await Context.Guild.GetChannelAsync(Settings.BotAnnouncementChannel.Id)is IMessageChannel botAnnouncementChannel)
        {
            var sentMessage = await botAnnouncementChannel.SendMessageAsync($"{Context.User.Mention} some moderation commands are disabled, try using Wick.");
            await Context.Message.DeleteAsync();
            await sentMessage.DeleteAfterSeconds(seconds: 60);
        }
        return false;
    }

    [Command("Mute")]
    [Summary("Mute a user for a fixed duration.")]
    [Alias("shutup", "stfu")]
    [RequireModerator]
    public async Task MuteUser(IUser user, uint arg)
    {
        if (!await IsModerationEnabled()) return;

        await Context.Message.DeleteAsync();

        var u = user as IGuildUser;
        if (u != null && u.RoleIds.Contains(Settings.MutedRoleId)) return;

        await u.AddRoleAsync(Context.Guild.GetRole(Settings.MutedRoleId));

        var reply = await ReplyAsync($"User {user} has been muted for {Utils.Utils.FormatTime(arg)} ({arg} seconds).");
        await LoggingService.LogAction(
            $"{Context.User.Username} has muted {u.Username} ({u.Id}) for {Utils.Utils.FormatTime(arg)} ({arg} seconds).");

        UserService.MutedUsers.AddCooldown(u.Id, (int)arg, ignoreExisting: true);

        await UserService.MutedUsers.AwaitCooldown(u.Id);
        await reply.DeleteAsync();
        await UnmuteUser(user, true);
    }

    [Command("Mute")]
    [Summary("Mute a user for a fixed duration.")]
    [Alias("shutup", "stfu")]
    [RequireModerator]
    public async Task MuteUser(IUser user, string duration, params string[] messages)
    {
        if (!await IsModerationEnabled()) return;
        try
        {
            var dt = DateTime.Now.Offset(duration);
            if (dt < DateTime.Now)
            {
                await ReplyAsync("Invalid DateTime specified.");
                return;
            }

            await MuteUser(user, (uint)Math.Round((dt - DateTime.Now).TotalSeconds), messages);
        }
        catch (Exception)
        {
            await ReplyAsync("Invalid DateTime specified.");
            await Context.Message.DeleteAsync();
        }
    }

    [Command("Mute")]
    [Summary("Mute a user for a fixed duration.")]
    [Alias("shutup", "stfu")]
    [RequireModerator]
    public async Task MuteUser(IUser user, uint seconds, params string[] messages)
    {
        if (!await IsModerationEnabled()) return;
        var message = string.Join(' ', messages);

        await Context.Message.DeleteAsync();

        var u = user as IGuildUser;
        if (u != null && u.RoleIds.Contains(Settings.MutedRoleId)) return;

        await u.AddRoleAsync(Context.Guild.GetRole(Settings.MutedRoleId));

        var reply =
            await ReplyAsync($"User {user} has been muted for {Utils.Utils.FormatTime(seconds)} ({seconds} seconds). Reason : {message}");
        await LoggingService.LogAction(
            $"{Context.User.Username} has muted {u.Username} ({u.Id}) for {Utils.Utils.FormatTime(seconds)} ({seconds} seconds). Reason : {message}");

        var dm = await user.CreateDMChannelAsync(new RequestOptions());
        if (!await dm.TrySendMessage(
                $"You have been muted from UDC for **{Utils.Utils.FormatTime(seconds)}** for the following reason : **{message}**. " +
                "This is not appealable and any tentative to avoid it will result in your permanent ban."))
        {
            if (await Context.Guild.GetChannelAsync(Settings.BotCommandsChannel.Id)is ISocketMessageChannel botCommandChannel)
                await botCommandChannel.SendMessageAsync(
                    $"I could not DM you {user.Mention}!\nYou have been muted from UDC for **{Utils.Utils.FormatTime(seconds)}** for the following reason : **{message}**. " +
                    "This is not appealable and any tentative to avoid it will result in your permanent ban.");
            await LoggingService.LogAction($"User {user.Username} has DM blocked and the mute reason couldn't be sent.", true, false);
        }

        UserService.MutedUsers.AddCooldown(u.Id, (int)seconds, ignoreExisting: true);
        await UserService.MutedUsers.AwaitCooldown(u.Id);

        await UnmuteUser(user, true);
        reply?.DeleteAsync();
    }

    [Command("Unmute")]
    [Summary("Unmute a muted user.")]
    [RequireModerator]
    public async Task UnmuteUser(IUser user, bool fromMute = false)
    {
        var u = user as IGuildUser;

        if (!fromMute && u == Context.Message.Author)
        {
            await ReplyAsync("You can't unmute yourself.").DeleteAfterSeconds(30);
            return;
        }

        if (!fromMute && Context != null && Context.Message != null)
            await Context.Message.DeleteAsync();

        UserService.MutedUsers.Remove(user.Id);
        await u.RemoveRoleAsync(Context.Guild.GetRole(Settings.MutedRoleId));
        var reply = await ReplyAsync("User " + user + " has been unmuted.");
        reply?.DeleteAfterSeconds(10d);
    }

    [Command("AddRole")]
    [Summary("Add a role to a user.")]
    [Alias("roleadd")]
    [RequireModerator]
    public async Task AddRole(IRole role, IUser user)
    {
        var contextUser = Context.User as SocketGuildUser;
        await Context.Message.DeleteAsync();

        if (Settings.UserAssignableRoles.Roles.Contains(role.Name))
        {
            var u = user as IGuildUser;
            await u.AddRoleAsync(role);
            await ReplyAsync("Role " + role + " has been added to " + user).DeleteAfterTime(minutes: 5);
            await LoggingService.LogAction($"{contextUser.Username} has added role {role} to {u.Username}");
            return;
        }

        await ReplyAsync($"Bot cannot add {role.Name} role. Administrator must do it manually.").DeleteAfterSeconds(25);
    }

    [Command("RemoveRole")]
    [Summary("Remove a role from a user.")]
    [Alias("roleremove")]
    [RequireModerator]
    public async Task RemoveRole(IRole role, IUser user)
    {
        var contextUser = Context.User as SocketGuildUser;
        await Context.Message.DeleteAsync();

        if (Settings.UserAssignableRoles.Roles.Contains(role.Name))
        {
            var u = user as IGuildUser;

            await u.RemoveRoleAsync(role);
            await ReplyAsync("Role " + role + " has been removed from " + user).DeleteAfterTime(minutes: 5);
            await LoggingService.LogAction($"{contextUser.Username} has removed role {role} from {u.Username}");
            return;
        }

        await ReplyAsync($"Bot cannot remove {role.Name} role. Administrator must do it manually.").DeleteAfterSeconds(25);
    }

    [Command("Clear")]
    [Summary("Removes the last x messages from channel.")]
    [Alias("clean", "nuke", "purge")]
    [RequireModerator]
    public async Task ClearMessages(int count)
    {
        var channel = Context.Channel as ITextChannel;

        var messages = await channel.GetMessagesAsync(count + 1).FlattenAsync();
        await channel.DeleteMessagesAsync(messages);

        await ReplyAsync("Messages deleted.").DeleteAfterSeconds(seconds: 5);
        await LoggingService.LogAction($"{Context.User.Username} has removed {count} messages from {Context.Channel.Name}");
    }

    [Command("Clear")]
    [Summary("Removes messages until the message at the specified id.")]
    [Alias("clean", "nuke", "purge")]
    [RequireModerator]
    public async Task ClearMessages(ulong messageId)
    {
        var channel = (ITextChannel)Context.Channel;

        var messages = await channel.GetMessagesAsync(messageId, Direction.After).FlattenAsync();
        var enumerable = messages.ToList();
        await channel.DeleteMessagesAsync(enumerable);

        await ReplyAsync("Messages deleted.").DeleteAfterSeconds(seconds: 5);
        await LoggingService.LogAction($"{Context.User.Username} has removed {enumerable.Count} messages from {Context.Channel.Name}");
    }

    [Command("Kick")]
    [Summary("Kick a user.")]
    [RequireUserPermission(GuildPermission.KickMembers)]
    internal async Task KickUser(IUser user)
    {
        if (!await IsModerationEnabled()) return;

        var u = user as IGuildUser;

        await u.KickAsync();
        await LoggingService.LogAction($"{Context.User.Username} has kicked {u.Username}");
    }

    [Command("Ban")]
    [Summary("Ban an user")]
    [RequireUserPermission(GuildPermission.BanMembers)]
    public async Task BanUser(IUser user, params string[] reasons)
    {
        if (!await IsModerationEnabled()) return;

        var reason = string.Join(' ', reasons);
        await Context.Guild.AddBanAsync(user, 7, reason, RequestOptions.Default);
        await LoggingService.LogAction($"{Context.User.Username} has banned {user.Username} with the reason \"{reasons}\"");
    }

    [Command("Rules")]
    [Summary("Display rules of the current channel.")]
    [RequireModerator]
    public async Task RulesCommand(int seconds = 60)
    {
        await RulesCommand(Context.Channel, seconds);
        await Context.Message.DeleteAsync();
    }

    [Command("Rules")]
    [Summary("Display rules of the mentioned channel.")]
    [RequireModerator]
    public async Task RulesCommand(IMessageChannel channel, int seconds = 60)
    {
        //Display rules of this channel for x seconds
        var rule = Rules.Channel.First(x => x.Id == 0);
        IUserMessage m;
        if (rule == null)
            m = await ReplyAsync(
                "There is no special rule for this channel.\nPlease follow global rules (you can get them by typing `!globalrules`)");
        else
            m = await ReplyAsync(
                $"{rule.Header}{(rule.Content.Length > 0 ? rule.Content : "There is no special rule for this channel.\nPlease follow global rules (you can get them by typing `!globalrules`)")}");

        var deleteAsync = Context.Message?.DeleteAsync();
        if (deleteAsync != null) await deleteAsync;

        if (seconds == -1)
            return;
        await m.DeleteAfterSeconds(seconds: seconds);
    }

    [Command("GlobalRules")]
    [Summary("Display global rules in current channel.")]
    [RequireModerator]
    public async Task GlobalRules(int seconds = 60)
    {
        //Display rules of this channel for x seconds
        var globalRules = Rules.Channel.First(x => x.Id == 0).Content;
        var m = await ReplyAsync(globalRules);
        await Context.Message.DeleteAsync();

        if (seconds == -1)
            return;
        await m.DeleteAfterSeconds(seconds: seconds);
    }

    [Command("Channels")]
    [Summary("Get a description of the channels.")]
    [RequireModerator]
    public async Task ChannelsDescription(int seconds = 60)
    {
        //Display rules of this channel for x seconds
        var channelData = Rules.Channel;
        var sb = new StringBuilder();

        foreach (var c in channelData)
            sb.Append($"{(await Context.Guild.GetTextChannelAsync(c.Id))?.Mention} - {c.Header}\n");
        var text = sb.ToString();
        IUserMessage m;
        IUserMessage m2 = null;

        if (sb.ToString().Length > 2000)
        {
            m = await ReplyAsync(text.Substring(0, 2000));
            m2 = await ReplyAsync(text.Substring(2000));
        }
        else
            m = await ReplyAsync(text);

        await Context.Message.DeleteAsync();

        if (seconds == -1)
            return;
        await m.DeleteAfterSeconds(seconds: seconds);
        var deleteAsync = m2?.DeleteAsync();
        if (deleteAsync != null) await deleteAsync;
    }

    [Command("SlowMode")]
    [Summary("Turn on slowmode.")]
    [RequireModerator]
    public async Task SlowMode(int time)
    {
        await Context.Message.DeleteAsync();
        await (Context.Channel as ITextChannel).ModifyAsync(p => p.SlowModeInterval = time);
        await ReplyAsync($"Slowmode has been set to {time}s !").DeleteAfterSeconds(10);
    }

    [Command("TagRole")]
    [Summary("Tag a role and post a message.")]
    [Alias("mentionrole", "pingrole", "rolemention", "roletag", "roleping")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task TagRole(IRole role, params string[] messages)
    {
        var message = string.Join(' ', messages);
        var isMentionable = role.IsMentionable;
        if (!isMentionable) await role.ModifyAsync(properties => { properties.Mentionable = true; });
        await role.ModifyAsync(properties => { properties.Mentionable = true; });
        await Context.Channel.SendMessageAsync($"{role.Mention}\n{message}");
        if (!isMentionable) await role.ModifyAsync(properties => { properties.Mentionable = false; });
        await Context.Message.DeleteAsync();
    }

    [Command("React")]
    [Alias("reaction", "reactions", "addreactions", "addreaction")]
    [Summary("Adds the requested reactions to a message.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task React(ulong msgId, params string[] emojis)
    {
        var msg = (IUserMessage)await Context.Channel.GetMessageAsync(msgId);
        await Context.Message.DeleteAsync();
        foreach (var emoji in emojis)
            if (Emote.TryParse(emoji, out var emote))
                await msg.AddReactionAsync(emote);
            else
                await msg.AddReactionAsync(new Emoji(emoji));
    }

    [Command("React")]
    [Alias("reaction", "reactions", "addreactions", "addreaction")]
    [Summary("Adds the requested reactions to a message.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task React(params string[] emojis)
    {
        var msg = (IUserMessage)(await Context.Channel.GetMessagesAsync(2).FlattenAsync()).Last();

        await Context.Message.DeleteAsync();
        foreach (var emoji in emojis)
            if (Emote.TryParse(emoji, out var emote))
                await msg.AddReactionAsync(emote);
            else
                await msg.AddReactionAsync(new Emoji(emoji));
    }

    [Command("ClosePoll")]
    [Summary("Close a poll and append a message.")]
    [Alias("pollclose")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ClosePoll(IMessageChannel channel, ulong messageId, params string[] additionalNotes)
    {
        var additionalNote = string.Join(' ', additionalNotes);
        var message = (IUserMessage)await channel.GetMessageAsync(messageId);
        var reactions = message.Reactions;

        var reactionCount = string.Empty;
        foreach (var reaction in reactions)
            reactionCount += $" {reaction.Key.Name} ({reaction.Value.ReactionCount})";

        await message.ModifyAsync(properties =>
        {
            properties.Content = message.Content +
                                 $"\n\nThe poll has been closed. Here's the vote results :{reactionCount}\nAdditional notes : {additionalNote}";
        });
    }

    [Command("DBSync")]
    [Summary("Force add a user to the database.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task DbSync(IUser user)
    {
        await DatabaseService.AddNewUser((SocketGuildUser)user);
    }

    [Command("DBFullSync")]
    [Summary("Inserts all missing users, and updates any tracked data.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task FullSync()
    {
        await Context.Message.DeleteAsync();
        var tracker = await ReplyAsync("Updating user data: ");
        await DatabaseService.FullDbSync(Context.Guild, tracker);
    }

    #region General Utility Commands
    
    [Command("WelcomeMessageCount")]
    [Summary("Returns a count of pending welcome messages.")]
    [RequireModerator, HideFromHelp]
    // Simple method to check if there are many welcome messages waiting, and when the next one is due.
    public async Task WelcomeMessageCount()
    {
        var count = UserService.WaitingWelcomeMessagesCount;
        // If there are more than 0 messages waiting, show when nearest one is
        if (count > 0)
        {
            var next = UserService.NextWelcomeMessage.ToUnixTimestamp();
            await ReplyAsync($"There are {count} pending welcome messages. The next one is in <t:{next}:R>").DeleteAfterSeconds(seconds: 10);
        }
        else
        {
            await ReplyAsync("There are no pending welcome messages.").DeleteAfterSeconds(seconds: 10);
        }
        await Context.Message.DeleteAsync();
    }
    
    // Command to show the tags available for a specific channel, so the command needs to be run in a channel with tags or specific a channel id to check
    [Command("ChannelTags")]
    [Summary("Returns a list of tags for the current channel.")]
    [RequireModerator, HideFromHelp]
    public async Task ChannelTags(ulong channelId)
    {
        // Get the channel
        var channel = await Context.Guild.GetChannelAsync(channelId);

        if (channel is not IForumChannel forumChannel)
        {
            await ReplyAsync($"<#{channelId}> is not a forum channel and has no tags.").DeleteAfterSeconds(seconds: 10);
            return;
        }

        var tags = forumChannel.Tags;
        // If there are no tags, say so
        if (tags.Count == 0)
        {
            await ReplyAsync($"<#{channelId}> has no tags.").DeleteAfterSeconds(seconds: 10);
            return;
        }

        // If there are tags, list them in an embed in format of (ID: `id` - Name: `name`)
        var embed = new EmbedBuilder()
            .WithTitle($"Tags for <#{channelId}>")
            .WithDescription(string.Join("\n", tags.Select(tag => $"ID: `{tag.Id}` - Name: `{tag.Name}`")) +
                             $"\n\n{StringUtil.MessageSelfDestructIn(60)}")
            .WithColor(Color.Blue)
            .Build();

        Context.Message.DeleteAsync();
        await ReplyAsync(embed: embed).DeleteAfterSeconds(seconds: 60);
    }

    #endregion
        
    #region CommandList
    [RequireModerator]
    [Summary("Does what you see now.")]
    [Command("Mod Help")]
    public async Task ModerationHelp()
    {
        foreach (var message in CommandHandlingService.GetCommandListMessages("ModerationModule", true, true, false))
        {
            await ReplyAsync(message);
        }
    }
    #endregion
}