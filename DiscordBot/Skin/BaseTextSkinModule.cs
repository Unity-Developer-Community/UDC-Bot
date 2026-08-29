using DiscordBot.Services.Rendering;
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

    public virtual IDrawables<byte> GetDrawables(ProfileCardRenderRequest data)
    {
        var position = new PointD(StartX, StartY);

        IDrawables<byte> drawables = new Drawables()
            .FontPointSize(FontPointSize)
            .Font(Font)
            .StrokeColor(new MagickColor(StrokeColor))
            .StrokeWidth(StrokeWidth);

        drawables = StrokeAntiAlias
            ? drawables.EnableStrokeAntialias()
            : drawables.DisableStrokeAntialias();

        drawables = drawables.FillColor(new MagickColor(FillColor));
        drawables = TextAntiAlias
            ? drawables.EnableTextAntialias()
            : drawables.DisableTextAntialias();

        return drawables
            .TextAlignment(TextAlignment)
            .TextKerning(TextKerning)
            .Text(position.X, position.Y, Text);
    }
}
