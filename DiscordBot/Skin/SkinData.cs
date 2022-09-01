namespace DiscordBot.Skin;

public class SkinData
{
    public SkinData()
    {
        Layers = new List<SkinLayer>();
    }

    public string Name { get; set; }
    public string Codename { get; set; }
    public string Description { get; set; }
    public int AvatarSize { get; set; }
    public string Background { get; set; }
    public List<SkinLayer> Layers { get; set; }
}