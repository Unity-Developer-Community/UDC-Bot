namespace DiscordBot.Domain;

/// <summary>
/// Rules of the three-reel slot machine: reel strips, paytable, spin and respin.
/// The paytable and strips live in code because the exact-return test guards them together.
/// </summary>
public static class SlotMachine
{
    public const int ReelCount = 3;
    public const int MinBet = 1;
    public const int MaxBet = 100;

    /// <summary>Chance in percent that a losing spin offers a hold.</summary>
    public const int HoldOfferPercent = 25;

    /// <summary>A win of at least this multiple of the bet is announced publicly.</summary>
    public const int PublicWinMultiplier = 20;

    private static readonly Dictionary<SlotSymbol, int> ThreeOfAKind = new()
    {
        [SlotSymbol.Cherry] = 5,
        [SlotSymbol.Lemon] = 4,
        [SlotSymbol.Grape] = 8,
        [SlotSymbol.Bell] = 15,
        [SlotSymbol.Star] = 30,
        [SlotSymbol.Seven] = 100
    };

    private const int OneCherry = 1;
    private const int TwoCherries = 2;

    /// <summary>Symbol counts per reel strip. The strips are built from these, so a count is a weight.</summary>
    private static readonly (SlotSymbol Symbol, int Count)[][] ReelWeights =
    [
        [(SlotSymbol.Cherry, 3), (SlotSymbol.Lemon, 8), (SlotSymbol.Grape, 7), (SlotSymbol.Bell, 6), (SlotSymbol.Star, 4), (SlotSymbol.Seven, 3), (SlotSymbol.Diamond, 1)],
        [(SlotSymbol.Cherry, 3), (SlotSymbol.Lemon, 8), (SlotSymbol.Grape, 7), (SlotSymbol.Bell, 6), (SlotSymbol.Star, 4), (SlotSymbol.Seven, 2), (SlotSymbol.Diamond, 2)],
        [(SlotSymbol.Cherry, 3), (SlotSymbol.Lemon, 8), (SlotSymbol.Grape, 7), (SlotSymbol.Bell, 6), (SlotSymbol.Star, 3), (SlotSymbol.Seven, 3), (SlotSymbol.Diamond, 2)]
    ];

    /// <summary>The three reel strips. The symbol at a stop index is on the payline, its neighbours above and below it (wrapping).</summary>
    public static IReadOnlyList<IReadOnlyList<SlotSymbol>> Reels { get; } = ReelWeights.Select(BuildStrip).ToArray();

    /// <summary>The paytable, generated from the same data the engine pays from.</summary>
    public static IReadOnlyList<SlotPaytableEntry> Paytable { get; } =
    [
        new("💎💎💎", 0, true),
        .. ThreeOfAKind.OrderByDescending(e => e.Value)
            .Select(e => new SlotPaytableEntry(string.Concat(Enumerable.Repeat(e.Key.Emoji(), ReelCount)), e.Value, false)),
        new("🍒🍒 + any", TwoCherries, false),
        new("🍒 + any", OneCherry, false)
    ];

    /// <summary>Returns the win multiple of a payline, and whether it is the jackpot line.</summary>
    public static (int Multiplier, bool IsJackpot) Evaluate(IReadOnlyList<SlotSymbol> line)
    {
        if (line[0] == line[1] && line[1] == line[2])
            return line[0] == SlotSymbol.Diamond ? (0, true) : (ThreeOfAKind[line[0]], false);

        return line.Count(s => s == SlotSymbol.Cherry) switch
        {
            2 => (TwoCherries, false),
            1 => (OneCherry, false),
            _ => (0, false)
        };
    }

    public static SlotResult Spin(ISlotRandom rng)
    {
        var stops = new int[ReelCount];
        for (var i = 0; i < ReelCount; i++)
            stops[i] = rng.Next(Reels[i].Count);

        return Build(stops, isRespin: false, rng);
    }

    /// <summary>
    /// Spins the reels that are not held once. Only allowed after a spin that offered a hold, and never offers another one.
    /// </summary>
    public static SlotResult Respin(SlotResult previous, IReadOnlyList<bool> held, ISlotRandom rng)
    {
        if (!previous.HoldOffered)
            throw new InvalidOperationException("The previous spin did not offer a hold.");
        if (held.Count != ReelCount)
            throw new ArgumentException($"Expected {ReelCount} hold flags.", nameof(held));

        var stops = new int[ReelCount];
        for (var i = 0; i < ReelCount; i++)
            stops[i] = held[i] ? previous.Stops[i] : rng.Next(Reels[i].Count);

        return Build(stops, isRespin: true, rng);
    }

    private static SlotResult Build(int[] stops, bool isRespin, ISlotRandom rng)
    {
        var symbols = stops.Select((stop, reel) => Reels[reel][stop]).ToArray();
        var (multiplier, isJackpot) = Evaluate(symbols);
        var isWin = isJackpot || multiplier > 0;
        var holdOffered = !isRespin && !isWin && rng.Next(100) < HoldOfferPercent;
        return new SlotResult(stops, symbols, multiplier, isJackpot, holdOffered, isRespin);
    }

    /// <summary>
    /// Spreads the symbols evenly along the strip (smooth weighted round-robin), so equal symbols rarely sit next to each other.
    /// </summary>
    private static SlotSymbol[] BuildStrip((SlotSymbol Symbol, int Count)[] weights)
    {
        var total = weights.Sum(w => w.Count);
        var current = new int[weights.Length];
        var strip = new SlotSymbol[total];
        for (var i = 0; i < total; i++)
        {
            var best = 0;
            for (var j = 0; j < weights.Length; j++)
            {
                current[j] += weights[j].Count;
                if (current[j] > current[best]) best = j;
            }
            current[best] -= total;
            strip[i] = weights[best].Symbol;
        }

        SeparateNeighbours(strip);
        return strip;
    }

    /// <summary>
    /// The round-robin can leave equal symbols side by side where the strip wraps around. Swapping one of them with a symbol
    /// elsewhere removes that without changing the counts, which are all the odds depend on.
    /// </summary>
    private static void SeparateNeighbours(SlotSymbol[] strip)
    {
        bool Clashes(int i) => strip[i] == strip[(i + 1) % strip.Length] || strip[i] == strip[(i + strip.Length - 1) % strip.Length];

        for (var i = 0; i < strip.Length; i++)
        {
            if (!Clashes(i)) continue;
            for (var j = 0; j < strip.Length; j++)
            {
                (strip[i], strip[j]) = (strip[j], strip[i]);
                if (!Clashes(i) && !Clashes(j)) break;
                (strip[i], strip[j]) = (strip[j], strip[i]);
            }
        }
    }
}
