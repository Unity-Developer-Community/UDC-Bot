using Discord.WebSocket;
using DiscordBot.Settings;
using DiscordBot.Services.Recruitment.Application;
using DiscordBot.Services.Recruitment.Runtime;

namespace DiscordBot.Services.Recruitment;

/// <summary>Stable service facade composing gateway translation and one serialized state worker.</summary>
public sealed class RecruitmentService
{
    private readonly DiscordSocketClient? _client;
    private readonly BotSettings _settings;
    private readonly RecruitmentWorker _worker;
    private readonly RecruitmentGateway? _gateway;
    private bool _started;
    public RecruitmentHealth Health => _worker.Health;

    public RecruitmentService(DiscordSocketClient client, BotSettings settings)
        : this(settings, new DiscordRecruitmentAdapter(client, settings), TimeProvider.System)
    {
        _client = client;
        _gateway = new RecruitmentGateway(client, settings, _discord, _worker);
        _worker.Stopped += () => _gateway.Subscribe(false);
    }

    private readonly IRecruitmentDiscord _discord;
    internal RecruitmentService(BotSettings settings, IRecruitmentDiscord discord, TimeProvider time)
    {
        _settings = settings;
        _discord = discord;
        _worker = new RecruitmentWorker(settings, discord, time);
    }

    public Task StartAsync()
    {
        if (_started) return Task.CompletedTask;
        _started = true;
        _worker.InitializeGateway(_client is null || _client.GetGuild(_settings.GuildId)?.IsConnected == true);
        _gateway?.Subscribe(true);
        return _worker.StartAsync();
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _gateway?.Subscribe(false);
        await _worker.StopAsync(ct);
    }

    public bool CanReadStatus(string guild, string actor) => guild == _settings.GuildId.ToString() &&
        ulong.TryParse(actor, out var id) && _client?.GetGuild(_settings.GuildId)?.GetUser(id) is { } user &&
        (user.GuildPermissions.Administrator || user.Roles.Any(r => r.Id == _settings.Roles.Moderator));

    public bool CanOpenAcknowledgement(string guild, string thread, string actor) => _worker.CanOpenAcknowledgement(guild, thread, actor);
    public string QueueRecovery(string guild, string actor, StaffCommand command, string target, string reason) =>
        CanReadStatus(guild, actor) ? _worker.QueueRecovery(guild, actor, command, target, reason) : "Moderator access is required in the configured server.";
    public Task<T> RunAsync<T>(Func<RecruitmentEngine, CancellationToken, Task<T>> command, string? ownerThread = null) => _worker.RunAsync(command, ownerThread);
    internal void EvidenceGap() => _worker.EvidenceGap();
    internal void NotifyThread(string id) => _worker.NotifyThread(id);
    internal void NotifyCreation(ThreadCreation creation) => _worker.NotifyCreation(creation);
    internal void NotifyFactsChanged(string id) => _worker.NotifyFactsChanged(id);
    internal void NotifyThreadDeletion(string id) => _worker.NotifyThreadDeletion(id);
    internal void NotifyMessageDeletion(string channel, IReadOnlySet<string> ids) => _worker.NotifyMessageDeletion(channel, ids);
}
