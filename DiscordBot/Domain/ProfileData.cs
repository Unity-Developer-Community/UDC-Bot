using ImageMagick;

namespace DiscordBot.Domain;

public class ProfileData
{
    public ulong UserId { get; set; }
    public string Nickname { get; set; }
    public string Username { get; set; }
    public uint XpTotal { get; set; }
    public uint XpRank { get; set; }
    public uint KarmaRank { get; set; }
    public uint Karma { get; set; }
    public uint Level { get; set; }
    public double XpLow { get; set; }
    public double XpHigh { get; set; }
    public uint XpShown { get; set; }
    public uint MaxXpShown { get; set; }
    public float XpPercentage { get; set; }
    public Color MainRoleColor { get; set; }
    public MagickImage Picture { get; set; }
}