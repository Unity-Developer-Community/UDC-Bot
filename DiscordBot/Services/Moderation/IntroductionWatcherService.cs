using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services;

// Small service to watch users posting new messages in introductions, keeping track of the last 500 messages and deleting any from the same user
public class IntroductionWatcherService
{
    private const string ServiceName = "IntroductionWatcherService";
    
    private readonly DiscordSocketClient _client;
    private readonly ILoggingService _loggingService;
    private readonly SocketChannel _introductionChannel;

    private readonly HashSet<ulong> _uniqueUsers = new HashSet<ulong>(MaxMessagesToTrack + 1);
    private readonly Queue<ulong> _orderedUsers = new Queue<ulong>(MaxMessagesToTrack + 1);
    
    private const int MaxMessagesToTrack = 1000;
    
    public IntroductionWatcherService(DiscordSocketClient client, ILoggingService loggingService, BotSettings settings)
    {
        _client = client;
        _loggingService = loggingService;

        if (!settings.IntroductionWatcherServiceEnabled)
        {
            LoggingService.LogServiceDisabled(ServiceName, nameof(settings.IntroductionWatcherServiceEnabled));
            return;
        }
        
        _introductionChannel = client.GetChannel(settings.IntroductionChannel.Id);
        if (_introductionChannel == null)
        {
            _loggingService.LogAction($"[{ServiceName}] Error: Could not find introduction channel.", ExtendedLogSeverity.Warning);
            return;
        }
        
        _client.MessageReceived += MessageReceived;
    }

    private async Task MessageReceived(SocketMessage message)
    {
        // We only watch the introduction channel
        if (_introductionChannel == null || message.Channel.Id != _introductionChannel.Id)
            return;
        
        if (_uniqueUsers.Contains(message.Author.Id))
        {
            await message.DeleteAsync();
            await _loggingService.LogChannelAndFile(
                $"[{ServiceName}]: Duplicate introduction from {message.Author.GetUserLoggingString()} [Message deleted]");
        }
        
        _uniqueUsers.Add(message.Author.Id);
        _orderedUsers.Enqueue(message.Author.Id);
        if (_orderedUsers.Count > MaxMessagesToTrack)
        {
            var oldestUser = _orderedUsers.Dequeue();
            _uniqueUsers.Remove(oldestUser);
        }
        
        await Task.CompletedTask;
    }
}