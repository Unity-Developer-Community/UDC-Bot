using System.Text;
using DiscordBot.Services.Moderation;
using ImageMagick;

namespace DiscordBot.Tests.Services;

public class ImageSpamThumbnailTests
{
    private static byte[] Image(uint width, uint height, MagickFormat format, string color = "red")
    {
        using var image = new MagickImage(new MagickColor(color), width, height);
        return image.ToByteArray(format);
    }

    private static MagickImage Decode(byte[] jpeg)
    {
        var image = new MagickImage(jpeg);
        Assert.Equal(MagickFormat.Jpeg, image.Format);
        return image;
    }

    [Fact]
    public void LargePng_ShrunkToJpegOnLongestSide()
    {
        var result = ImageSpamThumbnail.TryCreate(Image(3000, 2000, MagickFormat.Png));

        using var image = Decode(Assert.IsType<byte[]>(result));
        Assert.Equal(720u, image.Width);
        Assert.Equal(480u, image.Height);
    }

    [Fact]
    public void TallImage_ShrunkOnHeight()
    {
        var result = ImageSpamThumbnail.TryCreate(Image(1000, 2000, MagickFormat.Jpeg));

        using var image = Decode(Assert.IsType<byte[]>(result));
        Assert.Equal(360u, image.Width);
        Assert.Equal(720u, image.Height);
    }

    [Fact]
    public void SmallImage_NotEnlarged()
    {
        var result = ImageSpamThumbnail.TryCreate(Image(200, 200, MagickFormat.Gif));

        using var image = Decode(Assert.IsType<byte[]>(result));
        Assert.Equal(200u, image.Width);
        Assert.Equal(200u, image.Height);
    }

    [Fact]
    public void TransparentPng_FlattenedOntoWhite()
    {
        var result = ImageSpamThumbnail.TryCreate(Image(100, 100, MagickFormat.Png, "transparent"));

        using var image = Decode(Assert.IsType<byte[]>(result));
        Assert.False(image.HasAlpha);
        using var pixels = image.GetPixels();
        var pixel = pixels.GetPixel(50, 50).ToColor()!;
        Assert.True(pixel.R > 250 && pixel.G > 250 && pixel.B > 250, $"Expected white, got {pixel}");
    }

    [Fact]
    public void Svg_Rejected()
    {
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\"/></svg>");
        Assert.Null(ImageSpamThumbnail.TryCreate(svg));
    }

    [Fact]
    public void GarbageAfterPngSignature_Rejected()
    {
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5];
        Assert.Null(ImageSpamThumbnail.TryCreate(bytes));
    }

    [Fact]
    public void HeaderOverPixelLimit_Rejected()
    {
        // A valid PNG of 1x1, with the header patched to claim 8000x8000 (64 MP).
        var png = Image(1, 1, MagickFormat.Png);
        WriteBigEndian(png, 16, 8000);
        WriteBigEndian(png, 20, 8000);
        RefreshIhdrCrc(png);

        Assert.Null(ImageSpamThumbnail.TryCreate(png));
    }

    [Fact]
    public void PickIndex_SkipsAttachmentsOverTheCap()
    {
        long[] sizes = [ImageSpamThumbnail.MaxSourceBytes + 1, 100, ImageSpamThumbnail.MaxSourceBytes + 1];
        for (var seed = 0; seed < 20; seed++)
            Assert.Equal(1, ImageSpamThumbnail.PickIndex(sizes, new Random(seed)));
    }

    [Fact]
    public void PickIndex_NullWhenNoneFit()
    {
        Assert.Null(ImageSpamThumbnail.PickIndex([ImageSpamThumbnail.MaxSourceBytes + 1], new Random(0)));
        Assert.Null(ImageSpamThumbnail.PickIndex([], new Random(0)));
    }

    [Fact]
    public void PickIndex_CanPickAnyFittingAttachment()
    {
        long[] sizes = [1, 2, 3, 4];
        var picked = Enumerable.Range(0, 200).Select(seed => ImageSpamThumbnail.PickIndex(sizes, new Random(seed))).ToHashSet();
        Assert.Equal(new HashSet<int?> { 0, 1, 2, 3 }, picked);
    }

    private static void WriteBigEndian(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    // The IHDR chunk starts at byte 8: length (4), type (4), data (13), CRC (4) over type and data.
    private static void RefreshIhdrCrc(byte[] png)
    {
        uint crc = 0xFFFFFFFF;
        for (var i = 12; i < 12 + 4 + 13; i++)
        {
            crc ^= png[i];
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        WriteBigEndian(png, 29, ~crc);
    }
}
