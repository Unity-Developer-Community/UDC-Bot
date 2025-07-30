using System.Reflection;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Modules;
using DiscordBot.Settings;

namespace DiscordBot.Services;

/// <summary>
/// Implementation of IBetValidator that uses GameService and CasinoService
/// to validate bet increases across all active games
/// </summary>
public class GameServiceBetValidator : IBetValidator
{
    private readonly GameService _gameService;
    private readonly CasinoService _casinoService;
    private readonly string _currentSessionId;

    public GameServiceBetValidator(GameService gameService, CasinoService casinoService, string currentSessionId)
    {
        _gameService = gameService;
        _casinoService = casinoService;
        _currentSessionId = currentSessionId;
    }

    public async Task<bool> CanIncreaseBetAsync(ulong userId, ulong additionalAmount)
    {
        var user = await _casinoService.GetOrCreateCasinoUser(userId.ToString());
        var committedTokens = _gameService.GetCommittedTokens(userId, _currentSessionId);
        var availableTokens = user.Tokens - committedTokens;
        
        return additionalAmount <= availableTokens;
    }

    public async Task<string> GetBetIncreaseErrorAsync(ulong userId, ulong additionalAmount)
    {
        var user = await _casinoService.GetOrCreateCasinoUser(userId.ToString());
        var committedTokens = _gameService.GetCommittedTokens(userId, _currentSessionId);
        var availableTokens = user.Tokens - committedTokens;
        
        return $"Cannot increase bet: insufficient tokens. Available: {availableTokens}, Required: {additionalAmount}";
    }
}

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
        
        // Inject the bet validator using reflection to avoid generic type issues
        var betValidator = new GameServiceBetValidator(this, _casinoService, session.Id.ToString());
        var betValidatorProperty = gameInstance.GetType().GetProperty("BetValidator");
        betValidatorProperty?.SetValue(gameInstance, betValidator);
        
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