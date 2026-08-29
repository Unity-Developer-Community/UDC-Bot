using System.IO;

namespace DiscordBot.Services.Rendering;

public sealed class ImageRenderOptions
{
    public ImageRenderOptions(string assetsRootPath)
    {
        if (string.IsNullOrWhiteSpace(assetsRootPath))
            throw new ArgumentException("An assets root path is required.", nameof(assetsRootPath));

        AssetsRootPath = Path.GetFullPath(assetsRootPath);
    }

    public string AssetsRootPath { get; }
    public string SkinFileName { get; init; } = "skin.json";
    public string DefaultAvatarFileName { get; init; } = "default.png";
    public int MaximumOutputWidth { get; init; } = 1_024;
    public int MaximumOutputHeight { get; init; } = 1_024;
    public int MaximumOutputBytes { get; init; } = 2 * 1_024 * 1_024;
}
