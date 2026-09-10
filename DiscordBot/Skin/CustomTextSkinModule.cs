using System.Text.RegularExpressions;
using DiscordBot.Domain;
using ImageMagick;
using ImageMagick.Drawing;

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

    public override IDrawables<byte> GetDrawables(ProfileData data)
    {
        var textPosition = new PointD(StartX, StartY);

        // Reflection to convert stuff like {Level} to data.Level
        var reg = new Regex(@"(?<=\{)(.*?)(?=\})");
        var mc = reg.Matches(Text);
        foreach (var match in mc)
        {
            var prop = typeof(ProfileData).GetProperty(match.ToString()!);
            if (prop == null) continue;
            var value = (dynamic?)prop.GetValue(data, null);
            Text = Text.Replace("{" + match + "}", value?.ToString() ?? string.Empty);
        }
        /* ALL properties of ProfileData.cs can be used!
         * Like {Level} for ProfileData.Level
         * Or {Nickname} for ProfileData.Nickname
         */

        var drawables = new Drawables()
            .FontPointSize(FontPointSize)
            .Font(Font)
            .StrokeColor(new MagickColor(StrokeColor))
            .StrokeWidth(StrokeWidth)
            .FillColor(new MagickColor(FillColor))
            .TextAlignment(TextAlignment)
            .TextKerning(TextKerning)
            .Text(textPosition.X, textPosition.Y, $"{Text ?? Text}");

        if (StrokeAntiAlias) drawables.EnableStrokeAntialias(); else drawables.DisableStrokeAntialias();
        if (TextAntiAlias) drawables.EnableTextAntialias(); else drawables.DisableTextAntialias();

        return drawables;
    }
}