using System.IO;
using System.Net.Http;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Components;
using DiscordBot.Hosting;
using DiscordBot.Policies;
using DiscordBot.Service;
using DiscordBot.Services;
using DiscordBot.Services.Rendering;
using DiscordBot.Services.Tips;
using DiscordBot.Settings;
using DiscordBot.Settings.Legacy;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunMode = Discord.Commands.RunMode;

namespace DiscordBot;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--render-smoke", StringComparison.OrdinalIgnoreCase))
        {
            var outputPath = args.Length > 1 ? args[1] : null;
            var assetsRootPath = args.Length > 2
                ? args[2]
                : Path.Combine(AppContext.BaseDirectory, "Assets");
            return ProfileCardRenderSmoke.Run(assetsRootPath, outputPath);
        }

        if (args.Length > 0 && args[0].Equals("--render-stress", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadPositiveArgument(args, 1, 100, out var iterations) ||
                !TryReadPositiveArgument(args, 2, 4, out var parallelism))
            {
                Console.Error.WriteLine("Usage: --render-stress [iterations] [parallelism] [assets-root]");
                return 2;
            }

            var assetsRootPath = args.Length > 3
                ? args[3]
                : Path.Combine(AppContext.BaseDirectory, "Assets");
            return ProfileCardRenderSmoke.RunStress(assetsRootPath, iterations, parallelism);
        }

        try
        {
            using var host = BuildHost(args);
            await host.RunAsync();
            return 0;
        }
        catch (BotConfigurationException exception)
        {
            Console.Error.WriteLine($"Configuration error: {exception.Message}");
            return 2;
        }
        catch (OptionsValidationException exception)
        {
            WriteValidationFailures([exception]);
            return 2;
        }
        catch (AggregateException exception) when (
            exception.Flatten().InnerExceptions.All(inner => inner is OptionsValidationException))
        {
            WriteValidationFailures(exception.Flatten().InnerExceptions.Cast<OptionsValidationException>());
            return 2;
        }
    }

    public static IHost BuildHost(
        string[] args,
        string? contentRootPath = null,
        Action<IServiceCollection>? configureTestServices = null,
        Action<ConfigurationManager>? configureTestConfiguration = null)
    {
        var root = contentRootPath ?? Directory.GetCurrentDirectory();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = root
        });
        builder.Logging.ClearProviders();

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        }));

        builder.AddBotConfiguration(root);
        configureTestConfiguration?.Invoke(builder.Configuration);
        ConfigureServices(builder.Services, root);
        configureTestServices?.Invoke(builder.Services);
        return builder.Build();
    }

    private static void ConfigureServices(IServiceCollection services, string contentRootPath)
    {
        var rulesPath = Path.Combine(contentRootPath, "Settings/Rules.json");
        var faqPath = Path.Combine(contentRootPath, "Settings/FAQs.json");
        services.AddSingleton<IRulesCatalog>(new RulesCatalog(
            StaticContentLoader.LoadRequired<Rules>(rulesPath, "rules catalog")));
        services.AddSingleton<IFaqCatalog>(new FaqCatalog(
            StaticContentLoader.LoadRequired<List<FaqData>>(faqPath, "FAQ catalog")));

        services.AddSingleton(serviceProvider =>
        {
            var client = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose,
                AlwaysDownloadUsers = true,
                MessageCacheSize = 1024,
                GatewayIntents = GatewayIntents.All
            });
            client.Log += LoggingService.DiscordNetLogger;
            return client;
        });
        services.AddSingleton(serviceProvider => new CommandService(new CommandServiceConfig
        {
            CaseSensitiveCommands = false,
            DefaultRunMode = RunMode.Async
        }));
        services.AddSingleton(serviceProvider =>
            new InteractionService(serviceProvider.GetRequiredService<DiscordSocketClient>()));

        services.AddSingleton<IDiscordGateway, DiscordGateway>();
        services.AddSingleton<IBotRuntimeCoordinator, BotRuntimeCoordinator>();
        services.AddSingleton<CommandHandlingService>();
        services.AddSingleton<ICommandRuntime>(serviceProvider =>
            serviceProvider.GetRequiredService<CommandHandlingService>());
        services.AddSingleton<ILoggingService, LoggingService>();

        services.AddSingleton<IComponentCatalog, DefaultComponentCatalog>();
        services.AddSingleton<IComponentOverrideStore, ComponentOverrideStore>();
        services.AddSingleton<ComponentRegistry>();
        services.AddSingleton<IComponentRegistry>(serviceProvider =>
            serviceProvider.GetRequiredService<ComponentRegistry>());
        services.AddSingleton<IComponentStateReader>(serviceProvider =>
            serviceProvider.GetRequiredService<ComponentRegistry>());

        services.AddSingleton<ICommandChannelPolicy, CommandChannelPolicy>();
        services.AddSingleton<IBotAuthorizationPolicy, BotAuthorizationPolicy>();
        services.AddSingleton<IRoleAssignmentPolicy, RoleAssignmentPolicy>();
        services.AddSingleton<IBotPublicInfo, BotPublicInfo>();
        services.AddSingleton<IModerationPolicy, ModerationPolicy>();
        services.AddSingleton<ITicketPolicy, TicketPolicy>();
        services.AddSingleton<IUnityHelpPolicy, UnityHelpPolicy>();
        services.AddSingleton<IUserFunPolicy, UserFunPolicy>();
        services.AddSingleton<ITipsAuthorizationPolicy, TipsAuthorizationPolicy>();

        services.AddSingleton<DatabaseService>();
        services.AddSingleton(serviceProvider =>
            new ImageRenderOptions(
                serviceProvider.GetRequiredService<IOptions<StorageOptions>>().Value.AssetsRootPath));
        services.AddSingleton<IAvatarDownloader>(serviceProvider =>
        {
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(5),
                MaxConnectionsPerServer = 8,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10)
            };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            return new AvatarDownloader(client, serviceProvider.GetRequiredService<ImageRenderOptions>());
        });
        services.AddSingleton<IProfileCardRenderer, ProfileCardRenderer>();
        services.AddSingleton<UserService>();
        services.AddSingleton<IntroductionWatcherService>();
        services.AddSingleton<ModerationService>();
        services.AddSingleton<FeedService>();
        services.AddSingleton<UnityHelpService>();
        services.AddSingleton<RecruitService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<CurrencyService>();
        services.AddSingleton<ReminderService>();
        services.AddSingleton<WeatherService>();
        services.AddSingleton<AirportService>();
        services.AddSingleton<TipService>();
        services.AddSingleton<CannedResponseService>();
        services.AddSingleton<UserExtendedService>();
        services.AddSingleton<IBirthdaySource, GoogleSheetsBirthdaySource>();
        services.AddSingleton<BirthdayAnnouncementService>();
        services.AddSingleton<CasinoService>();
        services.AddSingleton<GameService>();
        services.AddSingleton<KarmaResetService>();

        services.AddSingleton(new ManagedComponentRegistration(
            ComponentIds.BirthdayAnnouncements,
            serviceProvider => serviceProvider.GetRequiredService<BirthdayAnnouncementService>()));
        services.AddSingleton(new ManagedComponentRegistration(
            ComponentIds.Database,
            serviceProvider => serviceProvider.GetRequiredService<DatabaseService>()));
        services.AddSingleton(new ManagedComponentRegistration(
            ComponentIds.Reminders,
            serviceProvider => serviceProvider.GetRequiredService<ReminderService>()));
        services.AddSingleton(new ManagedComponentRegistration(
            ComponentIds.KarmaReset,
            serviceProvider => serviceProvider.GetRequiredService<KarmaResetService>()));
        services.AddSingleton(new ManagedComponentRegistration(
            ComponentIds.Moderation,
            serviceProvider => serviceProvider.GetRequiredService<ModerationService>()));
        services.AddSingleton(new ManagedComponentRegistration(
            ComponentIds.IntroductionWatcher,
            serviceProvider => serviceProvider.GetRequiredService<IntroductionWatcherService>()));
        services.AddSingleton(new ManagedComponentRegistration(
            ComponentIds.Recruitment,
            serviceProvider => serviceProvider.GetRequiredService<RecruitService>()));
        services.AddSingleton(new ManagedComponentRegistration(
            ComponentIds.UnityHelp,
            serviceProvider => serviceProvider.GetRequiredService<UnityHelpService>()));
        services.AddSingleton(new ManagedComponentRegistration(
            ComponentIds.Updates,
            serviceProvider => serviceProvider.GetRequiredService<UpdateService>()));
        services.AddSingleton(new ManagedComponentRegistration(
            ComponentIds.UserActivity,
            serviceProvider => serviceProvider.GetRequiredService<UserService>()));

        services.AddHostedService<DiscordBotHostedService>();
    }

    private static bool TryReadPositiveArgument(
        IReadOnlyList<string> arguments,
        int index,
        int defaultValue,
        out int value)
    {
        if (arguments.Count <= index)
        {
            value = defaultValue;
            return true;
        }

        return int.TryParse(arguments[index], out value) && value > 0;
    }

    private static void WriteValidationFailures(IEnumerable<OptionsValidationException> exceptions)
    {
        Console.Error.WriteLine("Configuration validation failed:");
        foreach (var failure in exceptions
                     .SelectMany(exception => exception.Failures)
                     .Distinct(StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"- {failure}");
        }
    }
}
