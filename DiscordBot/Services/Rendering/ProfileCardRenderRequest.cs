namespace DiscordBot.Services.Rendering;

public sealed record ProfileCardRenderRequest
{
    public required ulong UserId { get; init; }
    public string? Nickname { get; init; }
    public required string Username { get; init; }
    public long XpTotal { get; init; }
    public long XpRank { get; init; }
    public long KarmaRank { get; init; }
    public int Karma { get; init; }
    public int Level { get; init; }
    public double XpLow { get; init; }
    public double XpHigh { get; init; }
    public int XpShown { get; init; }
    public int MaxXpShown { get; init; }
    public float XpPercentage { get; init; }
    public Color MainRoleColor { get; init; }
    public Color AvatarSampleColor { get; init; }
    public byte[]? AvatarBytes { get; init; }
}
