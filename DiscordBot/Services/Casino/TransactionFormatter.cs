using Discord.WebSocket;
using DiscordBot.Domain;

namespace DiscordBot.Services;

public class TransactionFormatter
{
    public (string emoji, string title, string description) Format(
        TokenTransaction transaction, SocketGuild guild, bool showUserInfo = false)
    {
        var (emoji, title, description) = transaction.Kind switch
        {
            TransactionKind.TokenInitialisation => ("🎯", "Account Created", ""),
            TransactionKind.DailyReward => ("📅", "Daily Reward", ""),
            TransactionKind.Gift => FormatGift(transaction, guild),
            TransactionKind.Game => FormatGame(transaction),
            TransactionKind.Admin => FormatAdmin(transaction, guild),
            _ => ("❓", transaction.TransactionType, "")
        };

        if (showUserInfo)
        {
            var user = guild.GetUser(ulong.Parse(transaction.UserID));
            var username = user?.DisplayName ?? "Unknown User";
            return (emoji, $"{username}: {title}", description);
        }

        return (emoji, title, description);
    }

    private static (string emoji, string title, string description) FormatGift(
        TokenTransaction transaction, SocketGuild guild)
    {
        SocketGuildUser user = null;
        var userId = transaction.Details?.GetValueOrDefault(transaction.Amount >= 0 ? "from" : "to");
        if (userId != null) user = guild.GetUser(ulong.Parse(userId));

        string title = transaction.Amount > 0 ? "Gift Received" : "Gift Sent";
        if (user != null) title = transaction.Amount > 0 ? $"Gift from {user.DisplayName}" : $"Gift to {user.DisplayName}";

        return ("🎁", title, "");
    }

    private static (string emoji, string title, string description) FormatGame(TokenTransaction transaction)
    {
        var gameName = transaction.Details?.GetValueOrDefault("game");

        string emoji = transaction.Amount >= 0 ? "📈" : "📉";
        string title = transaction.Amount >= 0 ? "Won" : "Lost";
        if (gameName != null) title += $" {CapitalizeFirst(gameName)}";

        return (emoji, title, "");
    }

    private static (string emoji, string title, string description) FormatAdmin(
        TokenTransaction transaction, SocketGuild guild)
    {
        var adminId = transaction.Details?.GetValueOrDefault("admin");
        var action = transaction.Details?.GetValueOrDefault("action");
        SocketGuildUser admin = null;
        if (adminId != null) admin = guild.GetUser(ulong.Parse(adminId));

        string title = action switch
        {
            "add" => "Tokens Added",
            "set" => "Tokens Set",
            _ => $"UNKNOWN ACTION: {action}"
        };
        string description = action switch
        {
            "set" => "This overrides past transactions",
            _ => ""
        };

        if (admin != null) title += $" by Admin {admin.DisplayName}";

        return ("⚙️", title, description);
    }

    private static string CapitalizeFirst(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;
        return char.ToUpper(input[0]) + input[1..].ToLower();
    }
}
