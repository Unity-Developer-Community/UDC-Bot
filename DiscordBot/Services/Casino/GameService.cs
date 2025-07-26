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
            _ => throw new ArgumentOutOfRangeException(nameof(game), $"Unknown game: {game}")
        };
    }

    public IDiscordGameSession CreateGameSession(CasinoGame game, int maxSeats, DiscordSocketClient client, SocketUser user, IGuild guild)
    {
        var gameInstance = GetGameInstance(game);
        // var session = new DiscordGameSession<ICasinoGame>(gameInstance, maxSeats, client, user, guild);
        var session = new BlackjackDiscordGameSession(new Blackjack(), maxSeats, client, user, guild);
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