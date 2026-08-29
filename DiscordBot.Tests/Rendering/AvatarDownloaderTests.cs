using System.Net;
using System.Net.Http;
using DiscordBot.Services.Rendering;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Rendering;

[TestClass]
public sealed class AvatarDownloaderTests
{
    private static readonly Uri AvatarUri = new("https://cdn.example.test/avatar.png");

    [TestMethod]
    public async Task DownloadAsync_ValidResponse_ReturnsBytes()
    {
        var expected = new byte[] { 1, 2, 3, 4 };
        using var downloader = CreateDownloader(
            (_, _) => Task.FromResult(Response(new ByteArrayContent(expected))));

        var actual = await downloader.DownloadAsync(AvatarUri);

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task DownloadAsync_ContentLengthOverLimit_RejectsResponse()
    {
        var content = new ByteArrayContent(new byte[] { 1 });
        content.Headers.ContentLength = 65;
        using var downloader = CreateDownloader(
            (_, _) => Task.FromResult(Response(content)),
            new ImageRenderOptions(Path.GetTempPath()) { MaximumAvatarBytes = 64 });

        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(AvatarUri));
    }

    [TestMethod]
    public async Task DownloadAsync_ChunkedResponseOverLimit_StopsReading()
    {
        using var downloader = CreateDownloader(
            (_, _) => Task.FromResult(Response(new UnknownLengthContent(new byte[65]))),
            new ImageRenderOptions(Path.GetTempPath()) { MaximumAvatarBytes = 64 });

        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(AvatarUri));
    }

    [TestMethod]
    public async Task DownloadAsync_SlowResponse_TimesOut()
    {
        using var downloader = CreateDownloader(
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Response(new ByteArrayContent(Array.Empty<byte>()));
            },
            new ImageRenderOptions(Path.GetTempPath())
            {
                AvatarDownloadTimeout = TimeSpan.FromMilliseconds(25)
            });

        await Assert.ThrowsAsync<TimeoutException>(() => downloader.DownloadAsync(AvatarUri));
    }

    [TestMethod]
    public async Task DownloadAsync_NonHttpsUrl_IsRejectedBeforeRequest()
    {
        var requestCount = 0;
        using var downloader = CreateDownloader((_, _) =>
        {
            requestCount++;
            return Task.FromResult(Response(new ByteArrayContent(Array.Empty<byte>())));
        });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadAsync(new Uri("http://cdn.example.test/avatar.png")));
        Assert.AreEqual(0, requestCount);
    }

    private static AvatarDownloader CreateDownloader(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory,
        ImageRenderOptions? options = null)
    {
        var client = new HttpClient(new StubHttpMessageHandler(responseFactory))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        return new AvatarDownloader(client, options ?? new ImageRenderOptions(Path.GetTempPath()));
    }

    private static HttpResponseMessage Response(HttpContent content) => new(HttpStatusCode.OK)
    {
        Content = content
    };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
