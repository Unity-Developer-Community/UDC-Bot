using Discord.WebSocket;
using DiscordBot.Domain;

public class RussianRouletteDiscordGameSession : DiscordGameSession<RussianRoulette>
{
    public RussianRouletteDiscordGameSession(RussianRoulette game, int maxSeats, DiscordSocketClient client, SocketUser user, IGuild guild)
        : base(game, maxSeats, client, user, guild)
    { }

    protected override Embed GenerateInProgressEmbed()
    {
        var embed = new EmbedBuilder()
            .WithTitle($"{Game.Emoji} Russian Roulette")
            .WithDescription(GenerateGameDescription())
            .WithColor(Game.State == GameState.Finished 
                ? (Players.Count > 0 && Game.GameData[Players[0]].WonGame ? Color.Green : Color.Red)
                : Color.Orange);

        if (Game.State == GameState.Finished)
        {
            var player = Players[0];
            var data = Game.GameData[player];
            
            if (data.WonGame)
            {
                embed.WithFooter("🎉 Victory! You beat the house!");
            }
            else
            {
                embed.WithFooter("💀 The house always wins... eventually.");
            }
        }
        else if (Players.Count > 0 && Game.GameData[Players[0]].HasSelectedSystem)
        {
            embed.WithFooter("⚠️ Remember: You're playing against the house. The odds are not in your favor.");
        }

        return embed.Build();
    }

    private string GenerateGameDescription()
    {
        if (Players.Count == 0)
            return "**Waiting for a player to join...**";

        var player = Players[0]; // Only one player in Russian Roulette
        var data = Game.GameData[player];

        var description = $"**Player:** {GetPlayerName(player)}\n";
        description += $"**Bet:** {player.Bet} tokens\n\n";

        if (!data.HasSelectedSystem)
        {
            description += "🎲 **Choose your game system:**\n\n";
            description += "**System 1 - Fixed Risk**\n";
            description += "• 1/6 chance of bullet each turn\n";
            description += "• Payouts: 1x → 1.1x → 1.4x → 1.9x → 2.9x → 5.9x\n\n";
            description += "**System 2 - Escalating Risk**\n";
            description += "• Turn 1: 1/6, Turn 2: 2/6, Turn 3: 3/6, etc.\n";
            description += "• Payouts: 1x → 1.1x → 1.65x → 3.4x → 10.4x → 63x\n\n";
            description += "*Choose wisely - the risk and reward profiles are very different!*";
        }
        else
        {
            var systemName = data.SelectedSystem == RussianRouletteSystem.System1 ? "System 1 (Fixed Risk)" : "System 2 (Escalating Risk)";
            description += $"**System:** {systemName}\n";
            description += $"**Turn:** {data.CurrentTurn + 1}/6\n";
            description += $"**Bullets Survived:** {data.BulletsSurvived}\n\n";

            if (data.GameEnded)
            {
                if (data.WonGame)
                {
                    description += "🎉 **CONGRATULATIONS!**\n";
                    if (data.BulletsSurvived == 6)
                    {
                        description += "You survived all 6 chambers! The house bows to your courage!\n";
                    }
                    else
                    {
                        description += "You wisely cashed out while ahead!\n";
                    }
                    description += $"**Bullets Survived:** {data.BulletsSurvived}\n";
                    description += $"**Final Payout:** {Game.GetCurrentPayoutMultiplier(player):F1}x ({(long)(player.Bet * Game.GetCurrentPayoutMultiplier(player))} tokens)\n";
                }
                else
                {
                    description += "💀 **GAME OVER**\n";
                    description += "You pulled the trigger and got the bullet!\n";
                    description += $"**Bullets Survived:** {data.BulletsSurvived}\n";
                    description += $"**Tokens Lost:** {player.Bet}\n";
                }
            }
            else
            {
                // Show current risk level
                if (data.SelectedSystem == RussianRouletteSystem.System1)
                {
                    description += $"**Current Risk:** 1/6 chance of bullet\n";
                }
                else
                {
                    var bullets = data.CurrentTurn + 1;
                    description += $"**Current Risk:** {bullets}/6 chance of bullet\n";
                }

                description += $"**Current Payout:** {Game.GetCurrentPayoutMultiplier(player):F1}x ({(long)(player.Bet * Game.GetCurrentPayoutMultiplier(player))} tokens)\n";
                
                if (data.BulletsSurvived < 5)
                {
                    description += $"**Next Payout:** {Game.GetNextPayoutMultiplier(player):F1}x ({(long)(player.Bet * Game.GetNextPayoutMultiplier(player))} tokens)\n";
                }

                description += "\n🔫 Pull the trigger and risk it all...\n";
                if (data.BulletsSurvived > 0)
                {
                    description += "💰 Or cash out and take your winnings!\n";
                }
            }
        }

        return description;
    }

    public override Embed GenerateRules()
    {
        var embed = new EmbedBuilder()
            .WithTitle($"{Game.Emoji} Russian Roulette Rules")
            .WithDescription("Survive as many chambers as possible by avoiding the bullet!")
            .WithColor(Color.Orange)
            .AddField("🎯 Objective", "Survive as many chambers as possible by avoiding the bullet!", false)
            .AddField("🎲 System 1 - Fixed Risk", 
                "• Each turn has exactly 1/6 chance of bullet\n" +
                "• Consistent risk, moderate rewards\n" +
                "• Payouts: 1x → 1.1x → 1.4x → 1.9x → 2.9x → 5.9x", false)
            .AddField("⚡ System 2 - Escalating Risk", 
                "• Turn 1: 1/6 chance, Turn 2: 2/6 chance, etc.\n" +
                "• Risk increases each turn, massive rewards\n" +
                "• Payouts: 1x → 1.1x → 1.65x → 3.4x → 10.4x → 63x", false)
            .AddField("🎮 How to Play", 
                "1. **Join** the game and place your bet\n" +
                "2. **Choose** your preferred system (1 or 2)\n" +
                "3. Each turn, choose to:\n" +
                "   • **Pull Trigger** - Risk everything for higher payout\n" +
                "   • **Cash Out** - Take current winnings (after turn 1)\n" +
                "4. **Survive** 6 chambers to automatically cash out with maximum payout", false)
            .AddField("🏆 Winning", 
                "• **Cash out** to secure your current payout multiplier\n" +
                "• **Survive all 6** chambers for automatic maximum payout\n" +
                "• **Hit the bullet** and lose your entire bet", false)
            .AddField("💡 Strategy Tips", 
                "• System 1: More consistent, good for steady gains\n" +
                "• System 2: High risk/high reward, massive payouts possible\n" +
                "• Consider cashing out early vs. going for maximum payout\n" +
                "• The house edge is built into the payout multipliers", false)
            .WithFooter("Remember: You're playing against the house - know when to quit!");

        return embed.Build();
    }
}