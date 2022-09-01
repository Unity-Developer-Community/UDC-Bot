using DiscordBot.Domain;
using ImageMagick;

namespace DiscordBot.Skin;

public class UsernameSkinModule : BaseTextSkinModule
{
    public UsernameSkinModule()
    {
        FontPointSize = 34;
        Font = "Consolas";
        StrokeColor = MagickColors.BlueViolet.ToString();
        FillColor = MagickColors.DeepSkyBlue.ToString();
    }

    public override Drawables GetDrawables(ProfileData data)
    {
        Text = $"{data.Nickname ?? data.Username}";
        return base.GetDrawables(data);
    }
}