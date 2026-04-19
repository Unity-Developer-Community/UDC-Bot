using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Modules;

namespace DiscordBot.Services.Fun.Casino;

public class GameService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, IDiscordGameSession> _activeSessions = new();
    private readonly CasinoService _casinoService;

    public GameService(CasinoService casinoService)
    {
        _casinoService = casinoService;
    }

    public ICasinoGame GetGameInstance(CasinoGame game)
    {
        return game switch
        {
            CasinoGame.Blackjack => new Blackjack(),
            CasinoGame.RockPaperScissors => new RockPaperScissors(),
            CasinoGame.Poker => new Poker(),
            _ => throw new ArgumentOutOfRangeException(nameof(game), $"Unknown game: {game}")
        };
    }

    private IDiscordGameSession CreateDiscordGameSession(CasinoGame game, ICasinoGame gameInstance, int maxSeats, DiscordSocketClient client, SocketUser user, IGuild guild)
    {
        return game switch
        {
            CasinoGame.Blackjack => new BlackjackDiscordGameSession((Blackjack)gameInstance, maxSeats, client, user, guild),
            CasinoGame.RockPaperScissors => new RockPaperScissorsDiscordGameSession((RockPaperScissors)gameInstance, maxSeats, client, user, guild),
            CasinoGame.Poker => new PokerDiscordGameSession((Poker)gameInstance, maxSeats, client, user, guild),
            _ => throw new ArgumentOutOfRangeException(nameof(game), $"Unknown game session type: {game}")
        };
    }

    public IDiscordGameSession CreateGameSession(CasinoGame game, int maxSeats, DiscordSocketClient client, SocketUser user, IGuild guild)
    {
        var gameInstance = GetGameInstance(game);
        var session = CreateDiscordGameSession(game, gameInstance, maxSeats == 0 ? gameInstance.MaxPlayers : maxSeats, client, user, guild);
        _activeSessions[session.Id] = session;
        return session;
    }

    public IDiscordGameSession PlayAgain(IDiscordGameSession session)
    {
        session.Reset();
        return session;
    }

    public IDiscordGameSession? GetActiveSession(string id)
    {
        if (Guid.TryParse(id, out var guid) && _activeSessions.TryGetValue(guid, out var session))
            return session;
        return null;
    }

    public void RemoveGameSession(IDiscordGameSession session)
    {
        _activeSessions.TryRemove(session.Id, out _);
    }

    public async Task JoinGame(IDiscordGameSession session, ulong userId)
    {
        var user = await _casinoService.GetOrCreateCasinoUser(userId.ToString());
        if (user.Tokens < 1) throw new InvalidOperationException("You must have at least 1 token.");

        session.AddPlayer(userId, 1);
    }

    public async Task SetBet(IDiscordGameSession session, ulong userId, long bet)
    {
        var user = await _casinoService.GetOrCreateCasinoUser(userId.ToString());
        if (bet > user.Tokens) throw new InvalidOperationException("You do not have enough tokens.");
        if (bet < 1) throw new InvalidOperationException("You must bet at least 1 token.");
        session.SetPlayerBet(userId, bet);
    }

    public async Task EndGame(IDiscordGameSession session)
    {
        var payouts = session.EndGame();
        foreach (var (player, payout) in payouts)
        {
            if (player.IsAI) continue; // Skip AI players
            await _casinoService.UpdateUserTokens(player.UserId.ToString(), payout, TransactionKind.Game, new Dictionary<string, string>
            {
                { "game", session.GameName },
            });
        }
    }
}