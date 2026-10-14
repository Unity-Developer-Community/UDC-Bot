using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using Discord.Net.Rest;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Services.Recruitment;

/// <summary>Hands Discord retry deadlines back to the worker instead of sleeping inside the SDK queue.</summary>
internal static class RecruitmentRequests
{
    private static readonly AsyncLocal<bool> RecruitmentCall = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> DeferredRoutes = new();
    private static long _globalUntilTicks;

    public static RequestOptions Options(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        RecruitmentCall.Value = true;
        var global = new DateTimeOffset(Interlocked.Read(ref _globalUntilTicks), TimeSpan.Zero);
        if (global > DateTimeOffset.UtcNow) throw new RecruitmentDeferredException(global);
        return new RequestOptions { CancelToken = ct, Timeout = 15000, RetryMode = RetryMode.AlwaysFail };
    }

    public static async Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken ct, Func<Task<RestResponse>> send)
    {
        // Discord buckets include a major channel/guild identity. Conservatively defer that identity
        // across methods, message IDs and query strings so a different URL cannot evade a known limit.
        var path = endpoint.Split('?')[0].Split('/');
        var key = path.Length >= 2 && path[0] is "channels" or "guilds" or "webhooks"
            ? path[0] + "/" + path[1] : method + " " + endpoint.Split('?')[0];
        var now = DateTimeOffset.UtcNow;
        var until = DeferredRoutes.GetValueOrDefault(key);
        var global = new DateTimeOffset(Interlocked.Read(ref _globalUntilTicks), TimeSpan.Zero);
        if (global > until) until = global;
        if (until > now)
        {
            if (RecruitmentCall.Value) throw new RecruitmentDeferredException(until);
            await Task.Delay(until - now, ct); // Other bot features retain their usual waiting behavior.
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
        now = DateTimeOffset.UtcNow;
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
        if (RecruitmentCall.Value) throw new RecruitmentDeferredException(until);
        return new RestResponse(response.StatusCode, response.Headers, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body)));
    }
}
