using System.Net;
using System.Text;
using Discord;
using Discord.Net.Rest;
using Discord.WebSocket;
using DiscordBot.Services.Recruitment;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Recruitment;

/// <summary>Exercise the pinned Discord.Net REST boundary through an in-memory transport.</summary>
[TestClass]
public sealed class DiscordRecruitmentObserverTests
{
    [TestMethod]
    public async Task ArchiveShortPage_ContinuesWithArchiveTimestamp_NotCreationTime()
    {
        using var fixture = new Fixture(); await fixture.Login();
        fixture.Transport.Reply = (_, path, _) => path.Contains("/threads/archived/public", StringComparison.Ordinal)
            ? "{\"threads\":[" + ThreadJson + "],\"members\":[],\"has_more\":true}" : ForumJson;
        var page = await fixture.Observer.GetArchivedAsync(101, null, default);
        Assert.AreEqual(1, page.Threads.Count); Assert.IsFalse(page.Complete);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero), page.BeforeUtc);
        Assert.AreEqual(101ul, page.Threads[0].ParentId);
        Assert.IsTrue(page.Threads[0].HasClosedTag);
        fixture.Transport.Reply = (_, path, _) => path.Contains("/threads/archived/public", StringComparison.Ordinal)
            ? "{\"threads\":[],\"members\":[],\"has_more\":false}" : ForumJson;
        var end = await fixture.Observer.GetArchivedAsync(101, page.BeforeUtc, default);
        Assert.IsTrue(end.Complete);
        StringAssert.Contains(Uri.UnescapeDataString(fixture.Transport.Calls.Last().Path), "2026-09-04");
        Assert.IsTrue(fixture.Transport.Calls.All(c => c.Method == "GET"));
    }

    [TestMethod]
    public async Task ThreadParentAndStarterIdentity_UseActualDiscordNetTypesAndRequests()
    {
        using var fixture = new Fixture(); await fixture.Login();
        fixture.Transport.Reply = (_, path, _) => path.EndsWith("/channels/101", StringComparison.Ordinal) ? ForumJson :
            path.Contains("/messages/", StringComparison.Ordinal) ? MessageJson :
            path.Contains("/messages?", StringComparison.Ordinal) ? "[" + MessageJson + "]" : ThreadJson;
        var thread = await fixture.Observer.GetThreadAsync(1000000000000000000, default);
        Assert.AreEqual(101ul, thread!.ParentId);
        await fixture.Observer.GetStarterAsync(thread.Id, default);
        var replies = await fixture.Observer.GetRepliesAsync(thread.Id, 1000000000000000001, default);
        Assert.IsTrue(replies.Complete);
        Assert.IsNull(replies.Messages.Single().Response.IsModerator); // Current roles cannot prove historical roles.
        Assert.IsTrue(fixture.Transport.Calls.Any(c => c.Path.EndsWith("/messages/1000000000000000000", StringComparison.Ordinal)));
        Assert.IsTrue(fixture.Transport.Calls.Any(c => c.Path.Contains("after=1000000000000000001", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task FeedSend_TargetsOnlyConfiguredTextChannel_WithMentionsDisabled()
    {
        using var fixture = new Fixture(); await fixture.Login();
        fixture.Transport.Reply = (method, _, _) => method == "POST" ? MessageJson :
            "{\"id\":\"105\",\"guild_id\":\"1\",\"type\":0,\"name\":\"staff-feed\",\"permission_overwrites\":[]}";
        await fixture.Observer.SendFeedAsync("Observation @everyone", default);
        var sent = fixture.Transport.Calls.Single(c => c.Method == "POST");
        Assert.IsTrue(sent.Path.EndsWith("/channels/105/messages", StringComparison.Ordinal));
        using var body = System.Text.Json.JsonDocument.Parse(sent.Body!);
        var mentions = body.RootElement.GetProperty("allowed_mentions");
        // Discord.Net 3.17.4 serializes AllowedMentions.None with null parse and empty ID lists.
        foreach (var key in new[] { "parse", "users", "roles" })
        {
            var value = mentions.GetProperty(key);
            Assert.IsTrue(value.ValueKind == System.Text.Json.JsonValueKind.Null || value.GetArrayLength() == 0);
        }
        Assert.IsTrue(fixture.Transport.Calls.All(c => c.Method is "GET" or "POST"));
    }

    private const string ForumJson = """
        {"id":"101","guild_id":"1","type":15,"name":"recruiting","permission_overwrites":[],
        "available_tags":[{"id":"700","name":"Closed","moderated":false}],"default_auto_archive_duration":1440}
        """;
    private const string ThreadJson = """
        {"id":"1000000000000000000","guild_id":"1","type":11,"name":"Listing","parent_id":"101","owner_id":"123",
        "applied_tags":["700"],"thread_metadata":{"archived":true,"auto_archive_duration":1440,
        "archive_timestamp":"2026-09-04T12:00:00Z","locked":false,"create_timestamp":"2026-08-01T12:00:00Z"}}
        """;
    private const string MessageJson = """
        {"id":"1000000000000000002","channel_id":"1000000000000000000","type":0,"content":"Budget $25/hour",
        "timestamp":"2026-09-05T12:00:00Z","author":{"id":"456","username":"reader","discriminator":"0","bot":false},
        "attachments":[],"embeds":[],"mentions":[],"mention_roles":[],"pinned":false,"tts":false,"mention_everyone":false}
        """;

    private sealed class Fixture : IDisposable
    {
        public readonly Transport Transport = new();
        private readonly DiscordSocketClient _client;
        public DiscordRecruitmentObserver Observer { get; }
        public Fixture()
        {
            _client = new(new DiscordSocketConfig { RestClientProvider = _ => Transport });
            var options = Options.Create(RecruitmentTestData.Options());
            Observer = new(_client, null!, new(options), options,
                Options.Create(new DiscordGuildOptions { GuildId = 1 }), Options.Create(new AuthorizationOptions { ModeratorRoleId = 77 }));
        }
        public Task Login() => _client.LoginAsync(TokenType.Bot, "offline-test-token", validateToken: false);
        public void Dispose() => _client.Dispose();
    }

    private sealed class Transport : IRestClient
    {
        public Func<string, string, string?, string> Reply = (_, _, _) =>
            "{\"id\":\"999\",\"username\":\"bot\",\"discriminator\":\"0\",\"bot\":true}";
        public readonly List<(string Method, string Path, string? Body)> Calls = [];
        public void SetHeader(string key, string value) { }
        public void SetCancelToken(CancellationToken cancellationToken) { }
        public void Dispose() { }
        private Task<RestResponse> Respond(string method, string endpoint, string? body)
        {
            endpoint = "/" + endpoint.TrimStart('/');
            Calls.Add((method, endpoint, body));
            return Task.FromResult(new RestResponse(HttpStatusCode.OK, [], new MemoryStream(Encoding.UTF8.GetBytes(Reply(method, endpoint, body)))));
        }
        public Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken token, bool headerOnly,
            string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders) => Respond(method, endpoint, null);
        public Task<RestResponse> SendAsync(string method, string endpoint, string json, CancellationToken token, bool headerOnly,
            string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders) => Respond(method, endpoint, json);
        public Task<RestResponse> SendAsync(string method, string endpoint, IReadOnlyDictionary<string, object> multipart, CancellationToken token,
            bool headerOnly, string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> requestHeaders) => throw new AssertFailedException("Observe does not upload files.");
    }
}
