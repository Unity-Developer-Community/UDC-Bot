using DiscordBot.Services.Rendering;
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

    public override Drawables GetDrawables(ProfileCardRenderRequest data)
    {
        Text = $"#{data.XpRank}";
        return base.GetDrawables(data);
    }
}
