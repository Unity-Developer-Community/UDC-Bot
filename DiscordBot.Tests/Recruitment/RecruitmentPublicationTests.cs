using System.Net;
using System.Text;
using Discord;
using Discord.Net.Rest;
using Discord.WebSocket;
using DiscordBot.Services.Recruitment;
using DiscordBot.Settings;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Tests.Recruitment;

// The transport's synthetic global rate limit is process-wide, including across SDK clients.
[Collection("Recruitment transport")]
public class RecruitmentPublicationTests
{
    [Theory]
    [InlineData("50", false, true, 1)]
    [InlineData("51", false, true, 1)]
    [InlineData("52", false, false, 1)]
    [InlineData("1", true, false, 0)]
    public async Task InstalledSdkSnapshotsStaffBotsAndRoles(string role, bool bot, bool staff, int roleCount)
    {
        var transport = new PublicationTransport { Role = role, OwnerBot = bot };
        using var client = await Client(transport);
        var thread = await new DiscordRecruitmentAdapter(client, Settings()).GetThreadAsync("100", default);
        Assert.Equal(staff, thread!.OwnerIsStaff); Assert.Equal(bot, thread.OwnerIsBot);
        Assert.Equal(roleCount, thread.MemberRoleCount);
    }

    [Fact]
    public async Task InstalledSdkCreatesAndPinsReferenceWithoutChangingOtherFlagsOrMessages()
    {
        var transport = new PublicationTransport(); using var client = await Client(transport);
        var adapter = new DiscordRecruitmentAdapter(client, Settings());
        const string marker = "recruitment:reference:instance:11";
        var id = await adapter.CreateReferenceAsync("11", "71", marker, default);
        Assert.Equal("100", id);
        Assert.Equal("71", (string?)transport.CreatePayload!["applied_tags"]![0]);
        var content = transport.StarterContent;
        Assert.True(await adapter.SecureReferenceAsync("11", id, true, marker, default));
        Assert.Equal(32770, (int)transport.Thread["flags"]!);
        Assert.True((bool)transport.Thread["thread_metadata"]!["archived"]!);
        Assert.True((bool)transport.Thread["thread_metadata"]!["locked"]!);
        Assert.Equal(content, transport.StarterContent);
        Assert.DoesNotContain(transport.ChannelWrites, w => w["permission_overwrites"] is not null);
        var writes = transport.ChannelWrites.Count;
        Assert.True(await adapter.SecureReferenceAsync("11", id, true, marker, default));
        Assert.Equal(writes, transport.ChannelWrites.Count);
    }

    [Fact]
    public async Task InstalledSdkUpdatesFieldOnlyChangesAndRemovesLegacyBannerOnce()
    {
        var transport = new PublicationTransport(); using var client = await Client(transport);
        var adapter = new DiscordRecruitmentAdapter(client, Settings());
        const string marker = "recruitment:100:public";
        var embed = new EmbedBuilder().WithTitle("Recruitment").WithDescription("Listing active").AddField("Payment", "Rate detected").Build();
        var message = new RecruitmentMessage($"||{marker}||", marker, false, Embed: embed);
        var writes = 0;
        await adapter.UpsertAsync("100", "101", false, DateTimeOffset.UtcNow, message, () => writes++, default);
        Assert.Equal(1, writes);
        var patch = Assert.Single(transport.MessageWrites);
        Assert.Empty((JArray)patch["attachments"]!);
        Assert.Equal("Rate detected", (string?)patch["embeds"]![0]!["fields"]![0]!["value"]);
        await adapter.UpsertAsync("100", "101", false, DateTimeOffset.UtcNow, message, () => writes++, default);
        Assert.Equal(1, writes);
        var corrected = embed.ToEmbedBuilder().WithFields(new EmbedFieldBuilder().WithName("Payment").WithValue("No rate detected")).Build();
        await adapter.UpsertAsync("100", "101", false, DateTimeOffset.UtcNow, message with { Embed = corrected }, () => writes++, default);
        Assert.Equal(2, writes);
    }

    private static BotSettings Settings() => new() { GuildId = 1,
        Recruitment = new RecruitmentSettings { Forums = new RecruitmentForums { PaidRecruitingId = 11, PaidForHireId = 12, HobbyRecruitingId = 13, HobbyForHireId = 14 } },
        Roles = new() { Moderator = 50 } };

    private static async Task<DiscordSocketClient> Client(PublicationTransport transport)
    {
        var client = new DiscordSocketClient(new DiscordSocketConfig { RestClientProvider = RecruitmentForumTransport.Wrap(_ => transport) });
        await client.LoginAsync(TokenType.Bot, "offline-placeholder-token", validateToken: false);
        // READY normally seeds the socket REST wrapper's identity. Seed only that SDK cache from an
        // offline REST login so these payload tests never open a gateway or replace production I/O.
        using var rest = new Discord.Rest.DiscordRestClient(new Discord.Rest.DiscordRestConfig { RestClientProvider = _ => transport });
        await rest.LoginAsync(TokenType.Bot, "offline-placeholder-token", validateToken: false);
        typeof(Discord.Rest.DiscordRestClient).GetProperty("CurrentUser", System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)!.SetValue(client.Rest, rest.CurrentUser);
        return client;
    }

    private sealed class PublicationTransport : IRestClient
    {
        public string Role { get; init; } = "52";
        public bool OwnerBot { get; init; }
        public string StarterContent { get; private set; } = "Example listing";
        public JObject? CreatePayload { get; private set; }
        public List<JObject> ChannelWrites { get; } = new();
        public List<JObject> MessageWrites { get; } = new();
        public JObject Thread { get; } = JObject.Parse("""{"id":"100","type":11,"guild_id":"1","parent_id":"11","owner_id":"200","name":"Example","flags":32768,"applied_tags":[],"thread_metadata":{"archived":false,"locked":false,"auto_archive_duration":10080,"archive_timestamp":"2026-10-14T12:00:00Z"}}""");
        private readonly JObject _message = JObject.Parse("""{"id":"101","channel_id":"100","type":0,"content":"||recruitment:100:public||","author":{"id":"999","username":"offline","discriminator":"0","bot":true},"timestamp":"2026-10-14T12:00:00Z","embeds":[{"type":"rich","title":"Recruitment","description":"Listing active","fields":[{"name":"Payment","value":"Unknown","inline":false}]}],"attachments":[{"id":"900","filename":"recruitment.png","size":10,"url":"https://cdn.discordapp.com/example.png","proxy_url":"https://cdn.discordapp.com/example.png"}],"components":[]}""");
        public void Dispose() { }
        public void SetHeader(string key, string value) { }
        public void SetCancelToken(CancellationToken token) { }
        public Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken cancelToken, bool headerOnly, string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers) => Respond(method, endpoint, null);
        public Task<RestResponse> SendAsync(string method, string endpoint, string json, CancellationToken cancelToken, bool headerOnly, string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers) => Respond(method, endpoint, JObject.Parse(json));
        public Task<RestResponse> SendAsync(string method, string endpoint, IReadOnlyDictionary<string, object> multipart, CancellationToken cancelToken, bool headerOnly, string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers) => Respond(method, endpoint, JObject.Parse((string)multipart["payload_json"]));
        private Task<RestResponse> Respond(string method, string endpoint, JObject? patch)
        {
            string body;
            if (endpoint == "users/@me") body = """{"id":"999","username":"offline","discriminator":"0","bot":true}""";
            else if (endpoint == "oauth2/applications/@me") body = """{"id":"999","name":"offline","description":"","bot_public":true,"bot_require_code_grant":false,"verify_key":"00"}""";
            else if (endpoint.StartsWith("guilds/1/members/200")) body = $$"""{"user":{"id":"200","username":"member","discriminator":"0","bot":{{OwnerBot.ToString().ToLowerInvariant()}}},"roles":["{{Role}}"],"joined_at":"2020-01-01T00:00:00Z"}""";
            else if (endpoint.StartsWith("guilds/1")) body = """{"id":"1","name":"Offline","owner_id":"900","roles":[{"id":"1","name":"@everyone","permissions":"0"},{"id":"50","name":"Moderator","permissions":"0"},{"id":"51","name":"Admin","permissions":"8"},{"id":"52","name":"Artist","permissions":"0"}],"emojis":[],"features":[],"stickers":[],"afk_timeout":300,"preferred_locale":"en-US"}""";
            else if (endpoint == "channels/11") body = """{"id":"11","type":15,"guild_id":"1","name":"forum","flags":16,"available_tags":[{"id":"71","name":"Closed","moderated":false}]}""";
            else if (endpoint == "channels/11/threads" && method == "POST")
            {
                CreatePayload = patch!; StarterContent = (string)patch!["message"]!["content"]!;
                Thread["owner_id"] = "999"; Thread["applied_tags"] = patch["applied_tags"]!.DeepClone(); body = Thread.ToString();
            }
            else if (endpoint == "channels/100")
            {
                if (method == "PATCH")
                {
                    ChannelWrites.Add(patch!);
                    foreach (var property in patch!.Properties())
                        if (property.Name is "locked" or "archived") Thread["thread_metadata"]![property.Name] = property.Value.DeepClone();
                        else Thread[property.Name] = property.Value.DeepClone();
                }
                body = Thread.ToString();
            }
            else if (endpoint == "channels/100/messages/100") body = new JObject { ["id"] = "100", ["channel_id"] = "100", ["type"] = 0,
                ["content"] = StarterContent, ["timestamp"] = "2026-10-14T12:00:00Z", ["author"] = new JObject { ["id"] = Thread["owner_id"]!.ToString(), ["username"] = "owner", ["discriminator"] = "0", ["bot"] = (string?)Thread["owner_id"] == "999" || OwnerBot }, ["embeds"] = new JArray(), ["attachments"] = new JArray() }.ToString();
            else if (endpoint == "channels/100/messages/101")
            {
                if (method == "PATCH") { MessageWrites.Add(patch!); foreach (var property in patch!.Properties()) _message[property.Name] = property.Value.DeepClone(); }
                body = _message.ToString();
            }
            else throw new InvalidOperationException($"Unexpected offline route: {method} {endpoint}");
            return Task.FromResult(new RestResponse(HttpStatusCode.OK, new Dictionary<string, string>(), new MemoryStream(Encoding.UTF8.GetBytes(body))));
        }
    }
}
