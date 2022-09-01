using DiscordBot.Domain;
using ImageMagick;

namespace DiscordBot.Skin;

public class XpRankSkinModule : BaseTextSkinModule
{
    public XpRankSkinModule()
    {
        StrokeColor = MagickColors.Transparent.ToString();
        FillColor = MagickColors.Black.ToString();
        FontPointSize = 17;
    }

    public override Drawables GetDrawables(ProfileData data)
    {
        Text = $"#{data.XpRank}";
        return base.GetDrawables(data);
    }
}