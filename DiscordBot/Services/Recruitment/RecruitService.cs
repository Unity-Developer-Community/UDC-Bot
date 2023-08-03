using Discord.WebSocket;
using DiscordBot.Settings;
using DiscordBot.Utils;

namespace DiscordBot.Services;

public class RecruitService
{
    private const string ServiceName = "RecruitmentService";
    
    private readonly DiscordSocketClient _client;
    private readonly ILoggingService _logging;
    private SocketRole ModeratorRole { get; set; }

    #region Extra Details
    
    private readonly ForumTag _tagIsHiring;
    private readonly ForumTag _tagWantsWork;
    private readonly ForumTag _tagUnpaidCollab;
    private readonly ForumTag _tagPosFilled;

    private readonly IForumChannel _recruitChannel;

    #endregion // Extra Details
    
    #region Configuration

    private Color DeletedMessageColor => new (255, 50, 50);
    private Color WarningMessageColor => new (255, 255, 100);
    private Color EditedMessageColor => new (100, 255, 100);

    private const int TimeBeforeDeletingForumInSec = 60;
    private const string _messageToBeDeleted = "Your thread will be deleted in %s because it did not follow the expected guidelines. Try again after the slow mode period has passed.";
    
    private const int MinimumLengthMessage = 120;
    private const int ShortMessageNoticeDurationInSec = 30 * 4;

    private readonly int _editTimePermissionInMin;
    private const string _messageToBeEdited = "This post will remain editable until %s, make any desired changes to your thread. After that the thread will be locked.";


    private Embed _userHiringButNoPrice;
    private Embed _userWantsWorkButNoPrice;

    private Embed _userDidntUseTags;
    private Embed _userRevShareMentioned;
    private Embed _userMoreThanOneTagUsed;

    Dictionary<ulong, bool> _botSanityCheck = new Dictionary<ulong, bool>();

    #endregion // Configuration
    
    public RecruitService(DiscordSocketClient client, ILoggingService logging, BotSettings settings)
    {
        _client = client;
        _logging = logging;
        ModeratorRole = _client.GetGuild(settings.GuildId).GetRole(settings.ModeratorRoleId);

        if (!settings.RecruitmentServiceEnabled)
        {
            LoggingService.LogServiceDisabled(ServiceName, nameof(settings.RecruitmentServiceEnabled));
            return;
        }
        _editTimePermissionInMin = settings.EditPermissionAccessTimeMin;
        
        // Get target channel
        _recruitChannel = _client.GetChannel(settings.RecruitmentChannel.Id) as IForumChannel;
        if (_recruitChannel == null)
        {
            LoggingService.LogToConsole("[{ServiceName}] Recruitment channel not found.", LogSeverity.Error);
            return;
        }
        
        try
        {
            var lookingToHire = ulong.Parse(settings.TagLookingToHire);
            var lookingForWork = ulong.Parse(settings.TagLookingForWork);
            var unpaidCollab = ulong.Parse(settings.TagUnpaidCollab);
            var positionFilled = ulong.Parse(settings.TagPositionFilled);
            
            var availableTags = _recruitChannel.Tags;
            _tagIsHiring = availableTags.First(x => x.Id == lookingToHire);
            _tagWantsWork = availableTags.First(x => x.Id == lookingForWork);
            _tagUnpaidCollab = availableTags.First(x => x.Id == unpaidCollab);
            _tagPosFilled = availableTags.First(x => x.Id == positionFilled);
            
            // If any tags are null we print a logging warning
            if (_tagIsHiring == null) StartUpTagMissing(lookingToHire, nameof(settings.TagLookingToHire));
            if (_tagWantsWork == null) StartUpTagMissing(lookingForWork, nameof(settings.TagLookingForWork));
            if (_tagUnpaidCollab == null) StartUpTagMissing(unpaidCollab, nameof(settings.TagUnpaidCollab));
            if (_tagPosFilled == null) StartUpTagMissing(positionFilled, nameof(settings.TagPositionFilled));
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[{ServiceName}] Error parsing recruitment tags: {e.Message}", LogSeverity.Error);
        }

        // Subscribe to events
        _client.ThreadCreated += GatewayOnThreadCreated;
        _client.MessageReceived += GatewayOnMessageReceived;

        ConstructEmbeds();
        
        LoggingService.LogServiceEnabled(ServiceName);
    }
    
    #region Thread Creation

    private async Task GatewayOnThreadCreated(SocketThreadChannel thread)
    {
        if (!thread.IsThreadInChannel(_recruitChannel.Id))
            return;
        if (thread.Owner.IsUserBotOrWebhook())
            return;
        if (thread.Owner.HasRoleGroup(ModeratorRole))
            return;

        #region Sanity Check
        // Oddly the thread is sometimes called twice, so we do a sanity check to make sure we don't process it twice.
        // Probably a better way to do this, but this is pretty cheap operation.
        if (_botSanityCheck.ContainsKey(thread.Id))
        {
            _botSanityCheck.Remove(thread.Id);
            return;
        }
        if (_botSanityCheck.Count > 10)
            _botSanityCheck.Clear();
        _botSanityCheck.Add(thread.Id, true);
        #endregion // Sanity Check
        
        LoggingService.DebugLog($"[{ServiceName}] New Thread Created: {thread.Id} - {thread.Name}", LogSeverity.Debug);

        var message = (await thread.GetMessagesAsync(1).FlattenAsync()).FirstOrDefault();
        if (message == null)
        {
            LoggingService.LogToConsole($"[{ServiceName}] Thread {thread.Id} has no messages.", LogSeverity.Error);
            return;
        }

        Task.Run(async () =>
        {
            if (!DoesThreadHaveAValidTag(thread))
            {
                await ThreadHandleNoTags(thread);
                return;
            }

            if (IsThreadUsingMoreThanOneTag(thread))
            {
                await ThreadHandleMoreThanOneTag(thread);
                return;
            }

            bool isPaidWork = (thread.AppliedTags.Contains(_tagWantsWork.Id) ||
                               thread.AppliedTags.Contains(_tagIsHiring.Id));

            if (isPaidWork)
            {
                if (!message.Content.ContainsCurrencySymbol())
                {
                    await ThreadHandleExpectedCurrency(thread);
                    return;
                }
                await ThreadHandleRevShare(thread, message);
            }
            
            // Any Notices that we can recommend the user for improvement
            if (message.Content.Length < MinimumLengthMessage)
            {
                Task.Run(() => ThreadHandleShortMessage(thread, message));
            }

            await Task.Delay(millisecondsDelay: 200);

            // If they got this far, they have a valid thread.
            await GrantEditPermissions(thread);
            // The above method will await for 30~ minutes, so we need to check if the thread is still valid.

            // Confirm user hasn't deleted the thread
            var channel = await _client.GetChannelAsync(thread.Id) as SocketThreadChannel;
            if (channel == null)
                return;
            // Confirm the message still exists
            var threadMessage = await (channel.GetMessageAsync(thread.Id));
            if (threadMessage == null)
                return;
            
            // We do one last check to make sure the thread is still valid
            if (isPaidWork && !threadMessage.Content.ContainsCurrencySymbol())
            {
                await ThreadHandleExpectedCurrency(channel);
            }
        });
    }
    
    private async Task GatewayOnMessageReceived(SocketMessage message)
    {
        var thread = message.Channel as SocketThreadChannel;
        // check if channel is a thread in a forum
        if (thread == null)
            return;
        
        if (!thread.IsThreadInChannel(_recruitChannel.Id))
            return;
        if (message.Author.IsUserBotOrWebhook())
            return;
        if (message.Author.HasRoleGroup(ModeratorRole))
            return;

        // Sanity process, delete any new messages that aren't written from the thread owner, moderators or bots.
        if (message.Author.Id != thread.Owner.Id)
        {
            await message.DeleteAsync();
        }
    }

    #endregion // Thread Creation

    #region Basic Handlers for posts

    private async Task ThreadHandleExpectedCurrency(SocketThreadChannel thread)
    {
        var embedToUse = thread.AppliedTags.Contains(_tagWantsWork.Id)
            ? _userWantsWorkButNoPrice
            : _userHiringButNoPrice;
        await thread.SendMessageAsync(embed: embedToUse);
        await DeleteThread(thread);
    }

    private async Task ThreadHandleRevShare(SocketThreadChannel thread, IMessage message)
    {
        if (message.Content.ContainsRevShare())
        {
            await thread.SendMessageAsync(embed: _userRevShareMentioned);
        }
    }
    
    private async Task ThreadHandleMoreThanOneTag(SocketThreadChannel thread)
    {
        await thread.SendMessageAsync(embed: _userMoreThanOneTagUsed);
        await DeleteThread(thread);
    }
    
    private async Task ThreadHandleNoTags(SocketThreadChannel thread)
    {
        await thread.SendMessageAsync(embed: _userDidntUseTags);
        await DeleteThread(thread);
    }
    
    private async Task ThreadHandleShortMessage(SocketThreadChannel thread, IMessage message)
    {
        if (message.Content.Length < MinimumLengthMessage)
        {
            var ourResponse = await thread.SendMessageAsync(embed: GetShortMessageEmbed());
            await ourResponse.DeleteAfterSeconds(ShortMessageNoticeDurationInSec);
        }
    }
    
    private async Task GrantEditPermissions(SocketThreadChannel thread)
    {
        var parentChannel = thread.ParentChannel;
        var message = await thread.SendMessageAsync(embed: GetEditPermMessageEmbed());
        await parentChannel.AddPermissionOverwriteAsync(thread.Owner, new OverwritePermissions(sendMessages: PermValue.Allow));
        
        // We give them a bit of time to edit their post, then remove the permission
        await message.DeleteAfterSeconds((_editTimePermissionInMin * 60) + 2);
        await parentChannel.RemovePermissionOverwriteAsync(thread.Owner);
        
        // Lock the thread so anyone else can't post even when they have edit permissions
        await thread.ModifyAsync(x => x.Locked = true);
    }
    
    #endregion // Basic Handlers for posts

    #region Basic Logging Assisst

    private static void StartUpTagMissing(ulong tagId, string tagName)
    {
        LoggingService.LogToConsole($"[{ServiceName}] Tag {tagId} not found. '{tagName}'", LogSeverity.Error);
    }

    #endregion // Basic Logging Assisst

    #region Embed Construction

    private void ConstructEmbeds()
    {
        _userHiringButNoPrice = new EmbedBuilder()
            .WithTitle("No payment price detected")
            .WithDescription(
                $"You have used the `{_tagIsHiring.Name}` tag but have not specified a price of any kind.\n\nPost **must** include a currency symbol or word, e.g. $, dollars, USD, £, pounds, €, EUR, euro, euros, GBP.")
            .WithColor(DeletedMessageColor)
            .Build();
        
        _userWantsWorkButNoPrice = new EmbedBuilder()
            .WithTitle("No payment price detected")
            .WithDescription(
                $"You have used the `{_tagWantsWork.Name}` tag but have not specified a price of any kind.\n\nPost **must** include a currency symbol or word, e.g. $, dollars, USD, £, pounds, €, EUR, euro, euros, GBP.")
            .WithColor(DeletedMessageColor)
            .Build();
        
        _userRevShareMentioned = new EmbedBuilder()
            .WithTitle("Notice: Rev-Share mentioned")
            .WithDescription(
                $"Rev-share isn't considered a valid form of payment for `{_tagIsHiring.Name}` or `{_tagWantsWork.Name}` as it's not guaranteed. " +
                $"Consider using the `{_tagUnpaidCollab.Name}` tag instead if you intend to use rev-share as a source of payment.")
            .WithColor(WarningMessageColor)
            .Build();
        
        _userMoreThanOneTagUsed = new EmbedBuilder()
            .WithTitle("Broken Guideline: Colliding tags used")
            .WithDescription(
                $"You may only use one of the following tags: `{_tagIsHiring.Name}`, `{_tagWantsWork.Name}` or `{_tagUnpaidCollab.Name}`\n\n" +
                "Be sure to read the guidelines before posting.")
            .WithColor(DeletedMessageColor)
            .Build();
        
        _userDidntUseTags = new EmbedBuilder()
            .WithTitle("Broken Guideline: No tags used")
            .WithDescription(
                $"You must use one of the following tags: `{_tagIsHiring.Name}`, `{_tagWantsWork.Name}` or `{_tagUnpaidCollab.Name}`\n\n" +
                "Be sure to read the guidelines before posting.")
            .WithColor(DeletedMessageColor)
            .Build();
    }
    
    private Embed GetDeletedMessageEmbed()
    {
        var message = _messageToBeDeleted.Replace("%s", GetDynamicTimeStampString(TimeBeforeDeletingForumInSec));
        return new EmbedBuilder()
            .WithTitle("Post does not follow guidelines")
            .WithDescription(message)
            .WithColor(DeletedMessageColor)
            .Build();
    }

    private Embed GetEditPermMessageEmbed()
    {
        var message = _messageToBeEdited.Replace("%s", GetDynamicTimeStampString(_editTimePermissionInMin * 60));
        return new EmbedBuilder()
            .WithTitle("Edit permissions granted")
            .WithDescription(message)
            .WithColor(EditedMessageColor)
            .Build();
    }

    private Embed GetShortMessageEmbed()
    {
        var timestamp = GetDynamicTimeStampString(ShortMessageNoticeDurationInSec);
        return new EmbedBuilder()
            .WithTitle("Notice: Post is short")
            .WithDescription(
                $"Your post should provide more information to convince others, we recommend at least {MinimumLengthMessage} characters, " +
                "which is still very short.\n\nYou should consider editing your post to contain more info otherwise it may be deleted by staff.\n\n" +
                $"*This is only a notice and will be removed {timestamp}, can be ignored.*")
            .WithColor(WarningMessageColor)
            .Build();
    }

    #endregion // Embed Construction

    #region Basic Utility

    private bool IsThreadUsingMoreThanOneTag(SocketThreadChannel thread)
    {
        int clashingTagCount = 0;
        var tags = thread.AppliedTags;
        
        if (tags.Contains(_tagIsHiring.Id)) clashingTagCount++;
        if (tags.Contains(_tagWantsWork.Id)) clashingTagCount++;
        if (tags.Contains(_tagUnpaidCollab.Id)) clashingTagCount++;
        
        return clashingTagCount > 1;
    }

    private bool DoesThreadHaveAValidTag(SocketThreadChannel thread)
    {
        var tags = thread.AppliedTags;
        return tags.Contains(_tagIsHiring.Id) || tags.Contains(_tagWantsWork.Id) || tags.Contains(_tagUnpaidCollab.Id);
    }
    
    private async Task DeleteThread(SocketThreadChannel thread)
    {
        await thread.SendMessageAsync(embed: GetDeletedMessageEmbed());
        await thread.DeleteAfterSeconds(TimeBeforeDeletingForumInSec);
    }

    private string GetDynamicTimeStampString(int addSeconds)
    {
        var timestamp = DateTimeOffset.Now.AddSeconds(addSeconds);
        return $"<t:{timestamp.ToUnixTimeSeconds()}:R>";
    }

    #endregion // Basic Utility
    
}