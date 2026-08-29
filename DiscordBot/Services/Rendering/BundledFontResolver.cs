using System.IO;

namespace DiscordBot.Services.Rendering;

public sealed class BundledFontResolver
{
    private static readonly IReadOnlyDictionary<string, string> FontFiles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Consolas"] = "Consolas.ttf",
            ["Roboto Mono"] = "Consolas.ttf",
            ["Consolas Bold"] = "ConsolasBold.ttf",
            ["Georgia"] = "georgia.ttf",
            ["Open Sans"] = "OpenSans-Regular.ttf",
            ["Open Sans Emoji"] = "OpenSansEmoji.ttf"
        };

    private readonly string _fontDirectory;

    public BundledFontResolver(string assetsRootPath)
    {
        _fontDirectory = Path.Combine(assetsRootPath, "fonts");
    }

    public string Resolve(string fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
            throw new InvalidOperationException("A profile skin requested an empty font name.");

        if (!FontFiles.TryGetValue(fontName, out var fileName))
            throw new InvalidOperationException($"Profile skin font '{fontName}' is not mapped to a bundled font.");

        var path = Path.Combine(_fontDirectory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Bundled profile font '{fontName}' was not found.", path);

        return path;
    }
}
