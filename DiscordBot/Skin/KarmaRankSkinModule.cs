using DiscordBot.Domain;
using ImageMagick;
using ImageMagick.Drawing;

namespace DiscordBot.Skin;

public class KarmaRankSkinModule : BaseTextSkinModule
{
    public KarmaRankSkinModule()
    {
        StartX = 535;
        StartY = 153;
        StrokeColor = MagickColors.Transparent.ToString();
        FillColor = MagickColors.Black.ToString();
        FontPointSize = 17;
    }

    public override IDrawables<byte> GetDrawables(ProfileData data)
    {
        Text = $"#{data.KarmaRank}";
        return base.GetDrawables(data);
    }
}