using DiscordBot.Components;
using DiscordBot.Services;
using DiscordBot.Settings.Legacy;
using DiscordBot.Settings.Validation;
using Microsoft.Extensions.Hosting;

namespace DiscordBot.Hosting;

public sealed class DiscordBotHostedService(
    IDiscordGateway gateway,
    IBotRuntimeCoordinator runtime,
    LegacyConfigurationReport configurationReport,
    FeatureConfigurationCatalog featureConfiguration) : IHostedService
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;
    private bool _gatewayStarted;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
            return;

        if (configurationReport.IsLegacySource)
        {
            LoggingService.LogToConsole(
                $"Legacy configuration source '{configurationReport.SourcePath}' is active and will be removed after the compatibility window.",
                ExtendedLogSeverity.LowWarning);
        }

        foreach (var unknownKey in configurationReport.UnknownKeys)
        {
            LoggingService.LogToConsole(
                $"Unknown legacy configuration key '{unknownKey}' is ignored.",
                ExtendedLogSeverity.Warning);
        }

        foreach (var status in featureConfiguration.Statuses.Where(status => !status.IsConfigured))
        {
            LoggingService.LogToConsole(
                $"Optional component '{status.ComponentId}' is unavailable: {string.Join(" ", status.Errors)}",
                ExtendedLogSeverity.Warning);
        }

        gateway.Ready += OnReady;
        try
        {
            await gateway.LoginAsync(cancellationToken);
            await gateway.StartAsync(cancellationToken);
            _gatewayStarted = true;
            await _ready.Task.WaitAsync(cancellationToken);

            await runtime.StartAsync(cancellationToken);
            _started = true;
        }
        catch
        {
            gateway.Ready -= OnReady;
            if (_gatewayStarted)
            {
                await gateway.StopAsync(CancellationToken.None);
                _gatewayStarted = false;
            }
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        gateway.Ready -= OnReady;

        if (!_started)
            return;

        await runtime.StopAsync(cancellationToken);
        await gateway.StopAsync(cancellationToken);
        _gatewayStarted = false;
        _started = false;
    }

    private Task OnReady()
    {
        _ready.TrySetResult();
        return Task.CompletedTask;
    }
}

public interface IBotRuntimeCoordinator
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public sealed class BotRuntimeCoordinator(
    ICommandRuntime commandRuntime,
    IComponentRegistry componentRegistry,
    ILoggingService logger) : IBotRuntimeCoordinator
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await commandRuntime.StartAsync(cancellationToken);

        await componentRegistry.StartAllAsync(cancellationToken);

        await logger.LogChannelAndFile("Bot Started.", ExtendedLogSeverity.Positive);
        LoggingService.LogToConsole("Bot is connected.", ExtendedLogSeverity.Positive);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await componentRegistry.StopAllAsync(cancellationToken);
        await commandRuntime.StopAsync(cancellationToken);
    }
}
