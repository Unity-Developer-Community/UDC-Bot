namespace DiscordBot.Skin;

public class SkinData
{
    public SkinData()
    {
        Layers = new List<SkinLayer>();
    }

    public string Name { get; set; } = string.Empty;
    public string Codename { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int AvatarSize { get; set; }
    public string Background { get; set; } = string.Empty;
    public List<SkinLayer> Layers { get; set; }
}