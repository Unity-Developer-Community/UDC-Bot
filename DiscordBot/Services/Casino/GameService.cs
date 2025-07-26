using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Modules;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class GameService
{
    private readonly ILoggingService _loggingService;
    private readonly BotSettings _settings;
    private readonly List<IDiscordGameSession> _activeSessions = new();
    private readonly CasinoService _casinoService;

    public GameService(ILoggingService loggingService, BotSettings settings, CasinoService casinoService)
    {
        _loggingService = loggingService;
        _settings = settings;
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
        var session = CreateDiscordGameSession(game, gameInstance, maxSeats, client, user, guild);
        _activeSessions.Add(session);
        return session;
    }

    public IDiscordGameSession? GetActiveSession(string id)
    {
        return _activeSessions.FirstOrDefault(s => s.Id.ToString() == id);
    }

    public void RemoveGameSession(IDiscordGameSession session)
    {
        _activeSessions.Remove(session);
    }

    public async Task JoinGame(IDiscordGameSession session, ulong userId)
    {
        var user = await _casinoService.GetOrCreateCasinoUser(userId.ToString());
        if (user.Tokens < 1) throw new InvalidOperationException("You must have at least 1 token.");

        session.AddPlayer(userId, 1);
    }

    public async Task SetBet(IDiscordGameSession session, ulong userId, ulong bet)
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
            await _casinoService.UpdateUserTokens(player.UserId.ToString(), payout, TransactionType.Game, new Dictionary<string, string>
            {
                { "game", session.GameName },
            });
        }
    }
}