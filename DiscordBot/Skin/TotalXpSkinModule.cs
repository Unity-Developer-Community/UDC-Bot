using System.Globalization;
using DiscordBot.Domain;
using ImageMagick;

namespace DiscordBot.Skin;

public class TotalXpSkinModule : BaseTextSkinModule
{
    public TotalXpSkinModule()
    {
        StrokeColor = MagickColors.Transparent.ToString();
        FillColor = MagickColors.Black.ToString();
        FontPointSize = 17;
    }

    public override Drawables GetDrawables(ProfileData data)
    {
        Text = data.XpTotal.ToString("N0", new CultureInfo("en-US"));
        return base.GetDrawables(data);
    }
}