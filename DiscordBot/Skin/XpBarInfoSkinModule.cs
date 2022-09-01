using DiscordBot.Domain;
using ImageMagick;

namespace DiscordBot.Skin;

public class XpBarInfoSkinModule : BaseTextSkinModule
{
    public XpBarInfoSkinModule()
    {
        StrokeWidth = 1;
        FillColor = MagickColors.Black.ToString();
        StrokeColor = MagickColors.Transparent.ToString();
        FontPointSize = 17;
    }

    public override Drawables GetDrawables(ProfileData data)
    {
        Text = $"{data.XpShown:#,##0} / {data.MaxXpShown:N0} ({Math.Floor(data.XpPercentage * 100):0}%)";
        return base.GetDrawables(data);
    }
}