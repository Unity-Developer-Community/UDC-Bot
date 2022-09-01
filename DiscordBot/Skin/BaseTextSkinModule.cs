using DiscordBot.Domain;
using ImageMagick;
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
    public string StrokeColor { get; set; }
    public double StrokeWidth { get; set; }
    public string FillColor { get; set; }
    public string Font { get; set; }
    public double FontPointSize { get; set; }
    public string Text { get; set; }
    public double TextKerning { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public TextAlignment TextAlignment { get; set; }

    public virtual string Type { get; set; }

    public virtual Drawables GetDrawables(ProfileData data)
    {
        var position = new PointD(StartX, StartY);

        return new Drawables()
            .FontPointSize(FontPointSize)
            .Font(Font)
            .StrokeColor(new MagickColor(StrokeColor))
            .StrokeWidth(StrokeWidth)
            .StrokeAntialias(StrokeAntiAlias)
            .FillColor(new MagickColor(FillColor))
            .TextAntialias(TextAntiAlias)
            .TextAlignment(TextAlignment)
            .TextKerning(TextKerning)
            .Text(position.X, position.Y, Text);
    }
}