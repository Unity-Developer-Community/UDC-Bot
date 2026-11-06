namespace CasinoWeb.Tables;

/// <summary>Moves the table clocks every second (turn deadlines, grace periods, the next deal, AI moves) and, when the server stops gracefully, gives back the tokens of unfinished hands.</summary>
public sealed class TablesSweeper(TableService tables, TimeProvider time, ILogger<TablesSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // An exception escaping a background service stops the whole server; the next tick tries again.
                try
                {
                    await tables.TickAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "The tables tick failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // The ticks stop first, so none can deal a hand whose tokens are being given back.
        await base.StopAsync(cancellationToken);
        await tables.ReleaseAllAsync();
    }
}
