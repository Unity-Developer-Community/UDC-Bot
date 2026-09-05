using DiscordBot.Components;

namespace DiscordBot.Services;

/// <summary>
/// The former single-forum enforcement was retired with the new configuration.
/// The Observe coordinator will supply the managed lifetime in the next uplift chunk.
/// </summary>
public sealed class RecruitService : IManagedBotService, IComponentHealthContributor
{
    public const string UnavailableReason =
        "Recruitment foundation is installed; the event coordinator is not implemented yet. Keep Recruitment:Enabled false.";

    public string ComponentId => ComponentIds.Recruitment;
    public bool IsRunning => false;

    public Task StartAsync(CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(UnavailableReason));

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ComponentHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ComponentHealthSnapshot(ComponentRuntimeState.Stopped,
            UnavailableReason, DateTimeOffset.UtcNow));
}
