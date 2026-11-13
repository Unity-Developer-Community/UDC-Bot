using System.Net;
using WebClient = DiscordBot.Utils.WebClient;
using DiscordBot.Utils;

namespace DiscordBot.Tests.Utils;

public class WebClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (WebClient client, StubHandler handler) Create(HttpStatusCode status, string body = "hello")
    {
        var handler = new StubHandler(status, body);
        return (new WebClient(new StubFactory(handler)), handler);
    }

    [Fact]
    public async Task TryGetContent_Success_ReturnsTheBody_AndSendsTheUserAgent()
    {
        var (client, handler) = Create(HttpStatusCode.OK);

        var content = await client.TryGetContent("https://unity.com/blog/x");

        Assert.Equal("hello", content);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(WebClient.UserAgent, request.Headers.UserAgent.ToString());
        Assert.StartsWith("UDC-Bot/", request.Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TryGetContent_FailedStatus_ReturnsNull(HttpStatusCode status)
    {
        var (client, _) = Create(status, "<html>error</html>");

        Assert.Null(await client.TryGetContent("https://unity.com/blog/x"));
    }

    [Fact]
    public async Task GetContent_FailedStatus_StillReturnsTheBody_AndSendsTheUserAgent()
    {
        var (client, handler) = Create(HttpStatusCode.NotFound, "not found page");

        Assert.Equal("not found page", await client.GetContent("https://unity.com/blog/x"));
        Assert.Equal(WebClient.UserAgent, Assert.Single(handler.Requests).Headers.UserAgent.ToString());
    }
}
