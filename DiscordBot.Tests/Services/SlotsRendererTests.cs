using System.Diagnostics;
using DiscordBot.Domain;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Settings;
using ImageMagick;

namespace DiscordBot.Tests.Services;

public class SlotsRendererTests
{
    private const int MaxGifBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan MaxRenderTime = TimeSpan.FromSeconds(1);

    private static readonly SlotsRenderer Renderer = new(new BotSettings
    {
        AssetsRootPath = Path.Combine(AppContext.BaseDirectory, "Assets")
    });

    /// <summary>A result whose payline shows the given symbols, stopped on the first occurrence of each in its reel.</summary>
    private static SlotResult ResultFor(params SlotSymbol[] line)
    {
        var stops = line.Select((symbol, reel) => SlotMachine.Reels[reel].ToList().IndexOf(symbol)).ToArray();
        var (multiplier, isJackpot) = SlotMachine.Evaluate(line);
        return new SlotResult(stops, line, multiplier, isJackpot, false, false);
    }

    private static readonly (string Name, SlotResult Result)[] Samples =
    [
        ("loss", ResultFor(SlotSymbol.Lemon, SlotSymbol.Bell, SlotSymbol.Grape)),
        ("cherry-win", ResultFor(SlotSymbol.Cherry, SlotSymbol.Grape, SlotSymbol.Star)),
        ("seven-seven-seven", ResultFor(SlotSymbol.Seven, SlotSymbol.Seven, SlotSymbol.Seven)),
        ("diamond-jackpot", ResultFor(SlotSymbol.Diamond, SlotSymbol.Diamond, SlotSymbol.Diamond))
    ];

    public static IEnumerable<object[]> SampleNames => Samples.Select(s => new object[] { s.Name });

    private static SlotResult Sample(string name) => Samples.Single(s => s.Name == name).Result;

    private static byte[] Pixels(IMagickImage<byte> image) => image.GetPixels().ToByteArray(PixelMapping.RGB)!;

    #region AC13

    [Theory]
    [MemberData(nameof(SampleNames))]
    public async Task Spin_WeighsUnder2MbAndRendersUnder1Second(string name)
    {
        await Renderer.RenderSpinAsync(Sample(name)); // the first render loads the art and builds the strips

        var watch = Stopwatch.StartNew();
        using var gif = await Renderer.RenderSpinAsync(Sample(name));
        watch.Stop();

        Assert.True(gif.Length < MaxGifBytes, $"{name}: {gif.Length} bytes");
        Assert.True(watch.Elapsed < MaxRenderTime, $"{name}: {watch.ElapsedMilliseconds} ms");
    }

    [Theory]
    [MemberData(nameof(SampleNames))]
    public async Task Spin_LastFrameIsTheStaticRender(string name)
    {
        var result = Sample(name);
        using var gif = await Renderer.RenderSpinAsync(result);
        using var png = await Renderer.RenderStaticAsync(result);

        using var frames = new MagickImageCollection(gif);
        frames.Coalesce();
        using var staticImage = new MagickImage(png);

        Assert.True(frames.Count > 2);
        Assert.Equal(Pixels(staticImage), Pixels(frames[^1]));
    }

    [Fact]
    public async Task Spin_PlaysOnceAndAnimatesBeforeTheLastFrame()
    {
        using var gif = await Renderer.RenderSpinAsync(Sample("loss"));
        using var frames = new MagickImageCollection(gif);
        frames.Coalesce();

        Assert.Equal(1u, frames[0].AnimationIterations);
        Assert.NotEqual(Pixels(frames[0]), Pixels(frames[1]));
        Assert.True(frames[^1].AnimationDelay > 100u, "the result stays on screen");
    }

    [Fact]
    public async Task Static_DependsOnTheStopsOnly()
    {
        using var first = await Renderer.RenderStaticAsync(Sample("loss"));
        using var second = await Renderer.RenderStaticAsync(Sample("loss"));
        using var other = await Renderer.RenderStaticAsync(Sample("diamond-jackpot"));

        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.NotEqual(first.ToArray(), other.ToArray());
    }

    #endregion

    /// <summary>Writes the sample GIFs the plan asks to look at; not an assertion.</summary>
    [Fact]
    public async Task WritesSamples()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "slots-samples");
        Directory.CreateDirectory(folder);
        foreach (var (name, result) in Samples)
        {
            await using (var gif = File.Create(Path.Combine(folder, $"{name}.gif")))
            {
                using var stream = await Renderer.RenderSpinAsync(result);
                await stream.CopyToAsync(gif);
            }

            await using var png = File.Create(Path.Combine(folder, $"{name}.png"));
            using var staticStream = await Renderer.RenderStaticAsync(result);
            await staticStream.CopyToAsync(png);
        }

        Assert.True(Directory.GetFiles(folder, "*.gif").Length >= Samples.Length);
    }
}
