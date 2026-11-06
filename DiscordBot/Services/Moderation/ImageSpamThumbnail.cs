using ImageMagick;

namespace DiscordBot.Services.Moderation;

/// <summary>
/// Turns one untrusted spam upload into a small JPEG for the report, so the upload stays well under
/// Discord's limit. Only plain raster formats reach ImageMagick, and only images of a sane size.
/// </summary>
public static class ImageSpamThumbnail
{
    /// <summary>Longest side of the copy. The images are almost always scams: a bigger copy adds nothing.</summary>
    public const uint MaxSide = 720;
    /// <summary>Largest attachment worth downloading.</summary>
    public const long MaxSourceBytes = 25 * 1024 * 1024;
    /// <summary>Largest decoded image accepted, checked from the header before decoding.</summary>
    public const long MaxPixels = 40_000_000;
    private const uint JpegQuality = 80;

    /// <summary>
    /// Picks one attachment at random among those of at most <see cref="MaxSourceBytes"/>,
    /// or <c>null</c> when none fit.
    /// </summary>
    public static int? PickIndex(IReadOnlyList<long> sizes, Random random)
    {
        var candidates = Enumerable.Range(0, sizes.Count).Where(i => sizes[i] <= MaxSourceBytes).ToList();
        return candidates.Count == 0 ? null : candidates[random.Next(candidates.Count)];
    }

    /// <summary>
    /// Returns the image as a JPEG of at most <see cref="MaxSide"/> pixels on its longest side
    /// (never enlarged), flattened onto white and without metadata. Only the first frame of an
    /// animation is kept. Returns <c>null</c> for anything that isn't a PNG, JPEG, GIF, WebP or BMP,
    /// for images over <see cref="MaxPixels"/>, and for anything that fails to decode.
    /// </summary>
    public static byte[]? TryCreate(byte[] source)
    {
        // Decide the format from the bytes, never from the file name or content type, so SVG, MVG and
        // other delegate formats never reach ImageMagick, not even for a header read.
        if (DetectFormat(source) is not { } format)
            return null;

        try
        {
            var info = new MagickImageInfo(source, new MagickReadSettings { Format = format });
            if (info.Width == 0 || info.Height == 0 || (long)info.Width * info.Height > MaxPixels)
                return null;

            using var image = new MagickImage(source, new MagickReadSettings { Format = format, FrameIndex = 0, FrameCount = 1 });
            image.AutoOrient();
            image.Resize(new MagickGeometry(MaxSide, MaxSide) { Greater = true });
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
            image.Alpha(AlphaOption.Off);
            image.Strip();
            image.Quality = JpegQuality;
            return image.ToByteArray(MagickFormat.Jpeg);
        }
        catch (MagickException)
        {
            return null;
        }
    }

    private static MagickFormat? DetectFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return MagickFormat.Png;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return MagickFormat.Jpeg;
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
            return MagickFormat.Gif;
        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
            return MagickFormat.WebP;
        if (bytes.StartsWith("BM"u8))
            return MagickFormat.Bmp;
        return null;
    }
}
