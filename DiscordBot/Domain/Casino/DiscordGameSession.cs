using Discord.WebSocket;
using DiscordBot.Domain;

public interface IDiscordGameSession : IGameSession
{
    public Task<(Embed, MessageComponent)> GenerateEmbedAndButtons();
    public Embed GenerateRules();
}

public abstract class DiscordGameSession<TGame> : GameSession<TGame>, IDiscordGameSession
    where TGame : ICasinoGame
{
    protected IGuild Guild { get; init; } // The context of the interaction that started the game
    protected DiscordSocketClient Client { get; init; } // The context of the interaction that started the game
    protected SocketUser User { get; init; } // The user who started the game session

    public DiscordGameSession(TGame game, int maxSeats, DiscordSocketClient client, SocketUser user, IGuild guild) : base(game, maxSeats)
    {
        Client = client;
        User = user;
        Guild = guild;
    }

    protected string GetPlayerName(DiscordGamePlayer player)
    {
        if (player.IsAI) return $"🤖 AI {player.UserId}";
        var user = Client.GetUser(player.UserId);
        return user?.Mention ?? $"Unknown User ({player.UserId})";
    }

    private string GetPlayers()
    {
        if (Players.Count == 0) return "None";
        var lines = Players.Select(p =>
            $"{GetPlayerName(p)} {(p.IsReady ? '✅' : '❌')} (Bet: {p.Bet})"
        );
        return string.Join("\n", lines);
    }

    #region Embed Generators

    private async Task<Embed> GenerateNotStartedEmbed()
    {
        var challenger = await Guild.GetUserAsync(User.Id);

        return new EmbedBuilder()
            .WithTitle($"{Game.Emoji} {GameName} Game Session")
            .WithDescription($"Welcome to {GameName}! Click the buttons below to take actions.")
            .WithAuthor($"Game started by {challenger.DisplayName}")
            .WithColor(Color.Green)
            .AddField("Players", GetPlayers(), true)
            .AddField("Seats Available", $"{PlayerCount}/{MaxSeats}", true)
            .AddField("Total Pot", $"{GetTotalPot}")
            .WithFooter($"Game started by {challenger.DisplayName} • Minimum {Game.MinPlayers} players ready required to start the game.")
            .Build();
    }

    protected abstract Embed GenerateInProgressEmbed();

    protected string GenerateResultsDescription()
    {
        var description = "\n**Results:**\n";

        foreach (var player in Players)
        {
            var result = Game.GetPlayerGameResult(player);
            var payout = Game.CalculatePayout(player);
            var resultEmoji = result switch
            {
                GamePlayerResult.Won => "🏆",
                GamePlayerResult.Lost => "❌",
                GamePlayerResult.Tie => "🤝",
                _ => "❓"
            };
            description += $"* {resultEmoji} {GetPlayerName(player)}: {result} (Payout: {payout:+0;-0;0})\n";
        }

        return description;
    }

    protected virtual Embed GenerateFinishedEmbed()
    {
        return new EmbedBuilder()
            .WithTitle($"{Game.Emoji} {GameName} Finished")
            .Build();
    }

    private Embed GenerateAbandonedEmbed()
    {
        return new EmbedBuilder()
            .WithTitle($"{Game.Emoji} {GameName} Abandoned")
            .Build();
    }

    #endregion

    #region Button Generators

    private MessageComponent GenerateNotStartedButtons()
    {
        return new ComponentBuilder()
            .WithRows(new List<ActionRowBuilder>
            {
                // Buttons to join, leave, and toggle ready
                new ActionRowBuilder()
                    .WithButton(new ButtonBuilder
                    {
                        CustomId = $"join_game:{Id}",
                        Emote = new Emoji("✅"),
                        Label = "Join Game",
                        Style = ButtonStyle.Success,
                        IsDisabled = Players.Count >= MaxSeats
                    })
                    .WithButton(new ButtonBuilder
                    {
                        CustomId = $"leave_game:{Id}",
                        Emote = new Emoji("❌"),
                        Label = "Leave Game",
                        Style = ButtonStyle.Danger,
                        IsDisabled = Players.Count == 0
                    })
                    .WithButton(new ButtonBuilder
                    {
                        CustomId = $"toggle_ready:{Id}",
                        Emote = new Emoji("✅"),
                        Label = "Ready",
                        Style = ButtonStyle.Primary,
                        IsDisabled = Players.Count == 0
                    }),
                // Buttons for adding/removing AI players
                new ActionRowBuilder()
                    .WithButton("Add AI", $"ai_add:{Id}", ButtonStyle.Success, new Emoji("🤖"))
                    .WithButton("Remove AI", $"ai_remove:{Id}", ButtonStyle.Danger, new Emoji("❌")),
                // Buttons for betting
                new ActionRowBuilder()
                    .WithButton("+1", $"bet_add:{Id}:1", ButtonStyle.Secondary, new Emoji("1️⃣"))
                    .WithButton("+10", $"bet_add:{Id}:10", ButtonStyle.Secondary, new Emoji("🔟"))
                    .WithButton("+100", $"bet_add:{Id}:100", ButtonStyle.Secondary, new Emoji("💯"))
                    // .WithButton("Custom", $"bet_custom:{Id}", ButtonStyle.Secondary, new Emoji("✏️"))
                    .WithButton("All In", $"bet_allin:{Id}", ButtonStyle.Primary, new Emoji("💰"))
                    .WithButton("Reset to 1", $"bet_set:{Id}:1", ButtonStyle.Danger, new Emoji("🔄"))
            })
            .Build();
    }

    private MessageComponent GenerateInProgressButtons()
    {
        var components = new ComponentBuilder();
        var values = Enum.GetValues(Game.ActionType).Cast<Enum>().ToList();
        foreach (var action in values)
        {
            components.WithButton(new ButtonBuilder
            {
                CustomId = $"action:{Id}:{action}",
                Label = action.ToString(),
                Style = ButtonStyle.Primary,
            });
        }
        return components.Build();
    }

    private MessageComponent GenerateFinishedButtons()
    {
        return new ComponentBuilder()
        // .WithButton("Reload Embed", $"reload:{Id}", ButtonStyle.Secondary, new Emoji("🔄"))
        .Build();
    }

    private MessageComponent GenerateAbandonedButtons()
    {
        return new ComponentBuilder().Build();
    }

    #endregion

    #region General Methods

    private async Task<Embed> GenerateEmbed()
    {
        return Game.State switch
        {
            GameState.NotStarted => await GenerateNotStartedEmbed(),
            GameState.InProgress => GenerateInProgressEmbed(),
            GameState.Finished => GenerateFinishedEmbed(),
            GameState.Abandoned => GenerateAbandonedEmbed(),
            _ => throw new InvalidOperationException("Unknown game state")
        };
    }

    private MessageComponent GenerateButtons()
    {
        return Game.State switch
        {
            GameState.NotStarted => GenerateNotStartedButtons(),
            GameState.InProgress => GenerateInProgressButtons(),
            GameState.Finished => GenerateFinishedButtons(),
            GameState.Abandoned => GenerateAbandonedButtons(),
            _ => throw new InvalidOperationException("Unknown game state")
        };
    }

    #endregion

    #region Public Methods

    public async Task<(Embed, MessageComponent)> GenerateEmbedAndButtons()
    {
        return (await GenerateEmbed(), GenerateButtons());
    }

    public virtual Embed GenerateRules()
    {
        var embed = new EmbedBuilder()
            .WithTitle($"{Game.Emoji} {GameName} Rules")
            .WithDescription("Here are the rules for this game:")
            .AddField("Minimum Players", Game.MinPlayers.ToString(), true)
            .AddField("Maximum Players", Game.MaxPlayers.ToString(), true)
            .WithColor(Color.Blue);

        return embed.Build();
    }

    #endregion
}