using Discord;
using DiscordBot.Services.Rendering;
using ImageMagick;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Rendering;

[TestClass]
public sealed class ProfileCardRendererTests
{
    private const double BaselineRootMeanSquaredTolerance = 0.08;

    private static string AssetsRootPath => Path.Combine(AppContext.BaseDirectory, "Assets");

    [TestMethod]
    public void Render_DefaultAvatar_MatchesV7BaselineWithinTolerance()
    {
        var bytes = CreateRenderer().Render(CreateRequest());
        using var actual = new MagickImage(bytes);

        Assert.AreEqual(500, actual.Width);
        Assert.AreEqual(200, actual.Height);
        Assert.IsTrue(actual.HasAlpha, "The profile PNG should retain an alpha channel.");
        Assert.IsGreaterThan(25_000, bytes.Length);
        Assert.IsLessThan(500_000, bytes.Length);

        var baselinePath = Path.Combine(AppContext.BaseDirectory, "Rendering", "Fixtures",
            "profile-card-v7-baseline.png");

        if (System.Environment.GetEnvironmentVariable("UPDATE_PROFILE_BASELINE") == "1")
        {
            var sourcePath = Path.Combine(GetRepositoryRoot(), "DiscordBot.Tests", "Rendering", "Fixtures",
                "profile-card-v7-baseline.png");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllBytes(sourcePath, bytes);
            return;
        }

        Assert.IsTrue(File.Exists(baselinePath),
            "The v7 baseline is missing. Regenerate it explicitly with UPDATE_PROFILE_BASELINE=1.");

        using var baseline = new MagickImage(baselinePath);
        var error = actual.Compare(baseline, ErrorMetric.RootMeanSquared);
        Assert.IsLessThanOrEqualTo(BaselineRootMeanSquaredTolerance, error,
            $"Profile image RMS error {error:F6} exceeded the approved tolerance.");
    }

    [TestMethod]
    public void Render_UsesDownloadedAvatarBytes()
    {
        var renderer = CreateRenderer();
        var defaultAvatar = renderer.Render(CreateRequest());
        var customAvatar = renderer.Render(CreateRequest() with
        {
            AvatarBytes = File.ReadAllBytes(Path.Combine(AssetsRootPath, "images", "triangle.png"))
        });

        using var defaultImage = new MagickImage(defaultAvatar);
        using var customImage = new MagickImage(customAvatar);
        Assert.IsGreaterThan(0, defaultImage.Compare(customImage, ErrorMetric.RootMeanSquared));
    }

    [TestMethod]
    public void Render_LongNickname_RemainsWithinOutputBounds()
    {
        var bytes = CreateRenderer().Render(CreateRequest() with
        {
            Nickname = "A deliberately very long profile nickname that must not resize the canvas"
        });

        using var image = new MagickImage(bytes);
        Assert.AreEqual(500, image.Width);
        Assert.AreEqual(200, image.Height);
    }

    [TestMethod]
    public void Render_ZeroAndFullXp_ProduceDifferentBars()
    {
        var renderer = CreateRenderer();
        var empty = renderer.Render(CreateRequest() with { XpPercentage = 0, XpShown = 0 });
        var full = renderer.Render(CreateRequest() with { XpPercentage = 1, XpShown = 1_000 });

        using var emptyImage = new MagickImage(empty);
        using var fullImage = new MagickImage(full);
        Assert.IsGreaterThan(0, emptyImage.Compare(fullImage, ErrorMetric.RootMeanSquared));
    }

    [TestMethod]
    public void Render_RoleColor_ChangesAvatarBorder()
    {
        var renderer = CreateRenderer();
        var red = renderer.Render(CreateRequest() with { MainRoleColor = new Color(255, 0, 0) });
        var blue = renderer.Render(CreateRequest() with { MainRoleColor = new Color(0, 0, 255) });

        using var redImage = new MagickImage(red);
        using var blueImage = new MagickImage(blue);
        Assert.IsGreaterThan(0, redImage.Compare(blueImage, ErrorMetric.RootMeanSquared));
    }

    [TestMethod]
    public void Render_MissingAssets_ThrowsWithoutPoisoningNextRender()
    {
        var missingRenderer = new ProfileCardRenderer(
            new ImageRenderOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        Assert.Throws<IOException>(() => missingRenderer.Render(CreateRequest()));

        var bytes = CreateRenderer().Render(CreateRequest());
        Assert.IsGreaterThan(0, bytes.Length);
    }

    private static ProfileCardRenderer CreateRenderer() =>
        new(new ImageRenderOptions(AssetsRootPath));

    internal static ProfileCardRenderRequest CreateRequest() => new()
    {
        UserId = 123456789,
        Username = "baseline-user",
        Nickname = "Baseline Nickname",
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

    private static string GetRepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
