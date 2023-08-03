using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services;

[Serializable]
public class ReminderItem {
    public ulong ChannelId { get; set; }
    public ulong MessageId { get; set; }
    public ulong UserId { get; set; }
    public string Message { get; set; }
    public DateTime When { get; set; }
}

public class ReminderService
{
    // Bot responds to reminder request, any users who also use this emoji on the message will be pinged when the reminder is triggered.
    public static readonly Emoji BotResponseEmoji = new("✅");
    
    public bool IsRunning { get; private set; }
    
    private DateTime _nearestReminder = DateTime.Now;
        
    private readonly DiscordSocketClient _client;
    private readonly ILoggingService _loggingService;
    private List<ReminderItem> _reminders = new List<ReminderItem>();
        
    private readonly BotCommandsChannel _botCommandsChannel;
    private bool _hasChangedSinceLastSave = false;

    private const int _maxUserReminders = 10;

    public ReminderService(DiscordSocketClient client, ILoggingService loggingService, BotSettings settings)
    {
        _client = client;
        _loggingService = loggingService;
        _botCommandsChannel = settings.BotCommandsChannel;

        Initialize();
    }

    private void Initialize()
    {
        if (IsRunning) return;
        
        LoadReminders();
        if (_reminders == null)
        {
            // Tell the user that we couldn't load the reminders
            _loggingService.LogAction("ReminderService: Couldn't load reminders", false);
        }
        IsRunning = true;
        Task.Run(CheckReminders);
    }

    // Serialize Reminders to file
    public void SaveReminders()
    {
        Utils.SerializeUtil.SerializeFile(@"Settings/reminders.json", _reminders);
    }
    private void LoadReminders()
    {
        _reminders = Utils.SerializeUtil.DeserializeFile<List<ReminderItem>>(@"Settings/reminders.json");
    }
    public void AddReminder(ReminderItem reminder)
    {
        _reminders.Add(reminder);
        _hasChangedSinceLastSave = true;
            
        // We check if this reminder is sooner than the next one
        if (_nearestReminder > reminder.When)
            _nearestReminder = reminder.When;
    }
        
    public bool UserHasTooManyReminders(ulong userId)
    {
        return _reminders.FindAll(x => x.UserId == userId).Count >= _maxUserReminders;
    }
        
    public List<ReminderItem> GetUserReminders(ulong userId)
    {
        return _reminders.FindAll(x => x.UserId == userId);
    }

    public int RemoveReminders(IUser user, int index = 0)
    {
        int count = 0;
        if (index == 0)
            count = _reminders.RemoveAll(x => x.UserId == user.Id);
        else
        {
            var userReminders = GetUserReminders(user.Id);
            if (userReminders.Count < index)
                return -1;

            _reminders.Remove(userReminders[index - 1]);
            count = 1;
        }

        if (count != 0)
            _hasChangedSinceLastSave = true;
        return count;
    }
        
    // Check if reminders are due in an async task that loops from the constructor
    private async Task CheckReminders()
    {
        try
        {
            while (true)
            {
                // We check if there has been a change to the reminders list since the last update.
                if (_hasChangedSinceLastSave)
                {
                    SaveReminders();
                    _hasChangedSinceLastSave = false;
                }

                await Task.Delay(1000);
                    
                var now = DateTime.Now;
                // We wait until we know at least one reminder needs to be checked
                if (now <= _nearestReminder || _reminders.Count <= 0) continue;
                    
                List<ReminderItem> remindersToCheck = _reminders.Where(r => r.When <= now).ToList();
                _hasChangedSinceLastSave = true;

                foreach (ReminderItem reminder in remindersToCheck)
                {
                    _reminders.Remove(reminder);

                    IUserMessage message = null;
                    var channel = _client.GetChannel(reminder.ChannelId) as SocketTextChannel;
                    if (channel != null)
                        message = await channel.GetMessageAsync(reminder.MessageId) as IUserMessage;

                    // We reply to their original message
                    if (message != null)
                    {
                        string botResponse = $"{message.Author.Mention} reminder: \"{reminder.Message}\"";
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
                        // If there are any extra users, we add them to the bot response
                        if (extraUsers != string.Empty)
                            botResponse += $"\n\nReacted Extras: {extraUsers}";
                        
                        await message.ReplyAsync(botResponse);
                        continue;
                    }
                    // If channel is null we get the bot command channel, and send the message there
                    channel ??= _client.GetChannel(_botCommandsChannel.Id) as SocketTextChannel;
                    var user = _client.GetUser(reminder.UserId);
                    if (user == null) continue;
                    
                    if (channel != null)
                        await channel.SendMessageAsync(
                            $"{user.Mention} reminder: \"{reminder.Message}\"");
                }

                // Find the nearest reminder in _reminders and set if there is at least 1 reminder
                if (_reminders.Count > 0)
                    _nearestReminder = _reminders.Min(x => x.When);
            }
        }
        catch (Exception e)
        {
            // Catch and show exception
            LoggingService.LogToConsole($"Reminder Service Exception during Reminder.\n{e.Message}");
            await _loggingService.LogAction($"Reminder Service has crashed.\nException Msg: {e.Message}.", false, true);
            IsRunning = false;
        }
    }
        
    public bool RestartService()
    {
        Initialize();
        return IsRunning;
    }
}