namespace DiscordBot.Services.Recruitment.Domain.Policies;

internal sealed class AcknowledgementRecovery
{
    private readonly HashSet<string> _listings = new();
    public void Add(string id) => _listings.Add(id);
    public bool Remove(string id) => _listings.Remove(id);
    public bool Contains(string id) => _listings.Contains(id);
}
