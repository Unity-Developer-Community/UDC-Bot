namespace DiscordBot.Domain;

/// <summary>The symbols on the slot machine reels.</summary>
public enum SlotSymbol
{
    Cherry,
    Lemon,
    Grape,
    Bell,
    Star,
    Seven,
    Diamond
}

public static class SlotSymbolExtensions
{
    public static string Emoji(this SlotSymbol symbol) => symbol switch
    {
        SlotSymbol.Cherry => "🍒",
        SlotSymbol.Lemon => "🍋",
        SlotSymbol.Grape => "🍇",
        SlotSymbol.Bell => "🔔",
        SlotSymbol.Star => "⭐",
        SlotSymbol.Seven => "7️⃣",
        SlotSymbol.Diamond => "💎",
        _ => throw new ArgumentOutOfRangeException(nameof(symbol), symbol, null)
    };
}
