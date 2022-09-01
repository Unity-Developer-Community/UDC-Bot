namespace DiscordBot.Skin;

public class SkinLayer
{
    public SkinLayer()
    {
        Modules = new List<ISkinModule>();
    }

    public string Image { get; set; }
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public List<ISkinModule> Modules { get; set; }
}