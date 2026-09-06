using DiscordBot.Services.Recruitment.Presentation;
using DiscordBot.Services.Rendering;
using DiscordBot.Tests.Rendering;
using ImageMagick;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Recruitment;

[TestClass]
public sealed class RecruitmentRenderingTests
{
    [TestMethod]
    public async Task MixedProfileAndBannerRenders_StayBoundedAndDoNotChangeNativeLimits()
    {
        var options = new ImageRenderOptions(Path.Combine(AppContext.BaseDirectory, "Assets"));
        var profiles = new ProfileCardRenderer(options);
        var banners = new BannerRenderer(options);
        var originalLimits = (ResourceLimits.Memory, ResourceLimits.Thread, ResourceLimits.Time);
        await Parallel.ForEachAsync(Enumerable.Range(0, 48), new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (index, token) =>
        {
            byte[] bytes;
            if (index % 2 == 0)
            {
                var post = RecruitmentTestData.Post();
                post.Title = new string('W', 200) + " · 👩🏽‍💻 e\u0301";
                bytes = await banners.RenderAsync(post, RecruitmentTestData.Policy(), token);
            }
            else bytes = profiles.Render(ProfileCardRendererTests.CreateRequest());
            using var image = new MagickImage(bytes);
            Assert.AreEqual(index % 2 == 0 ? 900u : 500u, image.Width);
            Assert.AreEqual(index % 2 == 0 ? 360u : 200u, image.Height);
            Assert.AreEqual(MagickFormat.Png, image.Format);
            Assert.AreEqual(0, image.ProfileNames.Count());
            Assert.IsTrue(bytes.Length <= BannerRenderer.MaximumBytes);
        });
        Assert.AreEqual(originalLimits, (ResourceLimits.Memory, ResourceLimits.Thread, ResourceLimits.Time));
    }

    [TestMethod]
    public async Task MissingFontAndCancellation_DoNotPoisonLaterBanners()
    {
        var missing = new BannerRenderer(new ImageRenderOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        await Assert.ThrowsAsync<IOException>(() => missing.RenderAsync(RecruitmentTestData.Post(), RecruitmentTestData.Policy(), default));
        var renderer = new BannerRenderer(new ImageRenderOptions(Path.Combine(AppContext.BaseDirectory, "Assets")));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => renderer.RenderAsync(RecruitmentTestData.Post(), RecruitmentTestData.Policy(), cancellation.Token));
        Assert.IsTrue((await renderer.RenderAsync(RecruitmentTestData.Post(), RecruitmentTestData.Policy(), default)).Length > 0);
    }
}
