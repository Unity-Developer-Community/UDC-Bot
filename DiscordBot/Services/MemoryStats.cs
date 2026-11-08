using Discord.WebSocket;

namespace DiscordBot.Services.Logging;

/// <summary>
/// The bot's memory and what Discord.Net caches, for the startup report and <see cref="MemoryLogService"/>.
/// <see cref="WorkingSet"/> is what the OS counts (a container's usage); <see cref="LiveHeap"/> and
/// <see cref="Committed"/> come from the last garbage collection, so garbage not yet collected doesn't count.
/// </summary>
public sealed record MemoryStats(long WorkingSet, long LiveHeap, long Committed, int Members, int CachedMessages, int Channels)
{
    public static MemoryStats Capture(DiscordSocketClient? client, ulong guildId)
    {
        var gc = GC.GetGCMemoryInfo();
        var guild = client?.GetGuild(guildId);
        // TextChannels includes threads and voice-channel chats, which keep their own message caches.
        var messages = guild?.TextChannels.Sum(c => c.CachedMessages.Count) ?? 0;
        return new MemoryStats(Environment.WorkingSet, gc.HeapSizeBytes - gc.FragmentedBytes, gc.TotalCommittedBytes,
            guild?.Users.Count ?? 0, messages, guild?.Channels.Count ?? 0);
    }

    /// <summary><c>125 MB (heap 35 MB live, 43 MB committed)</c></summary>
    public string MemoryText => $"{Megabytes(WorkingSet)} (heap {Megabytes(LiveHeap)} live, {Megabytes(Committed)} committed)";

    /// <summary><c>1,234 members, 5,678 cached messages, 95 channels</c></summary>
    public string CacheText =>
        $"{Count(Members, "member")}, {Count(CachedMessages, "cached message")}, {Count(Channels, "channel")}";

    public static string Megabytes(long bytes) => $"{bytes / (1024.0 * 1024.0):0} MB";

    /// <summary><c>+8 MB</c>, <c>-3 MB</c> or <c>±0 MB</c>.</summary>
    public static string Change(long bytes)
    {
        var mb = Math.Round(bytes / (1024.0 * 1024.0));
        return mb == 0 ? "±0 MB" : $"{mb:+0;-0} MB";
    }

    private static string Count(int n, string noun) =>
        $"{n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} {noun}{(n == 1 ? "" : "s")}";
}
