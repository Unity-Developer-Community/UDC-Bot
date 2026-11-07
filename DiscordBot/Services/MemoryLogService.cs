using Discord.WebSocket;
using DiscordBot.Services.Server;
using DiscordBot.Settings;

namespace DiscordBot.Services.Logging;

/// <summary>
/// Writes the bot's memory and cache sizes to the console and log file every <see cref="Interval"/>,
/// so growth over days can be read from <c>log.txt</c>. Never posts to a channel.
/// </summary>
public class MemoryLogService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(4);

    private readonly ILoggingService _logging;
    private readonly DiscordSocketClient _client;
    private readonly ulong _guildId;
    private readonly CancellationToken _shutdownToken;

    public MemoryLogService(ILoggingService logging, DiscordSocketClient client, BotSettings settings, CancellationTokenSource cts)
    {
        _logging = logging;
        _client = client;
        _guildId = settings.GuildId;
        _shutdownToken = cts.Token;

        RunAsync().SafeFireAndForget(nameof(MemoryLogService));
    }

    private async Task RunAsync()
    {
        var startedAt = DateTimeOffset.UtcNow;
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(_shutdownToken))
        {
            var stats = MemoryStats.Capture(_client, _guildId);
            await _logging.Log(LogBehaviour.Console | LogBehaviour.File,
                $"[{nameof(MemoryLogService)}] {Line(stats, DateTimeOffset.UtcNow - startedAt)}");
        }
    }

    /// <summary><c>Up 4h: 125 MB (heap 35 MB live, 43 MB committed); 1,234 members, 5,678 cached messages, 95 channels.</c></summary>
    internal static string Line(MemoryStats stats, TimeSpan uptime) =>
        $"Up {MemberLogCards.StayDuration(uptime)}: {stats.MemoryText}; {stats.CacheText}.";
}
