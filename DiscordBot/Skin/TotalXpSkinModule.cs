using System.Globalization;
using DiscordBot.Services.Rendering;
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

    public override Drawables GetDrawables(ProfileCardRenderRequest data)
    {
        Text = data.XpTotal.ToString("N0", new CultureInfo("en-US"));
        return base.GetDrawables(data);
    }
}
