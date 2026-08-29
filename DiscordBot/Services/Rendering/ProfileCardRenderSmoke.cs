using System.Diagnostics;
using System.IO;
using ImageMagick;

namespace DiscordBot.Services.Rendering;

public static class ProfileCardRenderSmoke
{
    public static int Run(string assetsRootPath, string? outputPath = null)
    {
        try
        {
            var renderer = new ProfileCardRenderer(new ImageRenderOptions(assetsRootPath));
            var bytes = renderer.Render(CreateRequest());
            ValidateOutput(bytes);

            if (!string.IsNullOrWhiteSpace(outputPath))
                File.WriteAllBytes(outputPath, bytes);

            Console.WriteLine($"Profile render smoke passed: 500x200, {bytes.Length} bytes.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Profile render smoke failed: {exception}");
            return 1;
        }
    }

    public static int RunStress(string assetsRootPath, int iterations, int parallelism)
    {
        try
        {
            if (iterations is <= 0 or > 10_000)
                throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be between 1 and 10,000.");
            if (parallelism is <= 0 or > 64)
                throw new ArgumentOutOfRangeException(nameof(parallelism), "Parallelism must be between 1 and 64.");

            var renderer = new ProfileCardRenderer(new ImageRenderOptions(assetsRootPath));
            var stopwatch = Stopwatch.StartNew();
            long totalBytes = 0;
            Parallel.For(0, iterations, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, _ =>
            {
                var bytes = renderer.Render(CreateRequest());
                ValidateOutput(bytes);
                Interlocked.Add(ref totalBytes, bytes.Length);
            });
            stopwatch.Stop();

            Console.WriteLine(
                $"Profile render stress passed: {iterations} renders at parallelism {parallelism}, " +
                $"{totalBytes} bytes in {stopwatch.Elapsed.TotalSeconds:F2}s.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Profile render stress failed: {exception}");
            return 1;
        }
    }

    private static ProfileCardRenderRequest CreateRequest() => new()
    {
        UserId = 1,
        Username = "render-smoke",
        Nickname = "Render Smoke",
        XpTotal = 12_345,
        XpRank = 42,
        KarmaRank = 17,
        Karma = 321,
        Level = 27,
        XpLow = 10_000,
        XpHigh = 11_000,
        XpShown = 420,
        MaxXpShown = 1_000,
        XpPercentage = 0.42f,
        MainRoleColor = new Color(52, 152, 219)
    };

    private static void ValidateOutput(byte[] bytes)
    {
        using var image = new MagickImage(bytes);
        if (image.Width != 500 || image.Height != 200 || !image.HasAlpha || image.Format != MagickFormat.Png)
        {
            throw new InvalidOperationException(
                $"Unexpected smoke-test output: {image.Width}x{image.Height}, {image.Format}, alpha={image.HasAlpha}.");
        }
    }
}
