namespace CasinoWeb.Games;

/// <summary>Settles idle blackjack hands in the background and gives back the open bets when the server stops gracefully.</summary>
public sealed class BlackjackSweeper(WebBlackjackService blackjack, ILogger<BlackjackSweeper> logger) : BackgroundService
{
    // A hand is settled at most this long after its timeout.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // An exception escaping a background service stops the whole server; the next sweep tries again.
                try
                {
                    await blackjack.SweepIdleHandsAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "The blackjack idle sweep failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // The sweep stops first, so it cannot settle a hand whose bet is being given back.
        await base.StopAsync(cancellationToken);
        await blackjack.ReleaseAllAsync();
    }
}
