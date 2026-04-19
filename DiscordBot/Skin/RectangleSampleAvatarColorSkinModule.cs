using DiscordBot.Domain;
using ImageMagick;
using ImageMagick.Drawing;

namespace DiscordBot.Skin;

/// <summary>
///     Fill the background with the color based on the pfp
/// </summary>
public class RectangleSampleAvatarColorSkinModule : ISkinModule
{
    public int StartX { get; set; }
    public int StartY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool WhiteFix { get; set; }
    public string DefaultColor { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public IDrawables<byte> GetDrawables(ProfileData data)
    {
        var color = DetermineColor(data.Picture);

        return new Drawables()
            .FillColor(color)
            .Rectangle(StartX, StartY, StartX + Width, StartY + Height);
    }

    private MagickColor DetermineColor(MagickImage dataPicture)
    {
        var copy = new MagickImage(dataPicture);
        copy.Resize(1, 1);
        var pixels = copy.GetPixels();
        var pixelColor = pixels?[0, 0]?.ToColor();
        var color = pixelColor != null
            ? new MagickColor(pixelColor.R, pixelColor.G, pixelColor.B)
            : new MagickColor(DefaultColor);

        if (WhiteFix && color.R + color.G + color.B > 650)
            color = new MagickColor(DefaultColor);

        return color;
    }
}