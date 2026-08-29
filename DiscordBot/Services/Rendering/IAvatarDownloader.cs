namespace DiscordBot.Services.Rendering;

public interface IAvatarDownloader
{
    Task<byte[]> DownloadAsync(Uri avatarUri, CancellationToken cancellationToken = default);
}
