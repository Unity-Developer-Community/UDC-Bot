using ImageMagick;
using ImageMagick.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DiscordBot.Services.Recruitment;

/// <summary>Offline, bounded renderer. No author identifiers or downloaded content enter the image.</summary>
public static class RecruitmentBanner
{
    public const string Version = "observations-grid-2";
    public const int Width = 1200, Height = 840;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, byte[]> Cache = new();
    public static string Identity(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Status(RecruitmentObservation c) => c.Applicability != ObservationApplicability.Available ? "Grey" : c.Severity.ToString();
    public static string Alt(IReadOnlyList<RecruitmentObservation> checks) => string.Join("; ", checks.Select(c => $"{c.Label}: {RecruitmentPresentation.Safe(c.Value, 60)} ({Status(c)})"));
    public static string GalleryAlt(IReadOnlyList<RecruitmentObservation> checks)
    {
        var labels = new[] { "Age", "Joined", "Roles", "Pay", "Duration", "Portfolio", "DM", "Avatar", "Detail" };
        var text = string.Join("; ", checks.Select((c, i) => $"{labels[i]}: {RecruitmentPresentation.Safe(c.Value, 60)}"));
        // The installed Discord.Net gallery builder caps descriptions at 256. The attachment
        // description and Legend retain full labels/statuses even for unexpected long values.
        return text.Length <= 256 ? text : "Nine automated observations. Complete values and explanations: Legend.";
    }
    public static byte[] Render(IReadOnlyList<RecruitmentObservation> checks, string? fontPath = null)
    {
        if (checks.Count != 9) throw new ArgumentException("The banner requires nine observations.", nameof(checks));
        fontPath ??= Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "OpenSans-Regular.ttf");
        if (!File.Exists(fontPath)) throw new FileNotFoundException("Recruitment banner font unavailable.", fontPath);
        var key = Identity(Version + fontPath + Alt(checks));
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached.ToArray();
            using var image = new MagickImage(new MagickColor("#20242B"), Width, Height);
            for (var i = 0; i < 9; i++)
            {
                var c = checks[i]; var x = i % 3 * 400 + 12; var y = i / 3 * 280 + 12;
                var status = Status(c);
                var color = status switch { "Green" => "#66D5A0", "Yellow" => "#F1CD68", "Red" => "#FF9494", _ => "#BAC2CE" };
                var d = new Drawables().FillColor(new MagickColor("#2C323B")).Rectangle(x, y, x + 376, y + 256)
                    .Font(fontPath).FontPointSize(40).FillColor(MagickColors.White).Text(x + 16, y + 48, c.Label)
                    .FillColor(new MagickColor(color));
                // Shapes and explicit words preserve meaning without color.
                if (status == "Green") d.Circle(x + 26, y + 229, x + 36, y + 229);
                else if (status == "Yellow") d.Polygon(new PointD(x + 16, y + 239), new PointD(x + 26, y + 217), new PointD(x + 36, y + 239));
                else if (status == "Red") d.Rectangle(x + 16, y + 219, x + 36, y + 239);
                else d.FillColor(MagickColors.Transparent).StrokeColor(new MagickColor(color)).StrokeWidth(3).Circle(x + 26, y + 229, x + 36, y + 229).StrokeWidth(0);
                d.FillColor(MagickColors.White).FontPointSize(40).Text(x + 48, y + 240, status);
                var words = RecruitmentPresentation.Safe(c.Value, 60).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var lines = new List<string>(); var line = "";
                foreach (var word in words) { if (line.Length + word.Length > 16 && line.Length > 0) { lines.Add(line); line = ""; } line += (line.Length > 0 ? " " : "") + word; }
                lines.Add(line);
                d.FontPointSize(40);
                for (var n = 0; n < Math.Min(3, lines.Count); n++) d.Text(x + 16, y + 100 + n * 44, lines[n].Length > 17 ? lines[n][..16] + "…" : lines[n]);
                d.Draw(image);
            }
            image.Format = MagickFormat.Png; image.Strip();
            var bytes = image.ToByteArray();
            if (Cache.Count >= 32) Cache.Clear();
            Cache[key] = bytes;
            return bytes.ToArray();
        }
    }
}
