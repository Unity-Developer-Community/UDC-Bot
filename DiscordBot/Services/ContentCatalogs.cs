using DiscordBot.Settings;

namespace DiscordBot.Services;

public sealed class FaqData
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string[] Keywords { get; set; } = [];
}

public interface IRulesCatalog
{
    IReadOnlyList<ChannelData> Channels { get; }
}

public sealed class RulesCatalog(Rules rules) : IRulesCatalog
{
    public IReadOnlyList<ChannelData> Channels { get; } = rules.Channel.ToArray();
}

public interface IFaqCatalog
{
    IReadOnlyList<FaqData> Entries { get; }
}

public sealed class FaqCatalog(IEnumerable<FaqData> entries) : IFaqCatalog
{
    public IReadOnlyList<FaqData> Entries { get; } = entries.ToArray();
}
