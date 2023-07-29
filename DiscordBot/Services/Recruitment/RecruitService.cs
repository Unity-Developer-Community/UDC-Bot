using Discord.WebSocket;
using DiscordBot.Services.UnityHelp;
using DiscordBot.Settings;
using DiscordBot.Utils;

public class RecruitService
{
    private static readonly string ServiceName = "RecruitmentService";
    
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

    private Color DeletedMessageColor => new Color(255, 50, 50);
    private Color WarningMessageColor => new Color(255, 255, 100);

    private const int TimeBeforeDeletingForumInSec = 30;
    private const int MinimumLengthMessage = 120;

    private string _messageToBeDeleted =
        "Your thread will be deleted in %s because it did not follow the expected guidelines. Try again after the slow mode period has passed.";
    private Embed _userHiringButNoPrice;
    private Embed _userWantsWorkButNoPrice;

    private Embed _userDidntUseTags;
    private Embed _userRevShareMentioned;
    private Embed _userMoreThanOneTagUsed;
    private Embed _userShortMessage;
    
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
            if (thread.AppliedTags.Count == 0)
            {
                await ThreadHandleNoTags(thread, message);
                return;
            }

            if (IsThreadUsingMoreThanOneTag(thread))
            {
                await ThreadHandleMoreThanOneTag(thread, message);
                return;
            }

            bool isPaidWork = (thread.AppliedTags.Contains(_tagWantsWork.Id) ||
                               thread.AppliedTags.Contains(_tagIsHiring.Id));

            if (isPaidWork)
            {
                if (!message.Content.ContainsCurrencySymbol())
                {
                    await ThreadHandleExpectedCurrency(thread, message);
                    return;
                }
                await ThreadHandleRevShare(thread, message);
            }
            
            await ThreadHandleShortMessage(thread, message);
        });
    }

    #endregion // Thread Creation

    #region Basic Handlers for posts

    private async Task ThreadHandleExpectedCurrency(SocketThreadChannel thread, IMessage message)
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
    
    private async Task ThreadHandleMoreThanOneTag(SocketThreadChannel thread, IMessage message)
    {
        await thread.SendMessageAsync(embed: _userMoreThanOneTagUsed);
        await DeleteThread(thread);
    }
    
    private async Task ThreadHandleNoTags(SocketThreadChannel thread, IMessage message)
    {
        await thread.SendMessageAsync(embed: _userDidntUseTags);
        await DeleteThread(thread);
    }
    
    private async Task ThreadHandleShortMessage(SocketThreadChannel thread, IMessage message)
    {
        if (message.Content.Length < MinimumLengthMessage)
        {
            var ourResponse = await thread.SendMessageAsync(embed: _userShortMessage);
            await ourResponse.DeleteAfterSeconds(TimeBeforeDeletingForumInSec * 5);
        }
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
            .WithTitle($"No payment price detected")
            .WithDescription(
                $"You have used the `{_tagIsHiring.Name}` tag but have not specified a price of any kind.\n\nPost **must** include a currency symbol or word, e.g. $, dollars, USD, £, pounds, €, EUR, euro, euros, GBP.")
            .WithColor(DeletedMessageColor)
            .Build();
        
        _userWantsWorkButNoPrice = new EmbedBuilder()
            .WithTitle($"No payment price detected")
            .WithDescription(
                $"You have used the `{_tagWantsWork.Name}` tag but have not specified a price of any kind.\n\nPost **must** include a currency symbol or word, e.g. $, dollars, USD, £, pounds, €, EUR, euro, euros, GBP.")
            .WithColor(DeletedMessageColor)
            .Build();
        
        _userRevShareMentioned = new EmbedBuilder()
            .WithTitle($"Notice: Rev-Share mentioned")
            .WithDescription(
                $"Rev-share isn't considered a valid form of payment for `{_tagIsHiring.Name}` or `{_tagWantsWork.Name}` as it's not guaranteed. " +
                $"Consider using the `{_tagUnpaidCollab.Name}` tag instead if you intend to use rev-share as a source of payment.")
            .WithColor(WarningMessageColor)
            .Build();
        
        _userMoreThanOneTagUsed = new EmbedBuilder()
            .WithTitle($"Broken Guideline: Colliding tags used")
            .WithDescription(
                $"You may only use one of the following tags: `{_tagIsHiring.Name}`, `{_tagWantsWork.Name}` or `{_tagUnpaidCollab.Name}`\n\n" +
                $"Be sure to read the guidelines before posting.")
            .WithColor(DeletedMessageColor)
            .Build();
        
        _userDidntUseTags = new EmbedBuilder()
            .WithTitle($"Broken Guideline: No tags used")
            .WithDescription(
                $"You must use one of the following tags: `{_tagIsHiring.Name}`, `{_tagWantsWork.Name}` or `{_tagUnpaidCollab.Name}`\n\n" +
                $"Be sure to read the guidelines before posting.")
            .WithColor(DeletedMessageColor)
            .Build();
        
        _userShortMessage = new EmbedBuilder()
            .WithTitle($"Warning: Post is short")
            .WithDescription(
                $"Your post should contain enough information to convince others to work with you, we recommend at least {MinimumLengthMessage} characters, " +
                $"which is still very short.\n\nPlease edit your post to contain more information otherwise it may be deleted by staff.")
            .WithColor(WarningMessageColor)
            .Build();
    }
    
    private Embed GetDeletedMessageEmbed()
    {
        // Create a dynamic timestamp for now + TimeBeforeDeletingForumInSec
        var timestamp = DateTimeOffset.Now.AddSeconds(TimeBeforeDeletingForumInSec);
        var timestampString = $"<t:{timestamp.ToUnixTimeSeconds()}:R>";
        var message = _messageToBeDeleted.Replace("%s", timestampString);
        
        return new EmbedBuilder()
            .WithTitle($"Post does not follow guidelines")
            .WithDescription(message)
            .WithColor(DeletedMessageColor)
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
    
    private async Task DeleteThread(SocketThreadChannel thread)
    {
        await thread.SendMessageAsync(embed: GetDeletedMessageEmbed());
        await thread.DeleteAfterSeconds(TimeBeforeDeletingForumInSec);
    }

    #endregion // Basic Utility
    
}