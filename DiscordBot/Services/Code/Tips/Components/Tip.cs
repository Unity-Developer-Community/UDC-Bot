using Discord;

namespace DiscordBot.Services.Code.Tips.Components;

public class Tip : IEntity<ulong>
{
    public ulong Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = [];
    public List<string> ImagePaths { get; set; } = [];
    public int Requests { get; set; }
}
