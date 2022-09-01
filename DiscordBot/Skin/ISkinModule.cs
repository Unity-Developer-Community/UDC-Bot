using DiscordBot.Domain;
using ImageMagick;

namespace DiscordBot.Skin;

public interface ISkinModule
{
    string Type { get; set; }

    Drawables GetDrawables(ProfileData data);
}