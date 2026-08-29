using System.Text.RegularExpressions;
using DiscordBot.Services.Rendering;
using ImageMagick;

namespace DiscordBot.Skin;

public class CustomTextSkinModule : BaseTextSkinModule
{
    public CustomTextSkinModule()
    {
        StrokeWidth = 1;
        FillColor = MagickColors.Black.ToString();
        StrokeColor = MagickColors.Transparent.ToString();
        Font = "Consolas";
        FontPointSize = 15;
    }

    public override Drawables GetDrawables(ProfileCardRenderRequest data)
    {
        var textPosition = new PointD(StartX, StartY);

        // Reflection to convert stuff like {Level} to data.Level
        var reg = new Regex(@"(?<=\{)(.*?)(?=\})");
        var text = Text;
        var mc = reg.Matches(text);
        foreach (var match in mc)
        {
            var prop = typeof(ProfileCardRenderRequest).GetProperty(match.ToString());
            if (prop == null) continue;
            var value = (dynamic)prop.GetValue(data, null);
            text = text.Replace("{" + match + "}", value.ToString());
        }
        /* All properties of ProfileCardRenderRequest can be used.
         * For example, {Level} or {Nickname}.
         */

        return new Drawables()
            .FontPointSize(FontPointSize)
            .Font(Font)
            .StrokeColor(new MagickColor(StrokeColor))
            .StrokeWidth(StrokeWidth)
            .StrokeAntialias(StrokeAntiAlias)
            .FillColor(new MagickColor(FillColor))
            .TextAlignment(TextAlignment)
            .TextAntialias(TextAntiAlias)
            .TextKerning(TextKerning)
            .Text(textPosition.X, textPosition.Y, text);
    }
}
