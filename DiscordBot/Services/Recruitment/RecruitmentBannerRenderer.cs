using DiscordBot.Services.Rendering;
using DiscordBot.Settings.Options;
using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Recruitment;

public interface IRecruitmentBannerRenderer
{
    Task<byte[]> RenderAsync(RecruitmentPostRecord post, RecruitmentPolicyEvaluator policy, CancellationToken token);
}

public sealed class RecruitmentBannerRenderer(ImageRenderOptions options) : IRecruitmentBannerRenderer
{
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    public const int MaximumBytes = 512 * 1024;

    public async Task<byte[]> RenderAsync(RecruitmentPostRecord post, RecruitmentPolicyEvaluator policy, CancellationToken token)
    {
        await _renderGate.WaitAsync(token);
        try
        {
            // Process-wide native limits are configured at startup, never by a banner render.
            string font = new BundledFontResolver(options.AssetsRootPath).Resolve("Open Sans");
            using var image = new MagickImage(new MagickColor("#14232D"), 900, 360);
            new Drawables().FillColor(new MagickColor("#48C8CB")).Rectangle(0, 0, 12, 360).Draw(image);
            DrawLine(image, font, "RECRUITMENT  /  INITIAL SNAPSHOT", 30, 47, 20, "#48C8CB");
            DrawLine(image, font, FitTitle(font, post.Title), 30, 91, 32, "#FFFFFF");
            DrawLine(image, font, $"Account age: {policy.AccountAge(SnowflakeUtils.FromSnowflake(post.AuthorId))}", 30, 143, 28, "#E0E8ED");
            DrawLine(image, font, $"Server tenure: {policy.ServerTenure(post.Observation.JoinedAtUtc)}", 455, 143, 28, "#E0E8ED");
            DrawLine(image, font, $"Activity: {RecruitmentAdvisoryMessage.ActivityText(post.Activity)}", 30, 185, 30, "#E0E8ED");
            string payment = PaymentSnapshot(post.Payment);
            DrawLine(image, font, payment, 30, 227, 30, "#E0E8ED");
            DrawLine(image, font, "Keep initial terms and verification public.", 30, 289, 24, "#FFFFFF");
            DrawLine(image, font, "Context only. Read the live message below for status and controls.", 30, 326, 19, "#B4C7D2");
            image.Strip();
            image.Format = MagickFormat.Png;
            image.Depth = 8;
            byte[] bytes = image.ToByteArray();
            if (bytes.Length > MaximumBytes) throw new InvalidOperationException("Recruitment banner exceeds its output limit.");
            token.ThrowIfCancellationRequested();
            return bytes;
        }
        finally { _renderGate.Release(); }
    }

    private static void DrawLine(MagickImage image, string font, string text, int x, int y, int size, string color) =>
        new Drawables().Font(font).FontPointSize(size).FillColor(new MagickColor(color)).Text(x, y, text).Draw(image);

    private static string PaymentSnapshot(RecruitmentPaymentSignal payment) => payment switch
    {
        RecruitmentPaymentSignal.Concrete => "Payment: amount detected; verify terms",
        RecruitmentPaymentSignal.Missing => "Payment: add a currency and rate or budget",
        RecruitmentPaymentSignal.Ambiguous => "Payment: clarify guaranteed pay and scope",
        RecruitmentPaymentSignal.NotApplicable => "Unpaid collaboration: agree on expectations",
        _ => "Payment details: unknown"
    };

    private static string FitTitle(string font, string text)
    {
        // Bound input first, then measure pixels: wide glyphs can overflow a character-count limit.
        int[] starts = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        string bounded = starts.Length <= 80 ? text : text[..starts[80]];
        bounded = bounded.Replace('\n', ' ').Replace('\r', ' ');
        var measure = new Drawables().Font(font).FontPointSize(32);
        starts = System.Globalization.StringInfo.ParseCombiningCharacters(bounded);
        for (int count = starts.Length; count > 0; count--)
        {
            string candidate = count == starts.Length ? bounded : bounded[..starts[count]] + "…";
            if (measure.FontTypeMetrics(candidate)!.TextWidth <= 835) return candidate;
        }
        return "Recruitment listing";
    }
}
