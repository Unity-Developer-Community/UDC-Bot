using Discord.WebSocket;
using DiscordBot.Settings;
using DiscordBot.Services.UnityHelp;

namespace DiscordBot.Services;

// TODO : (James) Better Slash Command Support

public class UnityHelpService
{
    private const string ServiceName = "UnityHelpService";

    private readonly DiscordSocketClient _client;
    private readonly ILoggingService _logging;
    private SocketRole ModeratorRole { get; set; }
    
    #region Configuration
    
    private static readonly Emoji ThumbUpEmoji = new Emoji("👍");
    
    private const int TimeBeforeClosedForResolvedTag = 10;
    private readonly Embed _resolvedWarnOfPendingCloseEmbedHasPin = new EmbedBuilder()
        .WithTitle($"Issue Resolved")
        .WithDescription($"This issue has been marked as resolved and will be archived in {TimeBeforeClosedForResolvedTag} minutes.")
        .WithColor(Color.Green)
        .Build();
    private readonly Embed _resolvedWarnOfPendingCloseEmbedNoPin = new EmbedBuilder()
        .WithTitle($"Issue Resolved")
        .WithDescription($"This issue has been marked as resolved and will be archived in {TimeBeforeClosedForResolvedTag} minutes.")
        .WithFooter($"Remember to Right click a message and select 'Apps->Correct Answer'")
        .WithColor(Color.Green)
        .Build();
    private static readonly Emoji CloseEmoji = new Emoji("\ud83d\udd12");
    private const int HasResponseIdleTimeSelfUser = 60 * 14;
    private static readonly string HasResponseIdleTimeSelfUserMessage = $"Hello {{0}}! This forum has been inactive for {HasResponseIdleTimeSelfUser / 60} hours. If the question has been appropriately answered, click the {CloseEmoji} emoji to close this thread.";
    private const int HasResponseIdleTimeOtherUser = 60 * 20;
    private static readonly string HasResponseMessageRequestClose = $"Hello {{0}}! This forum has been inactive for {HasResponseIdleTimeOtherUser / 60} hours without your input. If the question has been appropriately answered, click the {CloseEmoji} emoji to close this thread.";
    private const string HasResponseExtraMessage = $"If you still need help, perhaps include additional details!";

    private const int NoResponseNotResolvedIdleTime = 60 * 24 * 3;
    private readonly Embed _stealthDeleteEmbed = new EmbedBuilder()
        .WithTitle("Warning: No Activity")
        .WithDescription($"This question has been idle for {NoResponseNotResolvedIdleTime / 60} hours and has no response.\n" +
                         $"You can keep this thread active by adding additional details as a new message, " +
                         $"otherwise it will be closed in {StealthDeleteTime / 60} hours.")
        .WithColor(Color.LightOrange)
        .Build();
    private const int StealthDeleteTime = 60 * 5;
    
    private readonly Embed _noAppliedTagsEmbed = new EmbedBuilder()
        .WithTitle("Warning: No Tags Applied")
        .WithDescription($"Consider adding tags to your question to help others find it!\n" +
                         $"Right click on the thread title and select 'Edit Tags'!\n")
        .WithFooter($"Relevant tags help experienced users find you!")
        .WithColor(Color.LightOrange)
        .Build();


    private const int MinimumLengthMessage = 40;
    private readonly Embed _minimumLengthMessageEmbed = new EmbedBuilder()
        .WithTitle("Warning: Short Question!")
        .WithDescription($"Your question is short and may be difficult to answer! [don't ask to ask](https://dontasktoask.com/)!\n" +
                         $"- Errors? Include full error message.\n" +
                         $"- Features? Which version of Unity.\n" +
                         $"- Assets? Relevant store links.")
        .WithFooter("Be descriptive of the problem!")
        .WithColor(Color.LightOrange)
        .Build();
    
    #endregion // Configuration

    #region Extra Details
    
    private readonly IForumChannel _helpChannel;
    
    private readonly ForumTag _resolvedForumTag;

    #endregion // Extra Details

    public UnityHelpService(DiscordSocketClient client, BotSettings settings, ILoggingService logging)
    {
        _client = client;
        _logging = logging;
        
        ModeratorRole = _client.GetGuild(settings.GuildId).GetRole(settings.ModeratorRoleId);

        if (!settings.UnityHelpBabySitterEnabled)
        {
            LoggingService.LogServiceDisabled(ServiceName, nameof(settings.UnityHelpBabySitterEnabled));
            return;
        }
        
        // get the help channel settings.GenericHelpChannel
        _helpChannel = _client.GetChannel(settings.GenericHelpChannel.Id) as IForumChannel;
        if (_helpChannel == null)
        {
            LoggingService.LogToConsole($"[{ServiceName}] Help channel not found", LogSeverity.Error);
        }
        var resolvedTag = _helpChannel!.Tags.FirstOrDefault(x => x.Id == ulong.Parse(settings.TagUnitHelpResolvedTag));
        if (resolvedTag == null || resolvedTag.Id <= 0)
            LoggingService.LogToConsole($"[{ServiceName}] Resolved tag not found", LogSeverity.Error);
        _resolvedForumTag = resolvedTag;

        // on reaction added, call method
        _client.ReactionAdded += OnReactionAdded;

        _client.ThreadCreated += GatewayOnThreadCreated;
        _client.ThreadUpdated += GatewayOnThreadUpdated;
        _client.ThreadDeleted += GatewayOnThreadDeleted;
        
        _client.ThreadMemberJoined += GatewayOnThreadMemberJoinedThread;
        _client.ThreadMemberLeft += GatewayOnThreadMemberLeftThread;
        
        _client.MessageReceived += GatewayOnMessageReceived;
        _client.MessageUpdated += GatewayOnMessageUpdated;
        _client.MessageDeleted += GatewayOnMessageDeleted;

        Task.Run(LoadActiveThreads);
        
        LoggingService.LogServiceEnabled(ServiceName);
    }

    private async Task LoadActiveThreads()
    {
        var loadingActiveThreads = await GetHelpActiveThreads();
        foreach (var thread in loadingActiveThreads)
        {
            var threadContents = (await _client.GetChannelAsync(thread.Id)) as SocketThreadChannel;
            var latestMessageId = (thread.MessageCount > 0 ? threadContents!.GetMessagesAsync(1).FlattenAsync().Result.FirstOrDefault()!.Id : 0);
            var threadOwner = threadContents!.Owner.Id;
            var threadContainer = new ThreadContainer
            {
                ThreadId = thread.Id,
                LatestUserMessage = latestMessageId,
                Owner = threadOwner,
                // TODO : (James) Work out if this should be resolved/closed already or not
                IsResolved = thread.AppliedTags.Contains(_resolvedForumTag.Id),
            };

            if (threadContainer.IsResolved)
            {
                // Run in new task so we don't block the other threads from being processed
#pragma warning disable CS4014
                Task.Run(() => CloseThreadInTime(threadContainer, string.Empty,
                    TimeBeforeClosedForResolvedTag,
                    (threadContainer.PinnedAnswer != 0
                        ? _resolvedWarnOfPendingCloseEmbedHasPin
                        : _resolvedWarnOfPendingCloseEmbedNoPin)));
#pragma warning restore CS4014
            }
            else
            {
                _activeThreads.Add(threadContainer.ThreadId, threadContainer);
            }
        }
    }

    #region Thread Tracking
    
    // Threads we're currently tracking
    private readonly Dictionary<ulong, ThreadContainer> _activeThreads = new();

    #region Thread Creation
    
    private async Task OnThreadCreated(SocketThreadChannel thread)
    {
        ThreadContainer container = new()
        {
            ThreadId = thread.Id,
            LatestUserMessage = 0,
            Owner = thread.Owner.Id,
        };
        _activeThreads.Add(thread.Id, container);
        
        bool warnHelpTitle = false;
        
        // Check message length and inform user if too short
        var firstMessage = (await thread.GetMessagesAsync(1).FlattenAsync()).FirstOrDefault();
        container.FirstUserMessage = firstMessage!.Id;
        if (firstMessage.Content.Length < MinimumLengthMessage)
        {
            var botResponse = await thread.SendMessageAsync(embed: _minimumLengthMessageEmbed);
            container.AddMessage(HelpMessageType.QuestionLength, botResponse.Id);
            // container.WarningMessage = botResponse.Id;
        }
        
        var threadTitle = thread.Name;
        if (threadTitle.IsAlLCaps())
        {
            threadTitle = thread.Name.ToLower();
        }
        await thread.ModifyAsync(x => x.Name = threadTitle.ToCapitalizeFirstLetter());

        if (thread.Name.Contains(" help", StringComparison.CurrentCultureIgnoreCase))
            warnHelpTitle = true;

        // If not tags attached, let them know they should add some
        if (thread.AppliedTags.Count == 0)
        {
            var botResponse = await thread.SendMessageAsync(embed: _noAppliedTagsEmbed);
            container.AddMessage(HelpMessageType.NoTags, botResponse.Id);
            // container.NoAppliedTagsMessage = botResponse.Id;
        }

        // TODO : (James) Should we push this into a Task.Run?
        // Sets up the thread to be closed after a certain amount of time (This will quickly be removed if anyone interacts with the thread)
        await StealthDeleteThreadInTime(container);
    }
    
    private Task GatewayOnThreadCreated(SocketThreadChannel thread)
    {
        if (!thread.IsThreadInChannel(_helpChannel.Id))
            return Task.CompletedTask;
        if (thread.Owner.IsUserBotOrWebhook())
            return Task.CompletedTask;
        if (thread.Owner.HasRoleGroup(ModeratorRole))
            return Task.CompletedTask;
        // Gateway is called twice for forums/threads (When Bot joins channel)
        if (_activeThreads.ContainsKey(thread.Id))
            return Task.CompletedTask;
        // Ignore new thread if age is over, 5 mins?
        if (thread.CreatedAt < DateTime.Now.AddMinutes(-5))
            return Task.CompletedTask;
        
        LoggingService.DebugLog($"[{ServiceName}] New Thread Created: {thread.Id} - {thread.Name}", LogSeverity.Debug);
        Task.Run(() => OnThreadCreated(thread));
        
        return Task.CompletedTask;
    }
    
    #endregion // Thread Creation

    #region Thread Update

    private async Task OnThreadUpdated(SocketThreadChannel before, SocketThreadChannel after)
    {
        var thread = _activeThreads[after.Id];

        if (after.IsArchived || after.IsLocked)
        {
            // Thread has been archived, remove from tracking
            if (!thread.IsResolved && !after.AppliedTags.Contains(_resolvedForumTag.Id))
            {
                await after.SendMessageAsync($"This thread closed without being marked as resolved");
                await EndThreadTracking(after.Id);
                return;
            }
            return;
        }

        // If the user was informed to add tags and they add some, we revoke the message
        if (thread.HasMessage(HelpMessageType.NoTags) && before.AppliedTags.Count < after.AppliedTags.Count)
        {
            var message = await after.GetMessageAsync(thread.GetMessageId(HelpMessageType.NoTags));
            if (message != null)
                await message.DeleteAsync();
            thread.RemoveMessage(HelpMessageType.NoTags);
        }

        //! Handle when the thread gets resolve tag added to it
        bool isNewResolved = !before.AppliedTags.Contains(_resolvedForumTag.Id) &&
                             after.AppliedTags.Contains(_resolvedForumTag.Id);
        if (isNewResolved)
        {
            // Posts a message in chat and closes the channel after x minutes
            thread.IsResolved = true;
            if (_activeThreads.TryGetValue(after.Id, out thread))
            {
                var embed = thread.PinnedAnswer != 0 ? _resolvedWarnOfPendingCloseEmbedHasPin : _resolvedWarnOfPendingCloseEmbedNoPin;
                await CloseThreadInTime(thread, string.Empty, TimeBeforeClosedForResolvedTag, embed);
            }
        }

        // TODO : (James)
        // //! Handle when the thread was resolved, but has been changed to unresolved
        // if (before.AppliedTags.Contains(_resolvedForumTag.Id) &&
        //     !after.AppliedTags.Contains(_resolvedForumTag.Id))
        // {
        //
        // }
    }
    
    private async Task GatewayOnThreadUpdated(Cacheable<SocketThreadChannel, ulong> before, SocketThreadChannel after)
    {
        if (!after.IsThreadInChannel(_helpChannel.Id))
            return;
        if (!_activeThreads.TryGetValue(after.Id, out var thread))
            return;

        var beforeThread = await before.GetOrDownloadAsync();
        var afterThread = after;

        if (afterThread.IsPinned())
        {
            await EndThreadTracking(thread);
            return;
        }

        LoggingService.DebugLog($"[{ServiceName}] Thread Updated: {after.Id} - {after.Name}", LogSeverity.Debug);
        
#pragma warning disable CS4014
        Task.Run(() => OnThreadUpdated(beforeThread, afterThread));
#pragma warning restore CS4014
    }
    
    #endregion // Thread Update

    #region Thread Deleted
    
    private async Task OnThreadDeleted(SocketThreadChannel channel)
    {
        await EndThreadTracking(channel.Id);
    }

    private async Task GatewayOnThreadDeleted(Cacheable<SocketThreadChannel, ulong> threadId)
    {
        if (!_activeThreads.ContainsKey(threadId.Id))
            return;
        
        LoggingService.DebugLog($"[{ServiceName}] Thread Deleted: {threadId.Id}", LogSeverity.Debug);
        var thread = await threadId.GetOrDownloadAsync();

#pragma warning disable CS4014
        Task.Run(() => OnThreadDeleted(thread));
#pragma warning restore CS4014
    }
    
    #endregion // Thread Deleted

    #region User Joins/Leaves Thread
    
    private Task GatewayOnThreadMemberJoinedThread(SocketThreadUser user)
    {
        if (user.IsUserBotOrWebhook())
            return Task.CompletedTask;
        
        if (!user.Thread.IsThreadInChannel(_helpChannel.Id))
            return Task.CompletedTask;
        if (!_activeThreads.TryGetValue(user.Thread.Id, out var thread))
            return Task.CompletedTask;

        thread.HasInteraction = true;
        return Task.CompletedTask;
    }

    private Task GatewayOnThreadMemberLeftThread(SocketThreadUser user)
    {
        if (!user.Thread.IsThreadInChannel(_helpChannel.Id))
            return Task.CompletedTask;
        
        return Task.CompletedTask;

        // TODO : (James) Check if user was author? If so, close thread?
    }
    
    #endregion // User Joins/Leaves Thread

    #region Message Received
    
    private async Task OnMessageReceived(SocketMessage message)
    {
        var thread = _activeThreads[message.Channel.Id];
        
        thread.LatestUserMessage = message.Id;
        // If Author is only one who has interacted with the thread, we don't need to update anything else
        if (!thread.HasInteraction && message.Author.Id == thread.Owner)
        {
#pragma warning disable CS4014
            Task.Run(() => StealthDeleteThreadInTime(thread));
#pragma warning restore CS4014
            return;
        }

        thread.HasInteraction = true;
        // Depending on who sent the message we delay the thread closing request by a different amount of time
        var channel = message.Channel as SocketThreadChannel;
        if (channel!.Owner.Id == message.Author.Id)
        {
            await RequestThreadShutdownInTime(thread, HasResponseIdleTimeSelfUserMessage + HasResponseExtraMessage, HasResponseIdleTimeSelfUser);
        }
        else
        {
            await RequestThreadShutdownInTime(thread, HasResponseMessageRequestClose + HasResponseExtraMessage, HasResponseIdleTimeOtherUser);
        }
    }
    
    private Task GatewayOnMessageReceived(SocketMessage message)
    {
        if (!message.Channel.IsThreadInChannel(_helpChannel.Id))
            return Task.CompletedTask;
        if (message.Author.IsUserBotOrWebhook())
            return Task.CompletedTask;
        if (!_activeThreads.TryGetValue(message.Channel.Id, out var thread))
            return Task.CompletedTask;
        
        LoggingService.DebugLog($"[{ServiceName}] Help Message Received: {message.Id} - {message.Content}", LogSeverity.Debug);
        Task.Run(() => OnMessageReceived(message));
        return Task.CompletedTask;
    }

    private async Task OnMessageUpdated(IMessage before, IMessage after, SocketThreadChannel channel)
    {
        var thread = _activeThreads[channel.Id];
        
        if (thread.HasMessage(HelpMessageType.QuestionLength) && before.Id == thread.FirstUserMessage)
        {
            if (after.Content.Length > MinimumLengthMessage)
            {
                var warningMessage = await channel.GetMessageAsync(thread.GetMessageId(HelpMessageType.QuestionLength));
                if (warningMessage != null)
                    await warningMessage.DeleteAsync();
                thread.RemoveMessage(HelpMessageType.QuestionLength);
            }
        }
    }
    
    private async Task GatewayOnMessageUpdated(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        if (channel is not SocketThreadChannel threadChannel)
            return;
        if (!threadChannel.IsThreadInChannel(_helpChannel.Id))
            return;
        if (after.Author.IsUserBotOrWebhook())
            return;
        
        if (!_activeThreads.TryGetValue(channel.Id, out var thread))
            return;
        
        // This is done a bit late as we may need to check message from other authors
        if (thread.Owner != after.Author.Id)
            return;
        
        var beforeMsg = await before.GetOrDownloadAsync();
        if (beforeMsg == null)
            return;

        LoggingService.DebugLog($"[{ServiceName}] Help Message Updated: {after.Id} - {after.Content}", LogSeverity.Debug);
#pragma warning disable CS4014
        Task.Run(() => OnMessageUpdated(beforeMsg, after, channel as SocketThreadChannel));
#pragma warning restore CS4014

        if (after.Reactions.ContainsKey(CloseEmoji))
        {
            var closeReaction = await after.GetReactionUsersAsync(CloseEmoji, 10).FlattenAsync();
            if (closeReaction.Any(x => x.Id == thread.Owner))
            {
                await EndThreadTracking(thread);
                return;
            }
        }
    }
    
    private async Task GatewayOnMessageDeleted(Cacheable<IMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelChache)
    {
        if (!messageCache.HasValue)
            return;
        
        var message = messageCache.Value;
        var channel = channelChache.Value;
        
        // Check if message is in a proper thread/forum and not just a normal message
        if (message.Author.IsBot || channel.IsThreadInChannel(_helpChannel.Id))
            return;
        
        // Check if message is a thread
        if (message.Channel is not SocketThreadChannel threadChannel)
            return;
        
        // Check if message is a thread that is being tracked
        if (!_activeThreads.TryGetValue(threadChannel.Id, out var thread))
            return;
        if (thread.Owner != message.Author.Id)
            return;
        
        // Check if message was the last message in the thread
        if (thread.FirstUserMessage != message.Id)
        {
            // End Tracking and delete the whole thread not just the first message
            await EndThreadTracking(thread);
            await threadChannel.DeleteAsync();
            return;
        }
    }

    #endregion // Message Received
    
    #endregion // Thread Tracking

    #region Event Handlers

    private async Task OnReactionAdded(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelChache, SocketReaction reaction)
    {
        // Check if reaction is in a proper thread/forum and not just a normal message
        if (await channelChache.GetOrDownloadAsync() is not SocketThreadChannel channel || channel.Id != _helpChannel.Id)
            return;
        var message = await messageCache.GetOrDownloadAsync();
        // Limit locking to only reactions on the bots messages
        if (message == null || message.Author.Id != _client.CurrentUser.Id)
            return;

#pragma warning disable CS4014 
        Task.Run(async () =>
#pragma warning restore CS4014
        {
            // Check the owner is the one reacting
            var threadOwner = channel.Owner.Id;
            if (reaction.UserId != threadOwner)
                return;

            await CloseThread(channel, true);
        });
    }
    
    public async Task<string> OnUserRequestChannelClose(IUser user, SocketThreadChannel channel)
    {
        if (channel.ParentChannel.Id != _helpChannel.Id)
            return string.Empty;
        if (!_activeThreads.TryGetValue(channel.Id, out var thread))
            return string.Empty;
        
        if (thread.Owner != user.Id)
            return string.Empty;
        
        await CloseThread(channel, true);
        return "Your thread has been closed.";
    }

    #endregion // Event Handlers

    #region Bulk Behaviour Handler

    private async Task CloseThreadInTime(ThreadContainer thread, string message, int minutes, Embed embed = null)
    {
        await Task.Delay(TimeSpan.FromMinutes(minutes));
        if (thread.HasInteraction)
            return;
        
        var channel = _client.GetChannel(thread.ThreadId) as SocketThreadChannel;
        if (channel == null)
            return;
        
        if (!string.IsNullOrEmpty(message))
            await channel.SendMessageAsync(message);
        else
        {
            await channel.SendMessageAsync(embed: embed);
        }
        await CloseThread(channel, false);

        if (!(await IsValidThread(thread)))
            return;
        
        var expectedShutdownTime = DateTime.Now.AddMinutes(minutes);
        var threadChannel = _client.GetChannel(thread.ThreadId) as SocketThreadChannel;
        // Check if token already created, each thread shares its own token with any relevant action (close, delete, etc)
        await CancelPreviousWarning(thread, expectedShutdownTime);
        
        thread.CancellationToken ??= new CancellationTokenSource();
        // Send our message
        if (!string.IsNullOrEmpty(message))
        {
            await threadChannel.SendMessageAsync(message);
        }
        thread.ExpectedShutdownTime = expectedShutdownTime;
        await Task.Delay(minutes * 60 * 1000, thread.CancellationToken.Token);
        if (await IsTaskCancelled(thread))
            return;

        await CloseThread(threadChannel!, thread.IsResolved);
    }

    private async Task RequestThreadShutdownInTime(ThreadContainer thread, string msgString, int minutes)
    {
        if (!(await IsValidThread(thread)))
            return;
        
        var expectedWarnTime = DateTime.Now.AddMinutes(minutes);
        var threadChannel = _client.GetChannel(thread.ThreadId) as SocketThreadChannel;
        // Check if token already created, each thread shares its own token with any relevant action (close, delete, etc)
        await CancelPreviousWarning(thread, expectedWarnTime);
        thread.CancellationToken ??= new CancellationTokenSource();
        
        thread.ExpectedShutdownTime = expectedWarnTime;
        await Task.Delay(minutes * 60 * 1000, thread.CancellationToken.Token);
        if (await IsTaskCancelled(thread))
            return;
        
        msgString = string.Format(msgString, threadChannel.Owner.Mention);
        var sentMessage = await threadChannel.SendMessageAsync(msgString);
        // add the lock reaction
        await sentMessage.AddReactionAsync(CloseEmoji);
        thread.LatestUserMessage = sentMessage.Id;
    }
    
    /// <summary>
    /// When a thread is first started, this is called first to set it up to be closed after a certain amount of time
    /// This will quickly be canceled if the thread is interacted with.
    /// </summary>
    private async Task StealthDeleteThreadInTime(ThreadContainer thread)
    {
        if (!(await IsValidThread(thread)))
            return;

        var expectedShutdownTime = DateTime.Now.AddMinutes(NoResponseNotResolvedIdleTime);
        
        await CancelPreviousWarning(thread, expectedShutdownTime);
        var threadChannel = _client.GetChannel(thread.ThreadId) as SocketThreadChannel;

        thread.CancellationToken ??= new CancellationTokenSource();
        thread.ExpectedShutdownTime = expectedShutdownTime;
        // Wait for the time to pass
        await Task.Delay(NoResponseNotResolvedIdleTime * 60 * 1000, thread.CancellationToken.Token);
        if (await IsTaskCancelled(thread))
            return;

        // We prompt chat that the thread is going to be deleted in x number of hours, which will double as a bump.
        var botResponse = await threadChannel.SendMessageAsync(embed: _stealthDeleteEmbed);
        thread.BotsLastMessage = botResponse.Id;
        
        // Wait for the next set of time to pass
        thread.ExpectedShutdownTime = DateTime.Now.AddMinutes(StealthDeleteTime);
        await Task.Delay(StealthDeleteTime * 60 * 1000, thread.CancellationToken.Token);
        if (await IsTaskCancelled(thread))
            return;

        await threadChannel.DeleteAsync();
    }

    #endregion // Bulk Behaviour Handler
    
    
    #region Generic Methods
    
    private async Task CloseThread(IThreadChannel channel, bool includeResolvedTag = false)
    {
        var appliedTags = channel.AppliedTags.ToList();
        if (includeResolvedTag && !appliedTags.Contains(_resolvedForumTag.Id))
            appliedTags.Add(_resolvedForumTag.Id);
        await channel.ModifyAsync(x =>
        {
            x.Archived = true;
            x.AppliedTags = appliedTags;
            x.Locked = true;
        });

        await EndThreadTracking(channel.Id);
    }

    private Task EndThreadTracking(ThreadContainer thread)
    {
        thread.CancellationToken?.Cancel();
        // TODO : (James) Go through the message ids for the thread and delete them
        _activeThreads.Remove(thread.ThreadId);
        return Task.CompletedTask;
    }
    private async Task EndThreadTracking(ulong threadId)
    {
        if (_activeThreads.TryGetValue(threadId, out var thread))
            await EndThreadTracking(thread);
    }

    // Check if token already created, each thread shares its own token with any relevant action (close, delete, etc)
    private async Task CancelPreviousWarning(ThreadContainer thread, DateTime newShutdownTime)
    {
        if (thread.CancellationToken != null)
        {
            if (thread.ExpectedShutdownTime < newShutdownTime)
            {
                thread.CancellationToken.Cancel();
                thread.CancellationToken = null;
            }
            await RemoveContainerPreviousComment(thread);
        }
    }
    
    private async Task<List<IThreadChannel>> GetHelpActiveThreads()
    {
        var messages = await _helpChannel.GetActiveThreadsAsync();
        var helpThreads = messages.Where(x => x.CategoryId == _helpChannel.Id).ToList();
        return helpThreads;
    }
    
    public async Task<string> MarkResponseAsAnswer(IUser requester, IMessage message)
    {
        if (message.Channel is not IThreadChannel channel)
            return "Invalid Channel";
        if (!_activeThreads.TryGetValue(channel.Id, out var thread))
            return "Thread already closed";

        if (!IsValidAuthorUser(requester as SocketGuildUser, thread.Owner))
        {
            if (thread.Owner != requester.Id)
                return "You are not the owner of this thread";
            return "You can't do that!";
        }
        if (message.Author.Id == requester.Id)
            return "You cannot mark your own message as the answer";

        if (thread.PinnedAnswer != 0)
        {
            var oldAnswer = await channel.GetMessageAsync(thread.PinnedAnswer) as IUserMessage;
            if (oldAnswer != null)
            {
                await oldAnswer.RemoveReactionAsync(ThumbUpEmoji, _client.CurrentUser);
                await oldAnswer.UnpinAsync();
            }
        }

        if (await channel.GetMessageAsync(message.Id) is IUserMessage newAnswer)
        {
            await newAnswer.PinAsync();
            await newAnswer.AddReactionAsync(ThumbUpEmoji);
        }

        if (!thread.IsResolved)
            await CloseThread(channel, true);
        
        thread.PinnedAnswer = message.Id;
        return "New answer pinned";
    }

    #endregion // Generic Methods

    #region Utility Methods

    private async Task RemoveContainerPreviousComment(ThreadContainer thread)
    {
        if (thread.BotsLastMessage == 0)
            return;
        if (await _client.GetChannelAsync(thread.ThreadId) is SocketThreadChannel threadChannel)
        {
            var message = await threadChannel.GetMessageAsync(thread.BotsLastMessage);
            if (message != null)
            {
                await message.DeleteAsync();
            }
        }
    }

    private async Task<bool> IsValidThread(ThreadContainer thread)
    {
        var threadChannel = _client.GetChannel(thread.ThreadId) as SocketThreadChannel;
        // If channel is null, we have problems, we remove them from _activeThreads, cancel the token and return to avoid an exception
        if (threadChannel == null)
        {
            await EndThreadTracking(thread);
            return false;
        }
        return true;
    }
    
    private Task<bool> IsTaskCancelled(ThreadContainer thread)
    {
        if (thread.CancellationToken == null)
            return Task.FromResult(false);
        if (thread.CancellationToken.IsCancellationRequested)
        {
            LoggingService.DebugLog($"[{ServiceName}] Task cancelled for Channel {thread.ThreadId}");
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
    
    // Check if the user is the expected id and return true if so, if not then return false (Special: Moderator will return true)
    private bool IsValidAuthorUser(SocketGuildUser user, ulong authorId)
    {
        if (user == null || user.IsUserBotOrWebhook())
            return false;
        
        if (user.Id == authorId) return true;
        // If the user is moderator they can act on behalf of the author
        if (user.HasRoleGroup(ModeratorRole))
            return true;
        
        return false;

    }
    
    public int GetTrackedQuestionCount()
    {
        return _activeThreads.Count;
    }

    #endregion // Utility Methods
    
}