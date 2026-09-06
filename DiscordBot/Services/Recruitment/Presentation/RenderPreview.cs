using System.IO;
using DiscordBot.Services.Recruitment.Publishing;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Services.Rendering;
using DiscordBot.Settings.Options;
using ImageMagick;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment.Presentation;

/// <summary>Offline packaging check and a reproducible review artifact; never connects to Discord.</summary>
public static class RenderPreview
{
    public static async Task<int> RunAsync(string assetsRoot, string? outputPath)
    {
        try
        {
            var renderOptions = new ImageRenderOptions(assetsRoot);
            renderOptions.Validate();
            MagickResourcePolicy.Configure(renderOptions);
            // Synthetic IDs satisfy policy validation; this path has no Discord client.
            var options = new RecruitmentOptions
            {
                FeedChannelId = 5,
                Forums = new()
                {
                    PaidRecruiting = new() { ChannelId = 1 }, PaidForHire = new() { ChannelId = 2 },
                    HobbyRecruiting = new() { ChannelId = 3 }, HobbyForHire = new() { ChannelId = 4 }
                }
            };
            new GuidelineTemplates(Options.Create(options), Options.Create(new StorageOptions { AssetsRootPath = assetsRoot })).ValidateAll();
            var post = new PostRecord
            {
                ThreadId = 1, AuthorId = SnowflakeUtils.ToSnowflake(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                Title = "Gameplay programmer · paid contract",
                CreatedAtUtc = DateTimeOffset.UtcNow, Activity = ActivityStatus.Unknown,
                Payment = PaymentSignal.Concrete,
                Observation = new() { JoinedAtUtc = DateTimeOffset.UtcNow.AddMonths(-8) }
            };
            var renderer = new BannerRenderer(renderOptions);
            byte[] bytes = await renderer.RenderAsync(post, new(options, TimeProvider.System), CancellationToken.None);
            using var image = new MagickImage(bytes);
            if (image.Width != 900 || image.Height != 360 || image.Format != MagickFormat.Png || bytes.Length > BannerRenderer.MaximumBytes)
                throw new InvalidOperationException("Recruitment preview exceeded its image contract.");
            if (outputPath is not null) await File.WriteAllBytesAsync(outputPath, bytes);
            Console.WriteLine($"Recruitment preview passed: four templates validated; 900x360 PNG, {bytes.Length} bytes.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Recruitment preview failed: {error}");
            return 1;
        }
    }
}
