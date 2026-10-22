using Newtonsoft.Json.Linq;
using Discord.Net.Rest;
using System.IO;
using System.Net;
using System.Text;

namespace DiscordBot.Services.Recruitment;

/// <summary>Corrects only explicitly scoped recruitment V2 writes through the SDK queue.</summary>
internal static class RecruitmentMessageTransport
{
    private sealed record Publication(string Endpoint, string Method, IReadOnlyList<IAttachment> Retained);
    private static readonly AsyncLocal<Publication?> Current = new();
    private static readonly AsyncLocal<(ulong Channel, ulong Message)?> PinScope = new();
    internal static IDisposable Pin(ulong channel, ulong message)
    {
        var previous = PinScope.Value; PinScope.Value = (channel, message);
        return new Scope(() => PinScope.Value = previous);
    }
    internal static string PinEndpoint(string method, string endpoint) =>
        PinScope.Value is { } pin && method == "PUT" && endpoint == $"channels/{pin.Channel}/pins/{pin.Message}"
            ? $"channels/{pin.Channel}/messages/pins/{pin.Message}" : endpoint;
    private static readonly AsyncLocal<ulong?> ObservedChannel = new();
    internal static IDisposable Observe(ulong channelId)
    {
        var previous = ObservedChannel.Value; ObservedChannel.Value = channelId;
        return new Scope(() => ObservedChannel.Value = previous);
    }

    // Current Discord may expose uploaded component media only as media.attachment_id;
    // 3.20.1 drops that response field. Preserve it as SDK attachment metadata before parsing.
    internal static async Task<RestResponse> ObserveResponseAsync(string endpoint, RestResponse response, CancellationToken ct)
    {
        if (ObservedChannel.Value is not { } channelId || response.StatusCode != HttpStatusCode.OK ||
            !(endpoint == $"channels/{channelId}/messages" || endpoint.StartsWith($"channels/{channelId}/messages/", StringComparison.Ordinal) ||
              endpoint.StartsWith($"channels/{channelId}/messages?", StringComparison.Ordinal))) return response;
        using var stream = response.Stream;
        using var reader = new StreamReader(stream);
        var body = JToken.Parse(await reader.ReadToEndAsync(ct));
        foreach (var message in body is JArray messages ? messages.OfType<JObject>() : body is JObject single ? new[] { single } : Array.Empty<JObject>())
        {
            if (((int?)message["flags"] & (int)MessageFlags.ComponentsV2) != (int)MessageFlags.ComponentsV2 || (string?)message["channel_id"] != channelId.ToString()) continue;
            if (message["attachments"] is not JArray) message["attachments"] = new JArray();
            var attachments = (JArray)message["attachments"]!;
            foreach (var media in message["components"]?.SelectTokens("$..media").OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                var url = (string?)media["url"];
                if (url is null) continue;
                var filename = url.StartsWith("attachment://", StringComparison.Ordinal) ? url[13..] : null;
                var id = (string?)media["attachment_id"];
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host is "cdn.discordapp.com" or "media.discordapp.net")
                {
                    var parts = uri.AbsolutePath.Split('/');
                    if (parts.Length == 5 && parts[1] == "attachments" && parts[2] == channelId.ToString())
                    { filename = Uri.UnescapeDataString(parts[4]); id ??= parts[3]; }
                }
                if (!ulong.TryParse(id, out var attachmentId) || attachmentId == 0 || filename is null || filename.Length > 120 ||
                    filename.IndexOfAny(new[] { '/', '\\' }) >= 0 || attachments.Any(a => (string?)a["id"] == id)) continue;
                attachments.Add(new JObject { ["id"] = id, ["filename"] = filename, ["size"] = 0, ["url"] = url,
                    ["proxy_url"] = (string?)media["proxy_url"] ?? url, ["content_type"] = (string?)media["content_type"] });
            }
        }
        return new RestResponse(response.StatusCode, response.Headers, new MemoryStream(Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None))));
    }
    internal static IDisposable Publish(ulong channelId, ulong? messageId, IReadOnlyList<IAttachment> retained)
    {
        var previous = Current.Value;
        Current.Value = new(messageId.HasValue ? $"channels/{channelId}/messages/{messageId}" : $"channels/{channelId}/messages",
            messageId.HasValue ? "PATCH" : "POST", retained);
        return new Scope(() => Current.Value = previous);
    }
    private sealed class Scope(Action restore) : IDisposable { public void Dispose() => restore(); }
    internal static string Apply(string method, string endpoint, string json, bool multipart = false)
    {
        if (Current.Value is not { } scope || method != scope.Method || endpoint != scope.Endpoint) return json;
        var body = JObject.Parse(json);
        if ((int?)body["flags"] is not { } flags || (flags & (int)MessageFlags.ComponentsV2) == 0)
            throw new InvalidOperationException("Recruitment V2 publication flag is missing.");
        foreach (var name in new[] { "content", "embeds", "poll", "sticker_ids" })
        {
            if (body[name] is not { } field) continue;
            if (field.Type != JTokenType.Null && field.ToString().Length != 0 && (field is not JArray array || array.Count != 0))
                throw new InvalidOperationException("Recruitment V2 payload has incompatible message fields.");
            body.Remove(name);
        }
        // In 3.20.1 FileAttachment always means upload. The JSON edit path otherwise omits
        // attachments entirely; send the exact returned IDs so removals/retention are explicit.
        if (method == "PATCH" && !multipart)
        {
            if (body["attachments"] is not null) throw new InvalidOperationException("Unexpected attachment retention payload.");
            body["attachments"] = new JArray(scope.Retained.Select(a => new JObject { ["id"] = a.Id.ToString(), ["filename"] = a.Filename }));
        }
        return body.ToString(Newtonsoft.Json.Formatting.None);
    }
}
