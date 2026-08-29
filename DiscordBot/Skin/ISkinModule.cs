using DiscordBot.Services.Rendering;
using ImageMagick;

namespace DiscordBot.Skin;

public interface ISkinModule
{
    string Type { get; set; }

    Drawables GetDrawables(ProfileCardRenderRequest data);
}
