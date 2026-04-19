using System.Collections.Concurrent;
using Discord.WebSocket;

namespace DiscordBot.Services.Fun;

public class DuelService
{
    private readonly ConcurrentDictionary<string, (ulong challengerId, ulong opponentId)> _activeDuels = new();
    private readonly Random _random = new();

    private static readonly string[] NormalWinMessages =
    {
        "{winner} lands a solid hit on {loser} and wins the duel!",
        "{winner} uses their sword to attack {loser}, but {loser} fails to dodge and {winner} wins!",
        "{winner} outmaneuvers {loser} with a swift strike and claims victory!",
        "{winner} blocks {loser}'s attack and counters with a decisive blow!",
        "{winner} dodges {loser}'s clumsy swing and delivers the winning hit!",
        "{winner} parries {loser}'s blade and strikes back to win the duel!",
        "{winner} feints left, strikes right, and defeats {loser}!",
        "{winner} overwhelms {loser} with superior technique and emerges victorious!"
    };

    public DuelService()
    {
    }

    public bool IsDuelActive(string duelKey) => _activeDuels.ContainsKey(duelKey);

    public bool TryStartDuel(string duelKey, ulong challengerId, ulong opponentId)
    {
        string reverseKey = $"{opponentId}_{challengerId}";
        if (_activeDuels.ContainsKey(duelKey) || _activeDuels.ContainsKey(reverseKey))
            return false;

        _activeDuels[duelKey] = (challengerId, opponentId);
        return true;
    }

    public bool TryRemoveDuel(string duelKey, out (ulong challengerId, ulong opponentId) duel)
        => _activeDuels.TryRemove(duelKey, out duel);

    public (ulong challengerId, ulong opponentId)? GetDuel(string duelKey)
        => _activeDuels.TryGetValue(duelKey, out var duel) ? duel : null;

    public bool ChallengerWins() => _random.Next(2) == 0;

    public string GetWinMessage(string winnerMention, string loserMention)
    {
        var message = NormalWinMessages[_random.Next(NormalWinMessages.Length)];
        return message.Replace("{winner}", winnerMention).Replace("{loser}", loserMention);
    }
}
