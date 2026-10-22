using Discord.Net.Rest;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Services.Recruitment;

/// <summary>Shared Discord.Net 3.20.1 compatibility facade. Corrections are scoped; rate-limit deadlines are bot-wide.</summary>
internal sealed class DiscordCompatibilityTransport(IRestClient inner) : IRestClient
{
    internal const string TagWriteReason = "Recruitment: publish forum tags";
    private static readonly AsyncLocal<TagScope?> Current = new();
    private static readonly AsyncLocal<ActiveInventory?> Inventory = new();
    internal sealed class ActiveInventory(ulong guildId, ActiveInventory? previous) : IDisposable
    {
        public ulong GuildId { get; } = guildId;
        public IReadOnlyList<(ulong Id, ulong Parent)>? Identities { get; set; }
        public void Dispose() => Inventory.Value = previous;
    }
    internal static ActiveInventory ObserveActiveInventory(ulong guildId)
    {
        var scope = new ActiveInventory(guildId, Inventory.Value);
        Inventory.Value = scope;
        return scope;
    }
    private sealed record TagScope(string Endpoint, IReadOnlyList<LiveTag> Tags);

    public static RestClientProvider Wrap(RestClientProvider provider) => url => new DiscordCompatibilityTransport(provider(url));

    internal static IDisposable PreserveTags(ulong forumId, IReadOnlyList<LiveTag> tags)
    {
        var previous = Current.Value;
        Current.Value = new TagScope($"channels/{forumId}", tags);
        return new Scope(() => Current.Value = previous);
    }

    private sealed class Scope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    public void Dispose() => inner.Dispose();
    public void SetHeader(string key, string value) => inner.SetHeader(key, value);
    public void SetCancelToken(CancellationToken cancelToken) => inner.SetCancelToken(cancelToken);

    public async Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken cancelToken,
        bool headerOnly, string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders)
    {
        endpoint = RecruitmentMessageTransport.PinEndpoint(method, endpoint);
        var response = await RecruitmentRequests.SendAsync(method, endpoint, cancelToken,
            () => inner.SendAsync(method, endpoint, cancelToken, headerOnly, reason, requestHeaders));
        var scope = Inventory.Value;
        if (scope is null || method != "GET" || endpoint != $"guilds/{scope.GuildId}/threads/active" || response.StatusCode != HttpStatusCode.OK)
            return await RecruitmentMessageTransport.ObserveResponseAsync(endpoint, response, cancelToken);
        using var stream = response.Stream;
        using var reader = new StreamReader(stream);
        var body = await reader.ReadToEndAsync(cancelToken);
        // Forum.GetActiveThreadsAsync downloads the entire guild, then discards other parents.
        // Retain their identities from that same response without private SDK APIs or another request.
        var threads = JObject.Parse(body)["threads"] as JArray ?? throw new InvalidOperationException("Active-thread inventory is incomplete.");
        scope.Identities = threads.Select(t =>
        {
            if (!ulong.TryParse((string?)t["id"], out var id) || id == 0 ||
                !ulong.TryParse((string?)t["parent_id"], out var parent) || parent == 0 ||
                t["guild_id"] is { Type: not JTokenType.Null } guild && (ulong)guild != scope.GuildId)
                throw new InvalidOperationException("Active-thread inventory identity is invalid.");
            return (id, parent);
        }).ToArray();
        return new RestResponse(response.StatusCode, response.Headers, new MemoryStream(Encoding.UTF8.GetBytes(body)));
    }

    public async Task<RestResponse> SendAsync(string method, string endpoint, IReadOnlyDictionary<string, object> multipart,
        CancellationToken cancelToken, bool headerOnly, string reason,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders)
    {
        var corrected = new Dictionary<string, object>(multipart);
        if (corrected.TryGetValue("payload_json", out var payload) && payload is string json)
            corrected["payload_json"] = RecruitmentMessageTransport.Apply(method, endpoint, json, multipart: true);
        var response = await RecruitmentRequests.SendAsync(method, endpoint, cancelToken,
            () => inner.SendAsync(method, endpoint, corrected, cancelToken, headerOnly, reason, requestHeaders));
        return await RecruitmentMessageTransport.ObserveResponseAsync(endpoint, response, cancelToken);
    }

    public async Task<RestResponse> SendAsync(string method, string endpoint, string json, CancellationToken cancelToken,
        bool headerOnly, string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders)
    {
        json = RecruitmentMessageTransport.Apply(method, endpoint, json);
        if (reason == TagWriteReason)
        {
            // 3.20.1 ForumHelper drops IsModerated. Fail closed if a future SDK stops carrying
            // the execution context or changes the payload, rather than demoting unrelated tags.
            var scope = Current.Value;
            if (scope is null || method != "PATCH" || endpoint != scope.Endpoint)
                throw new InvalidOperationException("Recruitment tag publication context is unavailable.");
            var body = JObject.Parse(json);
            if (body["available_tags"] is not JArray tags || tags.Count != scope.Tags.Count)
                throw new InvalidOperationException("Recruitment tag payload changed unexpectedly.");
            for (var i = 0; i < tags.Count; i++)
            {
                var expected = scope.Tags[i];
                if ((string?)tags[i]["name"] != expected.Name || ((string?)tags[i]["id"] ?? "") != expected.Id)
                    throw new InvalidOperationException("Recruitment tag payload identity changed unexpectedly.");
                tags[i]["moderated"] = expected.Moderated;
            }
            json = body.ToString(Formatting.None);
        }
        var response = await RecruitmentRequests.SendAsync(method, endpoint, cancelToken, () => inner.SendAsync(method, endpoint, json, cancelToken, headerOnly, reason, requestHeaders));
        return await RecruitmentMessageTransport.ObserveResponseAsync(endpoint, response, cancelToken);
    }
}
