using System.IO;
using DiscordBot.Domain;
using DiscordBot.Settings;
using ImageMagick;
using ImageMagick.Drawing;

namespace DiscordBot.Services.Fun.Casino;

/// <summary>
/// Draws the slot machine: an animated GIF of the three reels stopping one after another, and the
/// static picture of an outcome. Both go through the same pipeline, so the last GIF frame is the static picture.
/// </summary>
public class SlotsRenderer
{
    private const int Width = 400;
    private const int Height = 220;
    private const int ReelWidth = 108;
    private const int CellHeight = 64;
    private const int SymbolSize = 56;
    private const int ReelGap = 4;
    private const int WindowTop = 14;
    private const int WindowHeight = CellHeight * 3;
    private const int PaylineY = WindowTop + WindowHeight / 2;

    /// <summary>Rows copied before and after a strip so a window crop never runs off the image, even with the blur.</summary>
    private const int StripPadding = 3;

    private const int FrameDelayTicks = 7;
    private const double FrameSeconds = FrameDelayTicks / 100.0;
    private const int FinalDelayTicks = 2000;

    private const double SpinSpeed = 20;
    private const double BrakeSeconds = 0.35;
    private const double BlurAboveSpeed = 6;

    private static readonly double[] StopSeconds = [0.8, 1.2, 1.7];

    private readonly string _assetsRoot;
    private readonly SemaphoreSlim _renderLock = new(1, 1);

    private Assets? _assets;

    public SlotsRenderer(BotSettings settings)
    {
        _assetsRoot = settings.AssetsRootPath;
    }

    /// <summary>Renders the reels spinning and stopping on the result, as an animated GIF.</summary>
    public virtual Task<MemoryStream> RenderSpinAsync(SlotResult result) => RunAsync(assets => RenderSpin(assets, result));

    /// <summary>Renders the reels at rest on the result, as a PNG with the same pixels as the last GIF frame.</summary>
    public virtual Task<MemoryStream> RenderStaticAsync(SlotResult result) => RunAsync(assets => RenderStatic(assets, result));

    /// <summary>
    /// Loads the art and builds the palette with one static render, so the first player's spin does not pay for it (about 2.7 s).
    /// </summary>
    public async Task WarmUpAsync()
    {
        int[] stops = [0, 0, 0];
        var symbols = stops.Select((stop, reel) => SlotMachine.Reels[reel][stop]).ToArray();
        var (multiplier, isJackpot) = SlotMachine.Evaluate(symbols);
        using var _ = await RenderStaticAsync(new SlotResult(stops, symbols, multiplier, isJackpot, false, false));
    }

    // One render at a time: production has half a CPU, and the shared strips are not thread-safe to clone from.
    private async Task<MemoryStream> RunAsync(Func<Assets, MemoryStream> render)
    {
        await _renderLock.WaitAsync();
        try
        {
            return await Task.Run(() => render(_assets ??= LoadAssets()));
        }
        finally
        {
            _renderLock.Release();
        }
    }

    private static MemoryStream RenderSpin(Assets assets, SlotResult result)
    {
        using var frames = new MagickImageCollection();
        var lastFrame = (int)Math.Ceiling(StopSeconds[^1] / FrameSeconds);
        for (var frame = 0; frame <= lastFrame; frame++)
        {
            var image = frame == 0 ? RenderFrame(assets, result, 0) : RenderChangedArea(assets, result, frame);
            image.AnimationDelay = (uint)(frame == lastFrame ? FinalDelayTicks : FrameDelayTicks);
            image.AnimationIterations = 1;
            frames.Add(image);
        }

        var stream = new MemoryStream();
        frames.Write(stream, MagickFormat.Gif);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream RenderStatic(Assets assets, SlotResult result)
    {
        using var image = RenderFrame(assets, result, StopSeconds[^1]);
        var stream = new MemoryStream();
        image.Write(stream, MagickFormat.Png);
        stream.Position = 0;
        return stream;
    }

    private static MagickImage RenderFrame(Assets assets, SlotResult result, double seconds)
    {
        using var frame = Compose(assets.Scene, result.Stops, seconds);
        return assets.Palette.Apply(frame);
    }

    /// <summary>
    /// Draws a frame after the first one as only the reels that moved since the previous frame, placed where they sit on the
    /// machine. The GIF keeps everything else from the earlier frames, so the file is smaller and quicker to write.
    /// </summary>
    private static MagickImage RenderChangedArea(Assets assets, SlotResult result, int frame)
    {
        // Reels stop left to right, so the reels still moving at the previous frame are always the rightmost ones.
        var previousSeconds = (frame - 1) * FrameSeconds;
        var firstMoving = Enumerable.Range(0, SlotMachine.ReelCount)
            .FirstOrDefault(reel => StopSeconds[reel] > previousSeconds, SlotMachine.ReelCount - 1);
        var left = ReelX(firstMoving);
        var area = new MagickGeometry(left, WindowTop, (uint)(ReelX(SlotMachine.ReelCount - 1) + ReelWidth - left), WindowHeight);

        using var full = Compose(assets.Scene, result.Stops, frame * FrameSeconds);
        full.Crop(area);
        full.ResetPage();
        var image = assets.Palette.Apply(full);
        image.Page = new MagickGeometry(left, WindowTop, Width, Height);
        image.GifDisposeMethod = GifDisposeMethod.None;
        return image;
    }

    /// <summary>Draws the machine at a time of the spin, in full color.</summary>
    private static MagickImage Compose(Scene scene, IReadOnlyList<int> stops, double seconds)
    {
        var frame = (MagickImage)scene.Machine.Clone();
        for (var reel = 0; reel < SlotMachine.ReelCount; reel++)
        {
            var (position, speed) = ReelMotion(seconds, StopSeconds[reel], stops[reel]);
            var strip = speed > BlurAboveSpeed ? scene.BlurredStrips[reel] : scene.Strips[reel];
            var length = SlotMachine.Reels[reel].Count;

            // The strip's first padding rows repeat its last symbols, so row 0 of the symbols sits StripPadding rows down.
            var wrapped = position % length;
            var top = (int)Math.Round((wrapped - 1 + StripPadding) * CellHeight);
            using var window = strip.CloneArea(new MagickGeometry(0, top, ReelWidth, WindowHeight));
            window.ResetPage();
            frame.Composite(window, ReelX(reel), WindowTop, CompositeOperator.Over);
        }

        frame.Composite(scene.Overlay, CompositeOperator.Over);
        return frame;
    }

    /// <summary>
    /// Where a reel is at a time, as the strip index on the payline (fractional while moving, always at or above
    /// the stop so the reel travels toward lower indexes, i.e. symbols move down), and how fast it moves in rows per second.
    /// It cruises at a constant speed, then brakes smoothly to land exactly on the stop.
    /// </summary>
    private static (double Position, double Speed) ReelMotion(double seconds, double stopSeconds, int stop)
    {
        var remaining = stopSeconds - seconds;
        if (remaining <= 0)
            return (stop, 0);

        if (remaining >= BrakeSeconds)
            return (stop + SpinSpeed * (remaining - BrakeSeconds / 2), SpinSpeed);

        return (stop + SpinSpeed * remaining * remaining / (2 * BrakeSeconds), SpinSpeed * remaining / BrakeSeconds);
    }

    private static int ReelX(int reel) => (Width - (SlotMachine.ReelCount * ReelWidth + (SlotMachine.ReelCount - 1) * ReelGap)) / 2
                                          + reel * (ReelWidth + ReelGap);

    private Assets LoadAssets()
    {
        var symbolImages = new Dictionary<SlotSymbol, MagickImage>();
        foreach (var symbol in Enum.GetValues<SlotSymbol>())
            symbolImages[symbol] = symbol == SlotSymbol.Seven ? DrawSeven() : LoadSymbol(symbol);

        try
        {
            var strips = new List<MagickImage>();
            var blurredStrips = new List<MagickImage>();
            for (var reel = 0; reel < SlotMachine.ReelCount; reel++)
            {
                var strip = BuildStrip(SlotMachine.Reels[reel], symbolImages);
                var blurred = (MagickImage)strip.Clone();
                blurred.MotionBlur(0, 9, 90);
                strips.Add(strip);
                blurredStrips.Add(blurred);
            }

            var scene = new Scene(BuildMachine(), BuildOverlay(), strips, blurredStrips);
            return new Assets(scene, BuildPalette(scene));
        }
        finally
        {
            foreach (var image in symbolImages.Values)
                image.Dispose();
        }
    }

    private MagickImage LoadSymbol(SlotSymbol symbol)
    {
        var image = new MagickImage($"{_assetsRoot}/slots/{symbol.ToString().ToLowerInvariant()}.png");
        image.Resize(SymbolSize, SymbolSize);
        return image;
    }

    private MagickImage DrawSeven()
    {
        var image = new MagickImage(MagickColors.Transparent, SymbolSize, SymbolSize);
        image.Settings.Font = $"{_assetsRoot}/fonts/ConsolasBold.ttf";
        image.Settings.FontPointsize = 62;
        image.Settings.FillColor = new MagickColor("#e3202c");
        image.Settings.StrokeColor = new MagickColor("#5c0008");
        image.Settings.StrokeWidth = 2.5;
        image.Annotate("7", Gravity.Center);
        return image;
    }

    /// <summary>One reel as a tall image: the symbols top to bottom by strip index, wrapped by padding rows on both ends.</summary>
    private static MagickImage BuildStrip(IReadOnlyList<SlotSymbol> symbols, Dictionary<SlotSymbol, MagickImage> symbolImages)
    {
        var rows = symbols.Count + 2 * StripPadding;
        var strip = new MagickImage(new MagickColor("#f4eedb"), ReelWidth, (uint)(rows * CellHeight));
        for (var row = 0; row < rows; row++)
        {
            var index = ((row - StripPadding) % symbols.Count + symbols.Count) % symbols.Count;
            strip.Composite(symbolImages[symbols[index]], (ReelWidth - SymbolSize) / 2, row * CellHeight + (CellHeight - SymbolSize) / 2, CompositeOperator.Over);
        }

        return strip;
    }

    /// <summary>The cabinet without the reels: background, gold border, reel gaps and the payline arrows.</summary>
    private static MagickImage BuildMachine()
    {
        var machine = new MagickImage("gradient:#3b2569-#150b2b", new MagickReadSettings { Width = Width, Height = Height });
        new Drawables()
            .StrokeColor(new MagickColor("#e7b93e"))
            .StrokeWidth(4)
            .FillColor(MagickColors.Transparent)
            .RoundRectangle(3, 3, Width - 4, Height - 4, 18, 18)
            .StrokeColor(new MagickColor("#8a6a1c"))
            .StrokeWidth(2)
            .FillColor(new MagickColor("#0e0818"))
            .RoundRectangle(ReelX(0) - 10, WindowTop - 8, ReelX(SlotMachine.ReelCount - 1) + ReelWidth + 10, WindowTop + WindowHeight + 8, 10, 10)
            .StrokeColor(new MagickColor("#5c0008"))
            .StrokeWidth(1.5)
            .FillColor(new MagickColor("#ff4d4d"))
            .Polygon(new PointD(9, PaylineY - 13), new PointD(9, PaylineY + 13), new PointD(ReelX(0) - 12, PaylineY))
            .Polygon(new PointD(Width - 9, PaylineY - 13), new PointD(Width - 9, PaylineY + 13), new PointD(ReelX(SlotMachine.ReelCount - 1) + ReelWidth + 12, PaylineY))
            .Draw(machine);
        return machine;
    }

    /// <summary>What sits above the reels: dark bezels at the top and bottom of each window and the red payline.</summary>
    private static MagickImage BuildOverlay()
    {
        var overlay = new MagickImage(MagickColors.Transparent, Width, Height);
        var drawables = new Drawables().StrokeColor(MagickColors.Transparent).FillColor(new MagickColor("#2a1d4d"));
        for (var reel = 0; reel < SlotMachine.ReelCount; reel++)
        {
            var x = ReelX(reel);
            drawables
                .Rectangle(x, WindowTop, x + ReelWidth - 1, WindowTop + 5)
                .Rectangle(x, WindowTop + WindowHeight - 6, x + ReelWidth - 1, WindowTop + WindowHeight - 1);
        }

        drawables
            .StrokeColor(new MagickColor("#e02020"))
            .StrokeWidth(3)
            .Line(ReelX(0), PaylineY, ReelX(SlotMachine.ReelCount - 1) + ReelWidth - 1, PaylineY)
            .Draw(overlay);
        return overlay;
    }

    /// <summary>
    /// One 256-color palette for every frame, built from sample frames of the machine at rest on each symbol and in
    /// motion. Every frame is then mapped to it without dithering, so colors do not shimmer between frames and the
    /// last frame is reproducible.
    /// </summary>
    private static PaletteMap BuildPalette(Scene scene)
    {
        using var samples = new MagickImageCollection();
        foreach (var symbol in Enum.GetValues<SlotSymbol>())
        {
            var stops = SlotMachine.Reels.Select(strip => Math.Max(0, strip.ToList().IndexOf(symbol))).ToArray();
            samples.Add(Compose(scene, stops, StopSeconds[^1]));
        }

        // Moments as shares of the spin's length, so every phase of the motion is sampled whatever the timing.
        var moving = new[] { 0, 0, 0 };
        foreach (var share in new[] { 0.04, 0.21, 0.375, 0.54, 0.625, 0.71, 0.83, 0.92 })
            samples.Add(Compose(scene, moving, share * StopSeconds[^1]));

        using var combined = samples.AppendVertically();
        combined.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.No });
        using var colors = combined.UniqueColors() ?? throw new InvalidOperationException("The slot art has no colors.");
        return new PaletteMap(colors.GetPixels().ToByteArray(PixelMapping.RGB)!);
    }

    /// <summary>
    /// Maps an image to a fixed palette through a lookup table indexed by the top 6 bits of each channel, so frames
    /// skip Magick's remap, which rebuilds its color search on every call.
    /// </summary>
    private sealed class PaletteMap
    {
        private const int Bits = 6;
        private const int Levels = 1 << Bits;
        private const int Shift = 8 - Bits;

        private readonly byte[] _colors;
        private readonly byte[] _nearest = new byte[Levels * Levels * Levels];

        /// <param name="colors">The palette as consecutive RGB triples, at most 256 colors.</param>
        public PaletteMap(byte[] colors)
        {
            _colors = colors;
            var count = colors.Length / 3;
            const int half = 1 << (Shift - 1);
            for (var r = 0; r < Levels; r++)
            for (var g = 0; g < Levels; g++)
            for (var b = 0; b < Levels; b++)
            {
                int cr = (r << Shift) | half, cg = (g << Shift) | half, cb = (b << Shift) | half;
                var best = 0;
                var bestDistance = int.MaxValue;
                for (var i = 0; i < count; i++)
                {
                    int dr = cr - colors[i * 3], dg = cg - colors[i * 3 + 1], db = cb - colors[i * 3 + 2];
                    var distance = dr * dr + dg * dg + db * db;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = i;
                }

                _nearest[(r << (2 * Bits)) | (g << Bits) | b] = (byte)best;
            }
        }

        public MagickImage Apply(IMagickImage<byte> image)
        {
            var pixels = image.GetPixels().ToByteArray(PixelMapping.RGB)!;
            for (var i = 0; i < pixels.Length; i += 3)
            {
                var color = _nearest[((pixels[i] >> Shift) << (2 * Bits)) | ((pixels[i + 1] >> Shift) << Bits) | (pixels[i + 2] >> Shift)] * 3;
                pixels[i] = _colors[color];
                pixels[i + 1] = _colors[color + 1];
                pixels[i + 2] = _colors[color + 2];
            }

            return new MagickImage(pixels, new PixelReadSettings(image.Width, image.Height, StorageType.Char, PixelMapping.RGB));
        }
    }

    private sealed record Scene(MagickImage Machine, MagickImage Overlay, List<MagickImage> Strips, List<MagickImage> BlurredStrips);

    private sealed record Assets(Scene Scene, PaletteMap Palette);
}
