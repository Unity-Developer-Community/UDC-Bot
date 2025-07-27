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
        var session = CreateDiscordGameSession(game, gameInstance, maxSeats == 0 ? gameInstance.MaxPlayers : maxSeats, client, user, guild);
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

    public async Task SetBet(IDiscordGameSession session, ulong userId, ulong bet)
    {
        var user = await _casinoService.GetOrCreateCasinoUser(userId.ToString());
        if (bet < 1) throw new InvalidOperationException("You must bet at least 1 token.");
        
        // Calculate tokens committed to other active games
        var committedTokens = GetCommittedTokens(userId, session.Id.ToString());
        var availableTokens = user.Tokens - committedTokens;
        
        if (bet > availableTokens) 
            throw new InvalidOperationException($"You do not have enough tokens. Available: {availableTokens} (You have {committedTokens} tokens committed to other active games).");
        
        session.SetPlayerBet(userId, bet);
    }

    /// <summary>
    /// Calculates the total tokens committed to active games by a user, excluding the specified session
    /// </summary>
    /// <param name="userId">The user ID to check</param>
    /// <param name="excludeSessionId">Session ID to exclude from calculation (optional)</param>
    /// <returns>Total committed tokens</returns>
    public ulong GetCommittedTokens(ulong userId, string? excludeSessionId = null)
    {
        ulong committedTokens = 0;
        
        foreach (var session in _activeSessions)
        {
            // Skip the session we're excluding (typically the current session being modified)
            if (excludeSessionId != null && session.Id.ToString() == excludeSessionId)
                continue;
                
            // Only count tokens from games that haven't finished
            if (session.State == GameState.NotStarted || session.State == GameState.InProgress)
            {
                var player = session.GetPlayer(userId);
                if (player != null)
                {
                    committedTokens += player.Bet;
                }
            }
        }
        
        return committedTokens;
    }
    
    /// <summary>
    /// Validates if a user can increase their bet in an active game
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <param name="currentBet">The current bet amount</param>
    /// <param name="newBet">The proposed new bet amount</param>
    /// <param name="sessionId">The session where the bet increase is happening</param>
    /// <returns>True if the bet increase is valid</returns>
    public async Task<bool> ValidateBetIncrease(ulong userId, ulong currentBet, ulong newBet, string sessionId)
    {
        if (newBet <= currentBet) return true; // Not an increase
        
        var user = await _casinoService.GetOrCreateCasinoUser(userId.ToString());
        var committedTokens = GetCommittedTokens(userId, sessionId);
        var availableTokens = user.Tokens - committedTokens;
        var additionalTokensNeeded = newBet - currentBet;
        
        return additionalTokensNeeded <= availableTokens;
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