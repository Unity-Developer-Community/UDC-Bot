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

    private IDiscordGameSession CreateDiscordGameSession(CasinoGame game, ICasinoGame gameInstance, int maxSeats, DiscordSocketClient client, SocketUser user, IGuild guild, bool isPrivate = false)
    {
        return game switch
        {
            CasinoGame.Blackjack => new BlackjackDiscordGameSession((Blackjack)gameInstance, maxSeats, client, user, guild, isPrivate),
            CasinoGame.RockPaperScissors => new RockPaperScissorsDiscordGameSession((RockPaperScissors)gameInstance, maxSeats, client, user, guild, isPrivate),
            CasinoGame.Poker => new PokerDiscordGameSession((Poker)gameInstance, maxSeats, client, user, guild, isPrivate),
            _ => throw new ArgumentOutOfRangeException(nameof(game), $"Unknown game session type: {game}")
        };
    }

    public IDiscordGameSession CreateGameSession(CasinoGame game, int maxSeats, DiscordSocketClient client, SocketUser user, IGuild guild, bool isPrivate = false)
    {
        var gameInstance = GetGameInstance(game);
        var session = CreateDiscordGameSession(game, gameInstance, maxSeats == 0 ? gameInstance.MaxPlayers : maxSeats, client, user, guild, isPrivate);
        _activeSessions.Add(session);
        return session;
    }

    public IDiscordGameSession PlayAgain(IDiscordGameSession session)
    {
        session.Reset();
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

    public async Task InviteToGame(IDiscordGameSession session, ulong userId)
    {
        if (!session.IsPrivate) throw new InvalidOperationException("Invitations are only available for private games.");
        
        var user = await _casinoService.GetOrCreateCasinoUser(userId.ToString());
        if (user.Tokens < 1) throw new InvalidOperationException("This user must have at least 1 token to join the game.");

        // Add player but set them as not ready (they need to ready up manually)
        var added = session.AddPlayer(userId, 1);
        if (!added) throw new InvalidOperationException("Could not invite this user to the game.");
        
        // Set the player as not ready since they were invited
        session.SetPlayerReady(userId, false);
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