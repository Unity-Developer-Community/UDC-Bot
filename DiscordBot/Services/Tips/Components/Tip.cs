using Discord;

namespace DiscordBot.Services.Tips.Components;

public class Tip: IEntity<ulong>
{
	public ulong Id { get; set; }
	public string Content { get; set; }
	public List<string> Keywords { get; set; }
	public List<string> ImagePaths { get; set; }
}
