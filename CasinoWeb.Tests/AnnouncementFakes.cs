using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using DiscordBot.Domain;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Settings;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace CasinoWeb.Tests;

/// <summary>One post to the fake webhook: the JSON message and, when the post was multipart, the attached files.</summary>
public sealed record WebhookPost(Uri Uri, string ContentType, JsonElement Payload, IReadOnlyDictionary<string, WebhookFile> Files);

public sealed record WebhookFile(string FileName, string ContentType, byte[] Bytes);

/// <summary>Plays the Discord webhook: keeps what was posted and answers as scripted.</summary>
public sealed class FakeWebhookHandler : HttpMessageHandler
{
    private readonly Channel<WebhookPost> _posts = Channel.CreateUnbounded<WebhookPost>();
    private int _postCount;

    public HttpStatusCode Status { get; set; } = HttpStatusCode.NoContent;

    /// <summary>Fails like an unreachable host.</summary>
    public bool Unreachable { get; set; }

    /// <summary>Never answers, until the client gives up.</summary>
    public bool Hang { get; set; }

    public int PostCount => Volatile.Read(ref _postCount);

    public async Task<WebhookPost> NextPostAsync() => await _posts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var contentType = request.Content!.Headers.ContentType!;
        var files = new Dictionary<string, WebhookFile>();
        JsonElement payload = default;
        if (contentType.MediaType == "multipart/form-data")
        {
            var boundary = HeaderUtilities.RemoveQuotes(contentType.Parameters.Single(p => p.Name == "boundary").Value).Value!;
            var reader = new MultipartReader(boundary, await request.Content.ReadAsStreamAsync(cancellationToken));
            while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
            {
                var disposition = ContentDispositionHeaderValue.Parse(section.ContentDisposition);
                using var bytes = new MemoryStream();
                await section.Body.CopyToAsync(bytes, cancellationToken);
                var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value!;
                if (name == "payload_json")
                    payload = JsonDocument.Parse(bytes.ToArray()).RootElement.Clone();
                else
                    files[name] = new WebhookFile(HeaderUtilities.RemoveQuotes(disposition.FileName).Value!, section.ContentType!, bytes.ToArray());
            }
        }
        else
        {
            payload = JsonDocument.Parse(await request.Content.ReadAsByteArrayAsync(cancellationToken)).RootElement.Clone();
        }

        Interlocked.Increment(ref _postCount);
        _posts.Writer.TryWrite(new WebhookPost(request.RequestUri!, contentType.ToString(), payload, files));
        if (Hang)
            await Task.Delay(Timeout.Infinite, cancellationToken);
        if (Unreachable)
            throw new HttpRequestException("No such host is known.");
        return new HttpResponseMessage(Status);
    }
}

/// <summary>Draws a fixed picture, or fails, and counts the draws.</summary>
public sealed class FakeSlotsRenderer() : SlotsRenderer(new BotSettings())
{
    public static readonly byte[] Picture = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private int _renders;

    public bool Fail { get; set; }
    public int Renders => Volatile.Read(ref _renders);

    public override Task<MemoryStream> RenderStaticAsync(SlotResult result)
    {
        Interlocked.Increment(ref _renders);
        return Fail
            ? Task.FromException<MemoryStream>(new FileNotFoundException("slots/cherry.png is missing"))
            : Task.FromResult(new MemoryStream(Picture));
    }

    public override Task<MemoryStream> RenderSpinAsync(SlotResult result) => throw new NotSupportedException();
}

/// <summary>Keeps every log line of every category, so a test can read what the app said and check what it never said.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<(string Category, LogLevel Level, string Message)> _lines = new();

    public IReadOnlyList<(string Category, LogLevel Level, string Message)> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new Capture(categoryName, _lines);

    public void Dispose() { }

    /// <summary>The first line matching, once it has been logged.</summary>
    public async Task<string> WaitForAsync(Func<LogLevel, string, bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (_lines.FirstOrDefault(l => match(l.Level, l.Message)) is { Message: not null } found)
                return found.Message;
            await Task.Delay(20);
        }

        throw new TimeoutException("The expected log line never appeared. Lines: " + string.Join(" | ", _lines.Select(l => $"[{l.Level}] {l.Message}")));
    }

    private sealed class Capture(string category, ConcurrentQueue<(string, LogLevel, string)> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue((category, logLevel, formatter(state, exception) + (exception is null ? "" : " " + exception)));
    }
}
