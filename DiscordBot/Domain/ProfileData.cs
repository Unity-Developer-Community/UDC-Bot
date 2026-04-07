using ImageMagick;

namespace DiscordBot.Domain;

public class ProfileData
{
    public ulong UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public long XpTotal { get; set; }
    public long XpRank { get; set; }
    public long KarmaRank { get; set; }
    public int Karma { get; set; }
    public int Level { get; set; }
    public double XpLow { get; set; }
    public double XpHigh { get; set; }
    public int XpShown { get; set; }
    public int MaxXpShown { get; set; }
    public float XpPercentage { get; set; }
    public Color MainRoleColor { get; set; }
    public MagickImage Picture { get; set; } = null!;
}