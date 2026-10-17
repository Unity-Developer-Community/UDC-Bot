using DiscordBot.Services.Recruitment;
using ImageMagick;
using Newtonsoft.Json;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentBannerTests
{
    [Fact]
    public async Task NineValuesMediaAndFallbackKeepOperationsAndPrivateEvidenceSeparate()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(forum: "12");
        post.Facts.ServerNickname = "**<@123> Name**"; post.Facts.Username = "user";
        post.Facts.AvatarUrl = "https://cdn.discordapp.com/avatars/123/avatar.png?ex=123";
        post.OwnerId = "18446744073709551615";
        post.Hold = "Recovery";
        post.Facts.Observations = new() { new("fee", "Applicant fee", "pay $50 today") };
        var message = RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now);
        Assert.Equal(RecruitmentMessageFormat.ComponentsV2, message.Format);
        Assert.Null(message.Embed); Assert.Equal("", message.Content);
        Assert.NotNull(message.Media); Assert.InRange(message.Media.Description.Length, 1, 1024);
        foreach (var c in post.Facts.Checks) Assert.Contains(c.Label + ": " + c.Value, message.Media.Description);
        Assert.InRange(RecruitmentBanner.GalleryAlt(post.Facts.Checks).Length, 1, 256);
        foreach (var c in post.Facts.Checks) Assert.Contains(c.Value, RecruitmentBanner.GalleryAlt(post.Facts.Checks));
        var json = JsonConvert.SerializeObject(message.Components);
        Assert.Contains("Legend", json); Assert.Contains("recruit:details:100", json);
        Assert.Contains("Acknowledge", json); Assert.Contains("Due", json); Assert.Contains("paused", json);
        Assert.Contains("Applicant fee", json); Assert.Contains("18446744073709551615", json);
        Assert.DoesNotContain("<@123>", json); Assert.Contains("\"Type\":11", json);
        var fallback = JsonConvert.SerializeObject(message.FallbackComponents);
        Assert.DoesNotContain("attachment://", fallback);
        foreach (var c in post.Facts.Checks) Assert.Contains(c.Value, fallback);
        post.Facts.AvatarUrl = "https://cdn.discordapp.com/avatars/123/avatar.png?ex=456";
        Assert.Equal(message.RenderKey, RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now).RenderKey);
        rig.State.PortfolioSites.Version++;
        Assert.NotEqual(message.RenderKey, RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now).RenderKey);
        var failed = RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now,
            c => RecruitmentBanner.Render(c, "/tmp/nonexistent-recruitment-font.ttf"));
        Assert.Null(failed.Media); Assert.DoesNotContain("attachment://", JsonConvert.SerializeObject(failed.Components));
        post.Facts.ServerNickname = " "; post.Facts.GlobalName = "Global Name";
        post.Facts.AvatarUrl = null; post.AvailabilityUnknown = true;
        var unknown = JsonConvert.SerializeObject(RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now).Components);
        Assert.Contains("Global Name", unknown); Assert.Contains("Avatar unavailable", unknown); Assert.Contains("last observed", unknown);
        post.Facts.GlobalName = null; post.AcceptedAt = rig.Clock.Now;
        var accepted = JsonConvert.SerializeObject(RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now).Components);
        Assert.Contains("**user**", accepted); Assert.DoesNotContain("recruit:ack:", accepted);
        post.Status = ListingStatus.Closed; post.LastOutcome = "Closed by author";
        var closed = JsonConvert.SerializeObject(RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now).Components);
        Assert.Contains("Closed by author", closed); Assert.DoesNotContain("recruit:ack:", closed);
        Assert.DoesNotContain("reuse", RecruitmentPresentation.Details(post), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MediaDigestChangesWithBytesAndNamesDoNotChangeImageIdentity()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        var first = RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now, _ => new byte[] { 1 });
        var changed = RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now, _ => new byte[] { 2 });
        Assert.NotEqual(first.Media!.FileName, changed.Media!.FileName); Assert.NotEqual(first.RenderKey, changed.RenderKey);
        post.Facts.ServerNickname = "New name";
        var renamed = RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now, _ => new byte[] { 1 });
        Assert.Equal(first.Media.FileName, renamed.Media!.FileName); Assert.NotEqual(first.RenderKey, renamed.RenderKey);
    }

    [Fact]
    public async Task RenderIsBoundedDeterministicAndReadableWithoutColor()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post(forum: "12");
        var checks = post.Facts.Checks.Select((c,i) => c with {
            Severity = (ObservationSeverity)(i % 4),
            Applicability = i == 3 ? ObservationApplicability.NotApplicable : i == 8 ? ObservationApplicability.Unavailable : ObservationApplicability.Available,
            Value = i == 3 ? "Not Applicable" : i == 8 ? "Unavailable" : c.Value }).ToArray();
        var bytes = RecruitmentBanner.Render(checks);
        Assert.Equal(bytes, RecruitmentBanner.Render(checks));
        using var image = new MagickImage(bytes);
        Assert.Equal(MagickFormat.Png, image.Format);
        Assert.Equal((uint)RecruitmentBanner.Width, image.Width); Assert.Equal((uint)RecruitmentBanner.Height, image.Height);
        Assert.InRange(bytes.Length, 1000, 500000);
        Assert.Throws<ArgumentException>(() => RecruitmentBanner.Render(checks.Take(8).ToArray()));
        var longChecks = checks.Select(c => c with { Value = new string('x', 500) }).ToArray();
        Assert.NotEmpty(RecruitmentBanner.Render(longChecks));
        // Local review fixtures, intentionally outside repository discovery.
        Directory.CreateDirectory("/tmp/udc-t04-previews");
        image.Write("/tmp/udc-t04-previews/grid.png");
        foreach (var width in new uint[] { 320, 360, 640 })
        foreach (var light in new[] { false, true })
        foreach (var gray in new[] { false, true })
        {
            using var preview = image.Clone(); preview.Resize(width, 0);
            if (gray) preview.ColorSpace = ColorSpace.Gray;
            preview.BackgroundColor = light ? MagickColors.White : new MagickColor("#313338");
            preview.Extent(width + 24, preview.Height + 24, Gravity.Center);
            preview.Write($"/tmp/udc-t04-previews/{width}-{(light ? "light" : "dark")}-{(gray ? "gray" : "color")}.png");
        }
    }
}
