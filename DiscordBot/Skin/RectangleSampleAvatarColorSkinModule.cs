using DiscordBot.Domain;
using ImageMagick;

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
    public string DefaultColor { get; set; }

    public string Type { get; set; }

    public Drawables GetDrawables(ProfileData data)
    {
        var color = DetermineColor(data.Picture);

        return new Drawables()
            .FillColor(color)
            .Rectangle(StartX, StartY, StartX + Width, StartY + Height);
    }

    private MagickColor DetermineColor(MagickImage dataPicture)
    {
        //basically we let magick to choose what the main color by resizing to 1x1
        var copy = new MagickImage(dataPicture);
        copy.Resize(1, 1);
        var color = copy.GetPixels()[0, 0].ToColor();

        if (WhiteFix && color.R + color.G + color.B > 650)
            color = new MagickColor(DefaultColor);

        return color;
    }
}