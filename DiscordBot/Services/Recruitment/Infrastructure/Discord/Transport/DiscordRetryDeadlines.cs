using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using Discord.Net.Rest;
using Newtonsoft.Json.Linq;
using DiscordBot.Services.Recruitment.Infrastructure.Discord;

namespace DiscordBot.Services.Recruitment;

/// <summary>One production instance tracks Discord's bot-wide and major-route retry deadlines.</summary>
internal sealed class DiscordRetryDeadlines(TimeProvider? time = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((duration, ct) => Task.Delay(duration, time ?? TimeProvider.System, ct));
    private readonly ConcurrentDictionary<string, DateTimeOffset> DeferredRoutes = new();
    private long _globalUntilTicks;

    internal void CheckGlobal()
    {
        var global = new DateTimeOffset(Interlocked.Read(ref _globalUntilTicks), TimeSpan.Zero);
        if (global > _time.GetUtcNow()) throw new RecruitmentDeferredException(global);
    }

    public async Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken ct, Func<Task<RestResponse>> send, bool recruitment)
    {
        // Discord buckets include a major channel/guild identity. Conservatively defer that identity
        // across methods, message IDs and query strings so a different URL cannot evade a known limit.
        var path = endpoint.Split('?')[0].Split('/');
        var key = path.Length >= 2 && path[0] is "channels" or "guilds" or "webhooks"
            ? path[0] + "/" + path[1] : method + " " + endpoint.Split('?')[0];
        var now = _time.GetUtcNow();
        var until = DeferredRoutes.GetValueOrDefault(key);
        var global = new DateTimeOffset(Interlocked.Read(ref _globalUntilTicks), TimeSpan.Zero);
        if (global > until) until = global;
        if (until > now)
        {
            if (recruitment) throw new RecruitmentDeferredException(until);
            await _delay(until - now, ct); // Other bot features retain their usual waiting behavior.
        }
        ct.ThrowIfCancellationRequested();
        var response = await send();
        if (response.StatusCode != HttpStatusCode.TooManyRequests) return response;
        using var original = response.Stream;
        using var reader = new StreamReader(original);
        var body = await reader.ReadToEndAsync(ct);
        var payload = JObject.Parse(body);
        var seconds = (double?)payload["retry_after"];
        if (!seconds.HasValue && response.Headers.TryGetValue("Retry-After", out var header) &&
            double.TryParse(header, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) seconds = value;
        // Include a small cushion; never shorten the server's retry window.
        now = _time.GetUtcNow();
        until = now.AddSeconds(Math.Max(1, seconds ?? 60) + 1);
        DeferredRoutes.AddOrUpdate(key, until, (_, prior) => prior > until ? prior : until);
        if ((bool?)payload["global"] == true)
        {
            long previous;
            do { previous = Interlocked.Read(ref _globalUntilTicks); }
            while (previous < until.UtcTicks && Interlocked.CompareExchange(ref _globalUntilTicks, until.UtcTicks, previous) != previous);
        }
        if (DeferredRoutes.Count > 1024)
            foreach (var expired in DeferredRoutes.Where(x => x.Value <= now)) DeferredRoutes.TryRemove(expired.Key, out _);
        if (recruitment) throw new RecruitmentDeferredException(until);
        return new RestResponse(response.StatusCode, response.Headers, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body)));
    }
}
