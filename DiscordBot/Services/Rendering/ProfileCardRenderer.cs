using System.IO;
using DiscordBot.Skin;
using ImageMagick;
using ImageMagick.Formats;
using Newtonsoft.Json;

namespace DiscordBot.Services.Rendering;

public sealed class ProfileCardRenderer : IProfileCardRenderer
{
    private static readonly IReadOnlySet<MagickFormat> AllowedAvatarFormats = new HashSet<MagickFormat>
    {
        MagickFormat.Avif,
        MagickFormat.Gif,
        MagickFormat.Gif87,
        MagickFormat.Jpe,
        MagickFormat.Jpeg,
        MagickFormat.Jpg,
        MagickFormat.Png,
        MagickFormat.Png8,
        MagickFormat.Png24,
        MagickFormat.Png32,
        MagickFormat.Png48,
        MagickFormat.Png64,
        MagickFormat.WebP
    };

    private readonly ImageRenderOptions _options;
    private readonly BundledFontResolver _fontResolver;

    public ProfileCardRenderer(ImageRenderOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        MagickResourcePolicy.Configure(_options);
        _fontResolver = new BundledFontResolver(options.AssetsRootPath);
    }

    public byte[] Render(ProfileCardRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var skin = LoadSkin();
        using var avatar = LoadAvatar(request.AvatarBytes);
        var avatarSize = ToPositiveDimension(skin.AvatarSize, nameof(skin.AvatarSize));
        avatar.Resize(avatarSize, avatarSize);

        var renderRequest = request with
        {
            AvatarSampleColor = SampleColor(avatar),
            XpPercentage = NormalizePercentage(request.XpPercentage)
        };
        using var background = new MagickImage(GetSkinAssetPath(skin.Background));

        ValidateOutputDimensions(background.Width, background.Height);

        foreach (var layer in skin.Layers)
        {
            CompositeLayerImage(background, avatar, layer);

            if (layer.Modules.Count == 0)
                continue;

            var layerWidth = ToPositiveDimension(layer.Width, nameof(layer.Width));
            var layerHeight = ToPositiveDimension(layer.Height, nameof(layer.Height));
            ValidateLayerDimensions(layerWidth, layerHeight);
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

        background.Strip();
        background.Format = MagickFormat.Png32;
        background.Depth = 8;
        var result = background.ToByteArray(new PngWriteDefines
        {
            BitDepth = 8,
            CompressionLevel = 6,
            CompressionStrategy = PngCompressionStrategy.Default
        });
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
        {
            if (avatarBytes.Length > _options.MaximumAvatarBytes)
            {
                throw new InvalidDataException(
                    $"Avatar input is {avatarBytes.Length} bytes; the limit is {_options.MaximumAvatarBytes} bytes.");
            }

            var info = new MagickImageInfo(avatarBytes);
            ValidateAvatar(info.Width, info.Height, info.Format);
            return EnsureValidAvatar(new MagickImage(avatarBytes));
        }

        return EnsureValidAvatar(new MagickImage(
            Path.Combine(_options.AssetsRootPath, "images", _options.DefaultAvatarFileName)));
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
        var pixel = pixels?[0, 0] ?? throw new InvalidOperationException("Could not sample the profile avatar.");
        var color = pixel.ToColor() ?? throw new InvalidOperationException("Could not read the sampled avatar color.");
        return new Color(color.R, color.G, color.B);
    }

    private void ValidateAvatar(uint width, uint height, MagickFormat format)
    {
        if (width == 0 || height == 0 || width > _options.MaximumAvatarWidth ||
            height > _options.MaximumAvatarHeight)
        {
            throw new InvalidDataException(
                $"Avatar dimensions {width}x{height} are outside the configured input bounds.");
        }

        if (!AllowedAvatarFormats.Contains(format))
            throw new InvalidDataException($"Avatar format '{format}' is not supported.");
    }

    private MagickImage EnsureValidAvatar(MagickImage avatar)
    {
        try
        {
            ValidateAvatar(avatar.Width, avatar.Height, avatar.Format);
            return avatar;
        }
        catch
        {
            avatar.Dispose();
            throw;
        }
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

    private void ValidateLayerDimensions(uint width, uint height)
    {
        if (width > _options.MaximumOutputWidth || height > _options.MaximumOutputHeight)
        {
            throw new InvalidOperationException(
                $"Profile skin layer dimensions {width}x{height} exceed the output bounds.");
        }
    }

    private string GetSkinAssetPath(string relativePath) =>
        Path.Combine(_options.AssetsRootPath, "skins", relativePath);

    private static int ToCoordinate(double value)
    {
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
            throw new InvalidOperationException($"Profile skin coordinate '{value}' is invalid.");

        return checked((int)value);
    }

    private static float NormalizePercentage(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;

    private static uint ToPositiveDimension(double value, string name)
    {
        if (!double.IsFinite(value) || value < 1 || value > uint.MaxValue)
            throw new InvalidOperationException($"Profile skin {name} must be greater than zero.");

        return checked((uint)value);
    }
}
