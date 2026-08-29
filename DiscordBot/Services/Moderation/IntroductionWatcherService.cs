using Discord.WebSocket;
using DiscordBot.Components;
using DiscordBot.Settings.Options;
using DiscordBot.Settings.Validation;
using DiscordBot.Services.UnityHelp;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

// Small service to watch users posting new messages in introductions, keeping track of the last 500 messages and deleting any from the same user
public class IntroductionWatcherService : IManagedBotService, IComponentHealthContributor
{
    private const string ServiceName = "IntroductionWatcherService";
    
    private readonly DiscordSocketClient _client;
    private readonly ILoggingService _loggingService;
    private SocketChannel? _introductionChannel;
    private readonly ulong _introductionChannelId;
    private readonly ulong _guildId;
    private readonly ulong _moderatorRoleId;
    private readonly bool _enabled;
    private readonly FeatureConfigurationCatalog _featureConfiguration;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private readonly HashSet<ulong> _uniqueUsers = new HashSet<ulong>(MaxMessagesToTrack + 1);
    private readonly Queue<ulong> _orderedUsers = new Queue<ulong>(MaxMessagesToTrack + 1);
    
    private SocketRole? ModeratorRole { get; set; }

    public string ComponentId => ComponentIds.IntroductionWatcher;
    public bool IsRunning { get; private set; }

    private const int MaxMessagesToTrack = 1000;
    
    public IntroductionWatcherService(
        DiscordSocketClient client,
        ILoggingService loggingService,
        IOptions<ModerationOptions> moderationOptions,
        IOptions<DiscordGuildOptions> guildOptions,
        IOptions<AuthorizationOptions> authorizationOptions,
        FeatureConfigurationCatalog featureConfiguration)
    {
        _client = client;
        _loggingService = loggingService;
        var settings = moderationOptions.Value;
        _enabled = settings.IntroductionWatcherEnabled;
        _introductionChannelId = settings.IntroductionChannelId;
        _guildId = guildOptions.Value.GuildId;
        _moderatorRoleId = authorizationOptions.Value.ModeratorRoleId;
        _featureConfiguration = featureConfiguration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
                return;
            if (!_enabled)
                throw new InvalidOperationException("Moderation:IntroductionWatcherEnabled is false.");

            var configurationStatus = _featureConfiguration.Get(ComponentIds.IntroductionWatcher);
            if (!configurationStatus.IsConfigured)
                throw new InvalidOperationException(string.Join(" ", configurationStatus.Errors));

            ModeratorRole = _client.GetGuild(_guildId)?.GetRole(_moderatorRoleId)
                ?? throw new InvalidOperationException("The configured moderator role was not found.");

            _introductionChannel = _client.GetChannel(_introductionChannelId);
            if (_introductionChannel == null)
                throw new InvalidOperationException("The configured introduction channel was not found.");

            _client.MessageReceived += MessageReceived;
            IsRunning = true;
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
            if (!IsRunning)
                return;
            _client.MessageReceived -= MessageReceived;
            IsRunning = false;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public Task<ComponentHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ComponentHealthSnapshot(
            IsRunning ? ComponentRuntimeState.Running : ComponentRuntimeState.Stopped,
            IsRunning ? "Introduction event handler is subscribed." : "Introduction event handler is stopped.",
            DateTimeOffset.UtcNow));

    private async Task MessageReceived(SocketMessage message)
    {
        // We only watch the introduction channel
        if (_introductionChannel == null || message.Channel.Id != _introductionChannel.Id)
            return;

        if (message.Author.HasRoleGroup(ModeratorRole))
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
