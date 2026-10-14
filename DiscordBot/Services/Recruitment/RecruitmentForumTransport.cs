using Discord.Net.Rest;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Services.Recruitment;

/// <summary>Corrects recruitment tag serialization without bypassing Discord.Net's request queue.</summary>
internal sealed class RecruitmentForumTransport(IRestClient inner) : IRestClient
{
    internal const string TagWriteReason = "Recruitment: publish forum tags";
    private static readonly AsyncLocal<TagScope?> Current = new();
    private sealed record TagScope(string Endpoint, IReadOnlyList<LiveTag> Tags);

    public static RestClientProvider Wrap(RestClientProvider provider) => url => new RecruitmentForumTransport(provider(url));

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

    public Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken cancelToken,
        bool headerOnly, string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders) =>
        inner.SendAsync(method, endpoint, cancelToken, headerOnly, reason, requestHeaders);

    public Task<RestResponse> SendAsync(string method, string endpoint, IReadOnlyDictionary<string, object> multipart,
        CancellationToken cancelToken, bool headerOnly, string reason,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders) =>
        inner.SendAsync(method, endpoint, multipart, cancelToken, headerOnly, reason, requestHeaders);

    public Task<RestResponse> SendAsync(string method, string endpoint, string json, CancellationToken cancelToken,
        bool headerOnly, string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders)
    {
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
        return inner.SendAsync(method, endpoint, json, cancelToken, headerOnly, reason, requestHeaders);
    }
}
