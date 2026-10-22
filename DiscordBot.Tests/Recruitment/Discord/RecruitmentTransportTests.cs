using DiscordBot.Services.Recruitment.Infrastructure.Discord;
using System.Net;
using System.Text;
using Discord;
using Discord.Net.Rest;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Settings;
using DiscordBot.Services.Recruitment;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Tests.Recruitment;

[Collection("Recruitment transport")]
public class RecruitmentTransportTests
{
    [Fact]
    public async Task InstalledSdkPreservesModerationEmojisAndFlagsThroughStagedPublication()
    {
        var transport = new ForumTransport();
        using var client = new DiscordRestClient(new DiscordRestConfig
        { RestClientProvider = DiscordCompatibilityTransport.Wrap(_ => transport) });
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
        using var transport = new DiscordCompatibilityTransport(inner);
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync("PATCH", "channels/11", "{}",
            default, false, DiscordCompatibilityTransport.TagWriteReason, Array.Empty<KeyValuePair<string, IEnumerable<string>>>()));
        Assert.Empty(inner.Writes);
    }

    [Fact]
    public async Task InventoryReusesForumsAndFetchesGuildActiveThreadsOnce()
    {
        var transport = new ForumTransport { InventoryMode = true };
        using var client = new DiscordSocketClient(new DiscordSocketConfig
        { RestClientProvider = DiscordCompatibilityTransport.Wrap(_ => transport) });
        await client.LoginAsync(TokenType.Bot, "offline-placeholder-token", validateToken: false);
        var settings = new BotSettings { GuildId = 1, Recruitment = new RecruitmentSettings
        { Forums = new RecruitmentForums { PaidRecruitingId = 11, PaidForHireId = 12, HobbyRecruitingId = 13, HobbyForHireId = 14 } } };
        var adapter = new DiscordRecruitmentAdapter(client, settings);
        ForumSnapshot? observed = null;
        foreach (var id in settings.Recruitment.Forums.Mapping().Values)
        {
            var snapshot = await adapter.GetForumAsync(id.ToString(), default);
            if (id == 11) observed = snapshot;
        }
        var floor = DateTimeOffset.UtcNow.AddDays(-30);
        var inventory = await adapter.InventoryAsync(floor, new HashSet<string>(),
            settings.Recruitment.Forums.Mapping().Values.ToDictionary(id => id.ToString(), _ => new ArchiveCursor(floor)), default);
        Assert.True(inventory.Complete);
        Assert.Single(transport.Requests, r => r.Contains("/threads/active"));
        Assert.Equal(4, transport.Requests.Count(r => r is "GET channels/11" or "GET channels/12" or "GET channels/13" or "GET channels/14"));
        var updated = await adapter.PublishForumAsync(observed!, "Updated Guidelines", null, false, default);
        Assert.True(adapter.IsExpectedForumUpdate(updated));
        Assert.False(adapter.IsExpectedForumUpdate(updated with { Topic = "External edit" }));
    }

    [Fact]
    public async Task InventoryObservationRetainsIdentitiesDiscardedBySdkParentFilter()
    {
        var transport = new ForumTransport { InventoryMode = true, IncludeOtherForumThread = true };
        using var client = new DiscordRestClient(new DiscordRestConfig { RestClientProvider = DiscordCompatibilityTransport.Wrap(_ => transport) });
        await client.LoginAsync(TokenType.Bot, "offline-placeholder-token", validateToken: false);
        var forum = Assert.IsType<RestForumChannel>(await client.GetChannelAsync(11));
        using var observed = DiscordCompatibilityTransport.ObserveActiveInventory(1);
        Assert.Empty(await forum.GetActiveThreadsAsync());
        Assert.Equal((100UL, 12UL), Assert.Single(observed.Identities!));
        Assert.Single(transport.Requests, r => r.Contains("/threads/active"));
    }

    [Theory]
    [InlineData(235)]
    [InlineData(537)]
    public async Task RateLimitReturnsDeadlineWithoutBlockingWorkerOrResending(int seconds)
    {
        using var requestScope = RecruitmentRequests.BeginScope(new DiscordRetryDeadlines());
        var transport = new ForumTransport { RetrySeconds = seconds };
        using var client = new DiscordRestClient(new DiscordRestConfig
        { RestClientProvider = DiscordCompatibilityTransport.Wrap(_ => transport) });
        await client.LoginAsync(TokenType.Bot, "offline-placeholder-token", validateToken: false);
        var started = DateTimeOffset.UtcNow;
        var error = await Assert.ThrowsAsync<RecruitmentDeferredException>(async () =>
            await client.GetChannelAsync((ulong)seconds, RecruitmentRequests.Options(default)).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(error.RetryAt >= started.AddSeconds(seconds));
        await Assert.ThrowsAsync<RecruitmentDeferredException>(async () =>
            await client.GetChannelAsync((ulong)seconds, RecruitmentRequests.Options(default)).WaitAsync(TimeSpan.FromSeconds(2)));
        using var sameChannel = new DiscordCompatibilityTransport(transport);
        _ = RecruitmentRequests.Options(default);
        await Assert.ThrowsAsync<RecruitmentDeferredException>(() => sameChannel.SendAsync("GET", $"channels/{seconds}/messages?limit=100",
            default, false, "", Array.Empty<KeyValuePair<string, IEnumerable<string>>>()).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, transport.RateLimitRequests);
    }

    [Fact]
    public async Task GlobalRateLimitDefersOtherRecruitmentRoutesBeforeSdkWaiting()
    {
        using var requestScope = RecruitmentRequests.BeginScope(new DiscordRetryDeadlines());
        var transport = new ForumTransport { RetrySeconds = 1, GlobalLimit = true };
        using var client = new DiscordRestClient(new DiscordRestConfig { RestClientProvider = DiscordCompatibilityTransport.Wrap(_ => transport) });
        await client.LoginAsync(TokenType.Bot, "offline-placeholder-token", validateToken: false);
        var error = await Assert.ThrowsAsync<RecruitmentDeferredException>(async () =>
            await client.GetChannelAsync(1, RecruitmentRequests.Options(default)).WaitAsync(TimeSpan.FromSeconds(2)));
        var other = Assert.Throws<RecruitmentDeferredException>(() => RecruitmentRequests.Options(default));
        Assert.Equal(error.RetryAt, other.RetryAt);
        Assert.Equal(1, transport.RateLimitRequests);
    }

    // Uses the real installed SDK's request queue and serialization, but never a network transport.
    private sealed class ForumTransport : IRestClient
    {
        public bool InventoryMode { get; init; }
        public bool IncludeOtherForumThread { get; init; }
        public List<string> Requests { get; } = new();
        public int RetrySeconds { get; init; }
        public bool GlobalLimit { get; init; }
        public int RateLimitRequests { get; private set; }
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
            Requests.Add(method + " " + endpoint);
            await Task.Yield(); // Verify the SDK preserves the scoped correction across asynchronous work.
            string body;
            if (RetrySeconds > 0 && endpoint == $"channels/{RetrySeconds}")
            {
                RateLimitRequests++;
                return new RestResponse(HttpStatusCode.TooManyRequests, new Dictionary<string, string>(),
                    new MemoryStream(Encoding.UTF8.GetBytes($"{{\"retry_after\":{RetrySeconds},\"global\":{GlobalLimit.ToString().ToLowerInvariant()}}}")));
            }
            if (method == "GET" && endpoint == "users/@me") body = """{"id":"999","username":"offline","discriminator":"0","bot":true}""";
            else if (method == "GET" && endpoint == "oauth2/applications/@me") body = """{"id":"999","name":"offline","description":"","bot_public":true,"bot_require_code_grant":false,"verify_key":"00"}""";
            else if (IncludeOtherForumThread && endpoint.EndsWith("/threads/active")) body = """{"threads":[{"id":"100","parent_id":"12","guild_id":"1","type":11}],"members":[]}""";
            else if (InventoryMode && endpoint.Contains("/threads/")) body = """{"threads":[],"members":[],"has_more":false}""";
            else if (InventoryMode && endpoint.StartsWith("guilds/1")) body = """{"id":"1","name":"Offline guild","owner_id":"999","roles":[],"emojis":[],"features":[],"stickers":[],"afk_timeout":300,"preferred_locale":"en-US"}""";
            else if ((endpoint == "channels/11" || InventoryMode && endpoint is "channels/12" or "channels/13" or "channels/14") && method is "GET" or "PATCH")
            {
                if (method == "PATCH")
                {
                    var patch = JObject.Parse(json!); Writes.Add(patch);
                    foreach (var property in patch.Properties()) _forum[property.Name] = property.Value.DeepClone();
                    var next = 60;
                    foreach (var tag in (JArray)_forum["available_tags"]!)
                        if (tag["id"] is null) tag["id"] = (++next).ToString();
                }
                var result = (JObject)_forum.DeepClone(); result["id"] = endpoint.Split('/')[1];
                body = result.ToString();
            }
            else throw new InvalidOperationException($"Unexpected offline route: {method} {endpoint}");
            return new RestResponse(HttpStatusCode.OK, new Dictionary<string, string>(), new MemoryStream(Encoding.UTF8.GetBytes(body)));
        }
    }
}
