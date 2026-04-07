using DiscordBot.Domain;
using ImageMagick;
using ImageMagick.Drawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DiscordBot.Skin;

public abstract class BaseTextSkinModule : ISkinModule
{
    public BaseTextSkinModule()
    {
        StrokeWidth = 1;
        Font = "Consolas";
        TextAntiAlias = true;
        StrokeAntiAlias = true;
        TextKerning = 0;
    }

    public double StartX { get; set; }
    public double StartY { get; set; }

    public bool StrokeAntiAlias { get; set; }
    public bool TextAntiAlias { get; set; }
    public string StrokeColor { get; set; } = string.Empty;
    public double StrokeWidth { get; set; }
    public string FillColor { get; set; } = string.Empty;
    public string Font { get; set; }
    public double FontPointSize { get; set; }
    public string Text { get; set; } = string.Empty;
    public double TextKerning { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public TextAlignment TextAlignment { get; set; }

    public virtual string Type { get; set; } = string.Empty;

    public virtual IDrawables<byte> GetDrawables(ProfileData data)
    {
        var position = new PointD(StartX, StartY);

        var drawables = new Drawables()
            .FontPointSize(FontPointSize)
            .Font(Font)
            .StrokeColor(new MagickColor(StrokeColor))
            .StrokeWidth(StrokeWidth)
            .FillColor(new MagickColor(FillColor))
            .TextAlignment(TextAlignment)
            .TextKerning(TextKerning)
            .Text(position.X, position.Y, Text);

        if (StrokeAntiAlias) drawables.EnableStrokeAntialias(); else drawables.DisableStrokeAntialias();
        if (TextAntiAlias) drawables.EnableTextAntialias(); else drawables.DisableTextAntialias();

        return drawables;
    }
}