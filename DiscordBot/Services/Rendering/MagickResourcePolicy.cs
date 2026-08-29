using ImageMagick;

namespace DiscordBot.Services.Rendering;

internal static class MagickResourcePolicy
{
    private static readonly object Sync = new();
    private static Limits? _configuredLimits;

    public static void Configure(ImageRenderOptions options)
    {
        var limits = Limits.From(options);

        lock (Sync)
        {
            if (_configuredLimits is { } configured)
            {
                if (configured != limits)
                {
                    throw new InvalidOperationException(
                        "ImageMagick resource limits have already been configured differently for this process.");
                }

                return;
            }

            ResourceLimits.Area = limits.Area;
            ResourceLimits.Disk = limits.Disk;
            ResourceLimits.Memory = limits.Memory;
            ResourceLimits.MaxMemoryRequest = limits.MaximumMemoryRequest;
            ResourceLimits.MaxProfileSize = limits.MaximumProfileSize;
            ResourceLimits.Width = limits.Width;
            ResourceLimits.Height = limits.Height;
            ResourceLimits.ListLength = limits.ImageListLength;
            ResourceLimits.Thread = limits.Threads;
            ResourceLimits.Time = limits.Seconds;
            _configuredLimits = limits;
        }
    }

    private sealed record Limits(
        ulong Area,
        ulong Disk,
        ulong Memory,
        ulong MaximumMemoryRequest,
        ulong MaximumProfileSize,
        ulong Width,
        ulong Height,
        ulong ImageListLength,
        ulong Threads,
        ulong Seconds)
    {
        public static Limits From(ImageRenderOptions options) => new(
            options.NativeAreaLimitPixels,
            options.NativeDiskLimitBytes,
            options.NativeMemoryLimitBytes,
            options.NativeMaximumMemoryRequestBytes,
            options.NativeMaximumProfileBytes,
            options.NativeMaximumWidth,
            options.NativeMaximumHeight,
            options.NativeMaximumImageListLength,
            options.NativeMaximumThreads,
            options.NativeMaximumSeconds);
    }
}
