using DiscordBot.Services.Rendering;
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
    public string DefaultColor { get; set; }

    public string Type { get; set; }

    public IDrawables<byte> GetDrawables(ProfileCardRenderRequest data)
    {
        var color = new MagickColor(data.AvatarSampleColor.R, data.AvatarSampleColor.G, data.AvatarSampleColor.B);

        if (WhiteFix && data.AvatarSampleColor.R + data.AvatarSampleColor.G + data.AvatarSampleColor.B > 650)
            color = new MagickColor(DefaultColor);

        return new Drawables()
            .FillColor(color)
            .Rectangle(StartX, StartY, StartX + Width, StartY + Height);
    }
}
