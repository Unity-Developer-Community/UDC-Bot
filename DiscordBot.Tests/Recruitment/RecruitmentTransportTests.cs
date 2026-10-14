using System.Net;
using System.Text;
using Discord;
using Discord.Net.Rest;
using Discord.Rest;
using DiscordBot.Services.Recruitment;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentTransportTests
{
    [Fact]
    public async Task InstalledSdkPreservesModerationEmojisAndFlagsThroughStagedPublication()
    {
        var transport = new ForumTransport();
        using var client = new DiscordRestClient(new DiscordRestConfig
        { RestClientProvider = RecruitmentForumTransport.Wrap(_ => transport) });
        await client.LoginAsync(TokenType.Bot, "offline-placeholder-token", validateToken: false);
        var forum = Assert.IsType<RestForumChannel>(await client.GetChannelAsync(11));
        var tags = new[] { new LiveTag("51", "Staff", true, new Emoji("⭐")), new LiveTag("", "Programming", false) };
        var first = await DiscordRecruitmentAdapter.ModifyForumAsync(forum, "Native Guidelines", tags, false, new RequestOptions());
        Assert.False(first.RequireTag);
        Assert.All(first.Tags, t => Assert.NotEmpty(t.Id));
        Assert.True(first.Tags.Single(t => t.Id == "51").Moderated);
        await DiscordRecruitmentAdapter.ModifyForumAsync(forum, null, null, true, new RequestOptions());
        await DiscordRecruitmentAdapter.ModifyForumAsync(forum, "Rotated Guidelines", null, true, new RequestOptions());
        Assert.Equal(3, transport.Writes.Count);
        var tagWrite = transport.Writes[0];
        Assert.True((bool)tagWrite["available_tags"]![0]!["moderated"]!);
        Assert.False((bool)tagWrite["available_tags"]![1]!["moderated"]!);
        Assert.Equal("⭐", (string?)tagWrite["available_tags"]![0]!["emoji_name"]);
        Assert.Equal(32768, (int)tagWrite["flags"]!);
        Assert.Equal(32784, (int)transport.Writes[1]["flags"]!);
        Assert.Null(transport.Writes[1]["available_tags"]);
        Assert.Null(transport.Writes[2]["available_tags"]);
        Assert.Equal(32784, (int)transport.Writes[2]["flags"]!);
    }

    [Fact]
    public async Task TagPublicationWithoutScopeFailsBeforeTransport()
    {
        var inner = new ForumTransport();
        using var transport = new RecruitmentForumTransport(inner);
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync("PATCH", "channels/11", "{}",
            default, false, RecruitmentForumTransport.TagWriteReason, Array.Empty<KeyValuePair<string, IEnumerable<string>>>()));
        Assert.Empty(inner.Writes);
    }

    // Uses the real installed SDK's request queue and serialization, but never a network transport.
    private sealed class ForumTransport : IRestClient
    {
        public List<JObject> Writes { get; } = new();
        private readonly JObject _forum = JObject.Parse("""
            {"id":"11","guild_id":"1","type":15,"name":"recruitment","topic":"","flags":32768,
             "available_tags":[{"id":"51","name":"Staff","moderated":true,"emoji_name":"⭐"}]}
            """);
        public void Dispose() { }
        public void SetHeader(string key, string value) { }
        public void SetCancelToken(CancellationToken cancelToken) { }
        public Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken cancelToken, bool headerOnly,
            string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders) => Respond(method, endpoint, null);
        public Task<RestResponse> SendAsync(string method, string endpoint, string json, CancellationToken cancelToken, bool headerOnly,
            string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders) => Respond(method, endpoint, json);
        public Task<RestResponse> SendAsync(string method, string endpoint, IReadOnlyDictionary<string, object> multipart,
            CancellationToken cancelToken, bool headerOnly, string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders) =>
            throw new InvalidOperationException("Unexpected multipart request");
        private async Task<RestResponse> Respond(string method, string endpoint, string? json)
        {
            await Task.Yield(); // Verify the SDK preserves the scoped correction across asynchronous work.
            string body;
            if (method == "GET" && endpoint == "users/@me") body = """{"id":"999","username":"offline","discriminator":"0","bot":true}""";
            else if (method == "GET" && endpoint == "oauth2/applications/@me") body = """{"id":"999","name":"offline","description":"","bot_public":true,"bot_require_code_grant":false,"verify_key":"00"}""";
            else if (endpoint == "channels/11" && method is "GET" or "PATCH")
            {
                if (method == "PATCH")
                {
                    var patch = JObject.Parse(json!); Writes.Add(patch);
                    foreach (var property in patch.Properties()) _forum[property.Name] = property.Value.DeepClone();
                    var next = 60;
                    foreach (var tag in (JArray)_forum["available_tags"]!)
                        if (tag["id"] is null) tag["id"] = (++next).ToString();
                }
                body = _forum.ToString();
            }
            else throw new InvalidOperationException($"Unexpected offline route: {method} {endpoint}");
            return new RestResponse(HttpStatusCode.OK, new Dictionary<string, string>(), new MemoryStream(Encoding.UTF8.GetBytes(body)));
        }
    }
}
