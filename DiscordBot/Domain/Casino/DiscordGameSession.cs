using System.Reflection;
using Discord.WebSocket;
using DiscordBot.Domain;

[AttributeUsage(AttributeTargets.Field)]
public class ButtonMetadataAttribute : Attribute
{
    public string? Emoji { get; set; }
    public ButtonStyle Style { get; set; } = ButtonStyle.Primary;
    public string Label { get; set; } = string.Empty;
}

public interface IDiscordGameSession : IGameSession
{
    public Task<(Embed, MessageComponent)> GenerateEmbedAndButtons();
    public Embed GenerateRules();
    public string ShowHand(GamePlayer player);
}

public abstract class DiscordGameSession<TGame> : GameSession<TGame>, IDiscordGameSession
    where TGame : ICasinoGame
{
    protected IGuild Guild { get; init; } // The context of the interaction that started the game
    protected DiscordSocketClient Client { get; init; } // The context of the interaction that started the game
    protected SocketUser User { get; init; } // The user who started the game session

    public DiscordGameSession(TGame game, int maxSeats, DiscordSocketClient client, SocketUser user, IGuild guild, bool isPrivate = false) : base(game, maxSeats, isPrivate)
    {
        Client = client;
        User = user;
        Guild = guild;
    }

    #region Helper Methods

    protected string GetPlayerName(DiscordGamePlayer player)
    {
        if (player.IsAI) return $"🤖 AI {player.UserId}";
        var user = Client.GetUser(player.UserId);
        return user?.Mention ?? $"Unknown User ({player.UserId})";
    }

    protected string GetCurrentPlayerName()
    {
        if (Game.CurrentPlayer == null) return "No one (all players finished)";
        return GetPlayerName((DiscordGamePlayer)Game.CurrentPlayer);
    }

    private string GeneratePlayersList()
    {
        if (Players.Count == 0) return "None";
        var lines = Players.Select(p =>
            $"{GetPlayerName(p)} {(p.IsReady ? '✅' : '❌')} (Bet: {p.Bet})"
        );
        return string.Join("\n", lines);
    }

    protected string GeneratePlayerHandDescription(DiscordGamePlayer player, string hand, string actions)
    {
        var description = $"**{GetPlayerName(player)}**: {hand}";
        description += $"\n-# *Bet: {player.Bet}*";
        if (!string.IsNullOrEmpty(actions))
            description += $" - Actions: {actions}";
        description += "\n";
        return description;
    }

    protected string GenerateResultsDescription()
    {
        var description = "\n**Results:**\n";

        foreach (var player in Players)
        {
            var result = Game.GetPlayerGameResult(player);
            var payout = Game.CalculatePayout(player, GetTotalPot);
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

    #endregion
    #region Embed Generators

    private async Task<Embed> GenerateNotStartedEmbed()
    {
        var challenger = await Guild.GetUserAsync(User.Id);

        var title = IsPrivate ? $"🔒 {Game.Emoji} {GameName} Game Session (Private)" : $"{Game.Emoji} {GameName} Game Session";
        var description = IsPrivate 
            ? $"This is a private {GameName} game! Only players in the game can invite others."
            : $"Welcome to {GameName}! Click the buttons below to take actions.";

        return new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithAuthor($"Game started by {challenger.DisplayName}")
            .WithColor(IsPrivate ? Color.Orange : Color.Green)
            .AddField("Players", GeneratePlayersList(), true)
            .AddField("Seats Available", $"{PlayerCount}/{MaxSeats}", true)
            .AddField("Total Pot", $"{GetTotalPot}")
            .WithFooter($"Game started by {challenger.DisplayName} • Minimum {Game.MinPlayers} players ready required to start the game.")
            .Build();
    }

    protected abstract Embed GenerateInProgressEmbed();

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
        var rows = new List<ActionRowBuilder>();

        // For private games, show different buttons
        if (IsPrivate)
        {
            // Show invite button (access control handled in interaction handler)
            rows.Add(new ActionRowBuilder()
                .WithSelectMenu(new SelectMenuBuilder()
                    .WithCustomId($"invite_user:{Id}")
                    .WithPlaceholder("Select a user to invite...")
                    .WithType(ComponentType.UserSelect)
                    .WithMinValues(1)
                    .WithMaxValues(1))
            );

            // Show leave button for players in the game
            rows.Add(new ActionRowBuilder()
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
                })
            );
        }
        else
        {
            // Original buttons for public games
            rows.Add(new ActionRowBuilder()
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
                })
            );
        }

#if DEBUG
        // AI buttons (for debugging)
        rows.Add(new ActionRowBuilder()
            .WithButton("Add AI", $"ai_add:{Id}", ButtonStyle.Success, new Emoji("🤖"))
            .WithButton("Add FULL AI", $"ai_add_full:{Id}", ButtonStyle.Success, new Emoji("🤖"))
            .WithButton("Remove AI", $"ai_remove:{Id}", ButtonStyle.Danger, new Emoji("❌"))
        );
#endif

        // Betting buttons
        rows.Add(new ActionRowBuilder()
            .WithButton("+1", $"bet_add:{Id}:1", ButtonStyle.Secondary, new Emoji("1️⃣"))
            .WithButton("+10", $"bet_add:{Id}:10", ButtonStyle.Secondary, new Emoji("🔟"))
            .WithButton("+100", $"bet_add:{Id}:100", ButtonStyle.Secondary, new Emoji("💯"))
            .WithButton("All In", $"bet_allin:{Id}", ButtonStyle.Primary, new Emoji("💰"))
            .WithButton("Reset to 1", $"bet_set:{Id}:1", ButtonStyle.Danger, new Emoji("🔄"))
        );

        return new ComponentBuilder().WithRows(rows).Build();
    }

    private MessageComponent GenerateInProgressButtons()
    {
        var components = new ComponentBuilder();

        var values = Enum.GetValues(Game.ActionType).Cast<Enum>();
        foreach (var action in values)
        {
            var member = Game.ActionType.GetMember(action.ToString()!)[0];
            var attr = member.GetCustomAttribute<ButtonMetadataAttribute>();

            components.WithButton(new ButtonBuilder
            {
                CustomId = $"action:{Id}:{action}",
                Label = string.IsNullOrEmpty(attr?.Label) ? action.ToString() : attr.Label,
                Emote = string.IsNullOrEmpty(attr?.Emoji) ? null : new Emoji(attr.Emoji),
                Style = attr?.Style ?? ButtonStyle.Primary,
            });
        }

        // Add Show Hand button for games with private hands
        if (Game.HasPrivateHands)
        {
            components.AddRow(
                new ActionRowBuilder().WithButton(new ButtonBuilder
                {
                    CustomId = $"show_hand:{Id}",
                    Label = "Show Hand",
                    Style = ButtonStyle.Secondary,
                    Emote = new Emoji("👁️")
                }));
        }

        return components.Build();
    }

    private MessageComponent GenerateFinishedButtons()
    {
        return new ComponentBuilder()
        // .WithButton("Reload Embed", $"reload:{Id}", ButtonStyle.Secondary, new Emoji("🔄"))
        .WithButton("Play Again", $"play_again:{Id}", ButtonStyle.Primary, new Emoji("🔄"))
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

    public virtual string ShowHand(GamePlayer player)
    {
        return Game.ShowHand(player);
    }

    #endregion
}