using CasinoWeb.Configuration;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Services.Logging;
using Microsoft.Extensions.Options;

namespace CasinoWeb.Games;

/// <summary>
/// Loads the slot art and builds the picture palette in the background at start (about 3 s), so the first announcement does not
/// pay for it. It never delays readiness; when the art is missing the announcements go out without a picture.
/// </summary>
public sealed class SlotsPictureWarmUp(SlotsRenderer renderer, IOptions<CasinoHostOptions> options, ILoggingService logging) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.AnnouncementsConfigured)
            return;

        // Hands control back to the host first: the first part of ExecuteAsync runs inside the host's start.
        await Task.Yield();
        try
        {
            await renderer.WarmUpAsync();
            await logging.Log(LogBehaviour.Console, "Casino: slots pictures ready");
        }
        catch (Exception ex)
        {
            await logging.Log(LogBehaviour.Console, $"Casino: the slots pictures cannot be drawn, big wins will be announced without a picture: {ex.GetType().Name}: {ex.Message}", ExtendedLogSeverity.Error);
        }
    }
}
