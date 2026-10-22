namespace DiscordBot.Utils;

public static class DomainName
{
    public static bool Matches(string host, string domain) => host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}
