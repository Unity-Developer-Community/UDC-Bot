using System.Buffers;
using System.IO;
using System.Net.Http;

namespace DiscordBot.Services.Rendering;

public sealed class AvatarDownloader : IAvatarDownloader, IDisposable
{
    private const int BufferSize = 81_920;

    private readonly HttpClient _httpClient;
    private readonly ImageRenderOptions _options;

    public AvatarDownloader(HttpClient httpClient, ImageRenderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public async Task<byte[]> DownloadAsync(Uri avatarUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(avatarUri);
        if (!avatarUri.IsAbsoluteUri || avatarUri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Avatar URLs must use absolute HTTPS addresses.", nameof(avatarUri));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.AvatarDownloadTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, avatarUri);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength > _options.MaximumAvatarBytes)
            {
                throw new InvalidDataException(
                    $"Avatar response is {contentLength} bytes; the limit is {_options.MaximumAvatarBytes} bytes.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var destination = new MemoryStream(contentLength is > 0 ? checked((int)contentLength.Value) : 0);
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

            try
            {
                var total = 0L;
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), timeout.Token);
                    if (read == 0)
                        break;

                    total += read;
                    if (total > _options.MaximumAvatarBytes)
                    {
                        throw new InvalidDataException(
                            $"Avatar response exceeded the {_options.MaximumAvatarBytes}-byte limit.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return destination.ToArray();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Avatar download exceeded the {_options.AvatarDownloadTimeout.TotalSeconds:g}-second timeout.",
                exception);
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
