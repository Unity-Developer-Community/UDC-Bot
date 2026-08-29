using System.IO;
using DiscordBot.Skin;
using ImageMagick;
using Newtonsoft.Json;

namespace DiscordBot.Services.Rendering;

public sealed class ProfileCardRenderer : IProfileCardRenderer
{
    private readonly ImageRenderOptions _options;
    private readonly BundledFontResolver _fontResolver;

    public ProfileCardRenderer(ImageRenderOptions options)
    {
        _options = options;
        _fontResolver = new BundledFontResolver(options.AssetsRootPath);
    }

    public byte[] Render(ProfileCardRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var skin = LoadSkin();
        using var avatar = LoadAvatar(request.AvatarBytes);
        var avatarSize = ToPositiveDimension(skin.AvatarSize, nameof(skin.AvatarSize));
        avatar.Resize(avatarSize, avatarSize);

        var renderRequest = request with { AvatarSampleColor = SampleColor(avatar) };
        using var background = new MagickImage(GetSkinAssetPath(skin.Background));

        ValidateOutputDimensions(background.Width, background.Height);

        foreach (var layer in skin.Layers)
        {
            CompositeLayerImage(background, avatar, layer);

            if (layer.Modules.Count == 0)
                continue;

            var layerWidth = ToPositiveDimension(layer.Width, nameof(layer.Width));
            var layerHeight = ToPositiveDimension(layer.Height, nameof(layer.Height));
            using var moduleLayer = new MagickImage(MagickColors.Transparent, layerWidth, layerHeight);

            foreach (var module in layer.Modules)
            {
                if (module is BaseTextSkinModule textModule)
                    textModule.Font = _fontResolver.Resolve(textModule.Font);

                module.GetDrawables(renderRequest).Draw(moduleLayer);
            }

            background.Composite(moduleLayer, ToCoordinate(layer.StartX), ToCoordinate(layer.StartY),
                CompositeOperator.Over);
        }

        var result = background.ToByteArray(MagickFormat.Png32);
        if (result.Length > _options.MaximumOutputBytes)
            throw new InvalidOperationException(
                $"Rendered profile card is {result.Length} bytes; the limit is {_options.MaximumOutputBytes} bytes.");

        return result;
    }

    private SkinData LoadSkin()
    {
        var path = GetSkinAssetPath(_options.SkinFileName);
        var skin = JsonConvert.DeserializeObject<SkinData>(File.ReadAllText(path), new SkinModuleJsonConverter());
        return skin ?? throw new InvalidOperationException($"Profile skin '{path}' could not be deserialized.");
    }

    private MagickImage LoadAvatar(byte[]? avatarBytes)
    {
        if (avatarBytes is { Length: > 0 })
            return new MagickImage(avatarBytes);

        return new MagickImage(Path.Combine(_options.AssetsRootPath, "images", _options.DefaultAvatarFileName));
    }

    private void CompositeLayerImage(MagickImage background, MagickImage avatar, SkinLayer layer)
    {
        if (string.IsNullOrWhiteSpace(layer.Image))
            return;

        var x = ToCoordinate(layer.StartX);
        var y = ToCoordinate(layer.StartY);
        if (layer.Image.Equals("avatar", StringComparison.OrdinalIgnoreCase))
        {
            background.Composite(avatar, x, y, CompositeOperator.Over);
            return;
        }

        using var image = new MagickImage(GetSkinAssetPath(layer.Image));
        background.Composite(image, x, y, CompositeOperator.Over);
    }

    private static Color SampleColor(MagickImage avatar)
    {
        using var sample = new MagickImage(avatar);
        sample.Resize(1, 1);
        using var pixels = sample.GetPixels();
        var color = pixels[0, 0].ToColor();
        return new Color(color.R, color.G, color.B);
    }

    private void ValidateOutputDimensions(uint width, uint height)
    {
        var maximumWidth = ToPositiveDimension(_options.MaximumOutputWidth, nameof(_options.MaximumOutputWidth));
        var maximumHeight = ToPositiveDimension(_options.MaximumOutputHeight, nameof(_options.MaximumOutputHeight));
        if (width == 0 || height == 0 || width > maximumWidth || height > maximumHeight)
        {
            throw new InvalidOperationException(
                $"Profile card dimensions {width}x{height} are outside the configured render bounds.");
        }
    }

    private string GetSkinAssetPath(string relativePath) =>
        Path.Combine(_options.AssetsRootPath, "skins", relativePath);

    private static int ToCoordinate(double value) => checked((int)value);

    private static uint ToPositiveDimension(double value, string name)
    {
        if (!double.IsFinite(value) || value < 1 || value > uint.MaxValue)
            throw new InvalidOperationException($"Profile skin {name} must be greater than zero.");

        return checked((uint)value);
    }
}
