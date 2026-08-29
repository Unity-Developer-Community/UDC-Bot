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
    public int MaximumAvatarBytes { get; init; } = 2 * 1_024 * 1_024;
    public int MaximumAvatarWidth { get; init; } = 2_048;
    public int MaximumAvatarHeight { get; init; } = 2_048;
    public TimeSpan AvatarDownloadTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public int MaximumOutputWidth { get; init; } = 1_024;
    public int MaximumOutputHeight { get; init; } = 1_024;
    public int MaximumOutputBytes { get; init; } = 2 * 1_024 * 1_024;

    // ResourceLimits are process-wide. These defaults leave headroom for the managed bot in its 512 MiB container.
    public ulong NativeAreaLimitPixels { get; init; } = 16 * 1_024 * 1_024;
    public ulong NativeDiskLimitBytes { get; init; } = 64 * 1_024 * 1_024;
    public ulong NativeMemoryLimitBytes { get; init; } = 128 * 1_024 * 1_024;
    public ulong NativeMaximumMemoryRequestBytes { get; init; } = 64 * 1_024 * 1_024;
    public ulong NativeMaximumProfileBytes { get; init; } = 1 * 1_024 * 1_024;
    public ulong NativeMaximumWidth { get; init; } = 4_096;
    public ulong NativeMaximumHeight { get; init; } = 4_096;
    public ulong NativeMaximumImageListLength { get; init; } = 16;
    public ulong NativeMaximumThreads { get; init; } = 2;
    public ulong NativeMaximumSeconds { get; init; } = 15;

    public void Validate()
    {
        RequirePositive(MaximumAvatarBytes, nameof(MaximumAvatarBytes));
        RequirePositive(MaximumAvatarWidth, nameof(MaximumAvatarWidth));
        RequirePositive(MaximumAvatarHeight, nameof(MaximumAvatarHeight));
        RequirePositive(MaximumOutputWidth, nameof(MaximumOutputWidth));
        RequirePositive(MaximumOutputHeight, nameof(MaximumOutputHeight));
        RequirePositive(MaximumOutputBytes, nameof(MaximumOutputBytes));

        if (AvatarDownloadTimeout <= TimeSpan.Zero || AvatarDownloadTimeout == Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(AvatarDownloadTimeout),
                "The avatar download timeout must be finite and greater than zero.");

        RequirePositive(NativeAreaLimitPixels, nameof(NativeAreaLimitPixels));
        RequirePositive(NativeDiskLimitBytes, nameof(NativeDiskLimitBytes));
        RequirePositive(NativeMemoryLimitBytes, nameof(NativeMemoryLimitBytes));
        RequirePositive(NativeMaximumMemoryRequestBytes, nameof(NativeMaximumMemoryRequestBytes));
        RequirePositive(NativeMaximumProfileBytes, nameof(NativeMaximumProfileBytes));
        RequirePositive(NativeMaximumWidth, nameof(NativeMaximumWidth));
        RequirePositive(NativeMaximumHeight, nameof(NativeMaximumHeight));
        RequirePositive(NativeMaximumImageListLength, nameof(NativeMaximumImageListLength));
        RequirePositive(NativeMaximumThreads, nameof(NativeMaximumThreads));
        RequirePositive(NativeMaximumSeconds, nameof(NativeMaximumSeconds));

        if ((ulong)Math.Max(MaximumAvatarWidth, MaximumOutputWidth) > NativeMaximumWidth ||
            (ulong)Math.Max(MaximumAvatarHeight, MaximumOutputHeight) > NativeMaximumHeight)
        {
            throw new ArgumentException("Renderer dimensions cannot exceed the process-wide native limits.");
        }
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(name, "The value must be greater than zero.");
    }

    private static void RequirePositive(ulong value, string name)
    {
        if (value == 0)
            throw new ArgumentOutOfRangeException(name, "The value must be greater than zero.");
    }
}
