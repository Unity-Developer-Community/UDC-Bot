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

    /// <summary>A line that lands less often than once in this many first spins is announced publicly.</summary>
    public const int PublicWinOneIn = 200;

    private static readonly Dictionary<SlotSymbol, int> ThreeOfAKind = new()
    {
        [SlotSymbol.Cherry] = 25,
        [SlotSymbol.Lemon] = 3,
        [SlotSymbol.Grape] = 5,
        [SlotSymbol.Bell] = 10,
        [SlotSymbol.Star] = 20,
        [SlotSymbol.Seven] = 100
    };

    /// <summary>
    /// Exactly two of a symbol anywhere on the line. Lemons are the filler symbol and diamond pairs are kept as near misses
    /// that offer a hold, so neither pays as a pair.
    /// </summary>
    private static readonly Dictionary<SlotSymbol, int> TwoOfAKind = new()
    {
        [SlotSymbol.Cherry] = 2,
        [SlotSymbol.Grape] = 2,
        [SlotSymbol.Bell] = 2,
        [SlotSymbol.Star] = 2,
        [SlotSymbol.Seven] = 3
    };

    /// <summary>Symbol counts per reel strip. The strips are built from these, so a count is a weight.</summary>
    private static readonly (SlotSymbol Symbol, int Count)[][] ReelWeights =
    [
        [(SlotSymbol.Cherry, 3), (SlotSymbol.Lemon, 14), (SlotSymbol.Grape, 8), (SlotSymbol.Bell, 6), (SlotSymbol.Star, 4), (SlotSymbol.Seven, 3), (SlotSymbol.Diamond, 1)],
        [(SlotSymbol.Cherry, 3), (SlotSymbol.Lemon, 14), (SlotSymbol.Grape, 8), (SlotSymbol.Bell, 6), (SlotSymbol.Star, 4), (SlotSymbol.Seven, 2), (SlotSymbol.Diamond, 2)],
        [(SlotSymbol.Cherry, 3), (SlotSymbol.Lemon, 14), (SlotSymbol.Grape, 8), (SlotSymbol.Bell, 6), (SlotSymbol.Star, 3), (SlotSymbol.Seven, 3), (SlotSymbol.Diamond, 2)]
    ];

    /// <summary>The three reel strips. The symbol at a stop index is on the payline, its neighbours above and below it (wrapping).</summary>
    public static IReadOnlyList<IReadOnlyList<SlotSymbol>> Reels { get; } = ReelWeights.Select(BuildStrip).ToArray();

    /// <summary>The paytable, generated from the same data the engine pays from, best lines first.</summary>
    public static IReadOnlyList<SlotPaytableEntry> Paytable { get; } =
    [
        Entry(SlotSymbol.Diamond, 3, 0),
        .. ThreeOfAKind.Select(e => Entry(e.Key, 3, e.Value)).OrderByDescending(e => e.Multiplier),
        .. TwoOfAKind.Select(e => Entry(e.Key, 2, e.Value)).OrderByDescending(e => e.Multiplier).ThenBy(e => e.Chance)
    ];

    /// <summary>Returns the win multiple of a payline, and whether it is the jackpot line.</summary>
    public static (int Multiplier, bool IsJackpot) Evaluate(IReadOnlyList<SlotSymbol> line) =>
        Match(line) is { } entry ? (entry.Multiplier, entry.IsJackpot) : (0, false);

    /// <summary>The paytable line a payline wins, or <c>null</c> when it wins nothing.</summary>
    public static SlotPaytableEntry? Match(IReadOnlyList<SlotSymbol> line)
    {
        // With three reels, at most one symbol appears more than once.
        var (symbol, count) = line[0] == line[1] && line[1] == line[2] ? (line[0], 3)
            : line[0] == line[1] || line[0] == line[2] ? (line[0], 2)
            : line[1] == line[2] ? (line[1], 2)
            : (line[0], 1);

        return Paytable.FirstOrDefault(e => e.Symbol == symbol && e.Count == count);
    }

    /// <summary>A winning line rarer than one first spin in <see cref="PublicWinOneIn"/>.</summary>
    public static bool IsRareWin(IReadOnlyList<SlotSymbol> line) => Match(line) is { IsAnnounced: true };

    /// <summary>A paytable line with its exact chance on a first spin, computed from the reel weights.</summary>
    private static SlotPaytableEntry Entry(SlotSymbol symbol, int count, int multiplier)
    {
        var p = ReelWeights.Select(reel => (double)reel.Single(w => w.Symbol == symbol).Count / reel.Sum(w => w.Count)).ToArray();
        var chance = count == ReelCount
            ? p[0] * p[1] * p[2]
            : (1 - p[0]) * p[1] * p[2] + p[0] * (1 - p[1]) * p[2] + p[0] * p[1] * (1 - p[2]);
        return new SlotPaytableEntry(symbol, count, multiplier, chance);
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
