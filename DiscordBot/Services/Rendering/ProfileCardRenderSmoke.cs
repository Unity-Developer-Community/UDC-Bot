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
            var bytes = renderer.Render(new ProfileCardRenderRequest
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
            });

            using var image = new MagickImage(bytes);
            if (image.Width != 500 || image.Height != 200 || !image.HasAlpha)
                throw new InvalidOperationException(
                    $"Unexpected smoke-test output: {image.Width}x{image.Height}, alpha={image.HasAlpha}.");

            if (!string.IsNullOrWhiteSpace(outputPath))
                File.WriteAllBytes(outputPath, bytes);

            Console.WriteLine($"Profile render smoke passed: {image.Width}x{image.Height}, {bytes.Length} bytes.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Profile render smoke failed: {exception}");
            return 1;
        }
    }
}
