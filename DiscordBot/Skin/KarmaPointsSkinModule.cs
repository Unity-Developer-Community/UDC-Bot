using DiscordBot.Domain;
using ImageMagick;
using ImageMagick.Drawing;

namespace DiscordBot.Skin;

public class KarmaPointsSkinModule : BaseTextSkinModule
{
    public KarmaPointsSkinModule()
    {
        StartX = 535;
        StartY = 130;
        StrokeColor = MagickColors.Transparent.ToString();
        FillColor = MagickColors.Black.ToString();
        FontPointSize = 17;
    }

    public override IDrawables<byte> GetDrawables(ProfileData data)
    {
        Text = $"{data.Karma}";
        return base.GetDrawables(data);
    }
}