using Discord.WebSocket;
using DiscordBot.Components;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

[Serializable]
public class ReminderItem
{
    public ulong ChannelId { get; set; }
    public ulong MessageId { get; set; }
    public ulong UserId { get; set; }
    public string Message { get; set; }
    public DateTime When { get; set; }
}

public class ReminderService : IManagedBotService, IComponentHealthContributor
{
    private const string ServiceName = "ReminderService";

    // Bot responds to reminder request, any users who also use this emoji on the message will be pinged when the reminder is triggered.
    public static readonly Emoji BotResponseEmoji = new("✅");

    public string ComponentId => ComponentIds.Reminders;
    public bool IsRunning => _loopTask is { IsCompleted: false };

    private DateTime _nearestReminder = DateTime.MaxValue;

    private readonly DiscordSocketClient _client;
    private readonly ILoggingService _loggingService;
    private List<ReminderItem> _reminders = new List<ReminderItem>();

    private readonly ulong _fallbackChannelId;
    private readonly string _serverRootPath;
    private bool _hasChangedSinceLastSave = false;
    private readonly object _reminderLock = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private CancellationTokenSource? _lifecycleCancellation;
    private Task? _loopTask;

    private const int _maxUserReminders = 10;

    public ReminderService(
        DiscordSocketClient client,
        ILoggingService loggingService,
        IOptions<ReminderOptions> options,
        IOptions<StorageOptions> storageOptions)
    {
        _client = client;
        _loggingService = loggingService;
        _fallbackChannelId = options.Value.FallbackChannelId;
        _serverRootPath = storageOptions.Value.ServerRootPath;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
                return;

            LoadReminders();
            _nearestReminder = _reminders.Count == 0 ? DateTime.MaxValue : _reminders.Min(reminder => reminder.When);
            _lifecycleCancellation?.Dispose();
            _lifecycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loopTask = CheckReminders(_lifecycleCancellation.Token);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_loopTask is null)
                return;

            await _lifecycleCancellation!.CancelAsync();
            try
            {
                await _loopTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
            {
            }

            SaveReminders();
            _loopTask = null;
            _lifecycleCancellation.Dispose();
            _lifecycleCancellation = null;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public Task<ComponentHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken)
    {
        int reminderCount;
        lock (_reminderLock)
            reminderCount = _reminders.Count;
        return Task.FromResult(new ComponentHealthSnapshot(
            IsRunning ? ComponentRuntimeState.Running : ComponentRuntimeState.Stopped,
            IsRunning ? $"Running with {reminderCount} reminder(s)." : "Stopped.",
            DateTimeOffset.UtcNow));
    }

    // Serialize Reminders to file
    public void SaveReminders()
    {
        List<ReminderItem> snapshot;
        lock (_reminderLock)
            snapshot = _reminders.ToList();
        Utils.SerializeUtil.SerializeFile($"{_serverRootPath}/reminders.json", snapshot);
    }
    private void LoadReminders()
    {
        _reminders = Utils.SerializeUtil.DeserializeFile<List<ReminderItem>>($"{_serverRootPath}/reminders.json");
    }
    public void AddReminder(ReminderItem reminder)
    {
        lock (_reminderLock)
        {
            _reminders.Add(reminder);
            _hasChangedSinceLastSave = true;
            if (_nearestReminder > reminder.When)
                _nearestReminder = reminder.When;
        }
    }

    public bool UserHasTooManyReminders(ulong userId)
    {
        lock (_reminderLock)
            return _reminders.Count(x => x.UserId == userId) >= _maxUserReminders;
    }

    public List<ReminderItem> GetUserReminders(ulong userId)
    {
        lock (_reminderLock)
            return _reminders.FindAll(x => x.UserId == userId);
    }

    public int RemoveReminders(IUser user, int index = 0)
    {
        lock (_reminderLock)
        {
            int count;
            if (index == 0)
                count = _reminders.RemoveAll(x => x.UserId == user.Id);
            else
            {
                var userReminders = _reminders.FindAll(x => x.UserId == user.Id);
                if (userReminders.Count < index)
                    return -1;

                _reminders.Remove(userReminders[index - 1]);
                count = 1;
            }

            if (count != 0)
            {
                _hasChangedSinceLastSave = true;
                _nearestReminder = _reminders.Count == 0
                    ? DateTime.MaxValue
                    : _reminders.Min(reminder => reminder.When);
            }
            return count;
        }
    }

    // Check if reminders are due in an async task that loops from the constructor
    private async Task CheckReminders(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // We check if there has been a change to the reminders list since the last update.
                bool shouldSave;
                lock (_reminderLock)
                {
                    shouldSave = _hasChangedSinceLastSave;
                    _hasChangedSinceLastSave = false;
                }
                if (shouldSave)
                {
                    SaveReminders();
                }

                await Task.Delay(1000, cancellationToken);

                var now = DateTime.Now;
                List<ReminderItem> remindersToCheck;
                lock (_reminderLock)
                {
                    if (now <= _nearestReminder || _reminders.Count == 0)
                        continue;

                    remindersToCheck = _reminders.Where(reminder => reminder.When <= now).ToList();
                    foreach (var reminder in remindersToCheck)
                        _reminders.Remove(reminder);
                    _hasChangedSinceLastSave = remindersToCheck.Count > 0;
                    _nearestReminder = _reminders.Count == 0
                        ? DateTime.MaxValue
                        : _reminders.Min(reminder => reminder.When);
                }

                foreach (ReminderItem reminder in remindersToCheck)
                {
                    IUserMessage message = null;
                    var channel = _client.GetChannel(reminder.ChannelId) as SocketTextChannel;
                    if (channel != null)
                        message = await channel.GetMessageAsync(reminder.MessageId) as IUserMessage;

                    // We reply to their original message
                    if (message != null)
                    {
                        string botResponse = $"Reminding {message.Author.Mention}: \"{reminder.Message}\"";
                        // Get the people who reacted to the message 
                        var includeUsers = await message.GetReactionUsersAsync(BotResponseEmoji, 10).FlattenAsync();
                        string extraUsers = string.Empty;
                        foreach (IUser includeUser in includeUsers)
                        {
                            if (includeUser.IsBot)
                                continue;
                            if (includeUser.Id == message.Author.Id)
                                continue;

                            extraUsers += $"{includeUser.Mention} ";
                        }
                        extraUsers = extraUsers.TrimEnd();

                        // If there are any extra users, we add them to the bot response
                        if (extraUsers != string.Empty)
                            botResponse += $"\n({extraUsers} also signed on {BotResponseEmoji})";

                        await message.ReplyAsync(botResponse);
                        continue;
                    }
                    // If channel is null we get the bot command channel, and send the message there
                    channel ??= _client.GetChannel(_fallbackChannelId) as SocketTextChannel;
                    var user = _client.GetUser(reminder.UserId);
                    if (user == null) continue;

                    if (channel != null)
                        await channel.SendMessageAsync(
                            $"{user.Mention} reminder: \"{reminder.Message}\"");
                }

            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            // Catch and show exception
            await _loggingService.LogChannelAndFile($"Reminder Service has crashed.\nException Msg: {e.Message}.", ExtendedLogSeverity.Warning);
            throw;
        }
    }

}
