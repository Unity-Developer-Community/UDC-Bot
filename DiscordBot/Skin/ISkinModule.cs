using DiscordBot.Services.Rendering;
using ImageMagick;
using ImageMagick.Drawing;

namespace DiscordBot.Skin;

public interface ISkinModule
{
    string Type { get; set; }

    IDrawables<byte> GetDrawables(ProfileCardRenderRequest data);
}
