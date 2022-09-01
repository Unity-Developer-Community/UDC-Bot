using DiscordBot.Domain;
using ImageMagick;

namespace DiscordBot.Skin;

public class LevelSkinModule : BaseTextSkinModule
{
    public LevelSkinModule()
    {
        StartX = 220;
        StartY = 140;
        StrokeColor = MagickColors.IndianRed.ToString();
        FillColor = MagickColors.IndianRed.ToString();
        FontPointSize = 50;
    }

    public override Drawables GetDrawables(ProfileData data)
    {
        Text = data.Level.ToString();
        return base.GetDrawables(data);
    }
}