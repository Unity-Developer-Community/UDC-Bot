using System.IO;
using System.Net.Http;
using DiscordBot.Domain;
using DiscordBot.Settings;
using DiscordBot.Skin;
using ImageMagick;
using Newtonsoft.Json;

namespace DiscordBot.Services;

public class ProfileCardService
{
    private readonly DatabaseService _databaseService;
    private readonly ILoggingService _loggingService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BotSettings _settings;
    private readonly XpService _xpService;

    public ProfileCardService(DatabaseService databaseService, ILoggingService loggingService,
        IHttpClientFactory httpClientFactory, BotSettings settings, XpService xpService)
    {
        _databaseService = databaseService;
        _loggingService = loggingService;
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _xpService = xpService;
    }

    private SkinData? GetSkinData() =>
        JsonConvert.DeserializeObject<SkinData>(File.ReadAllText($"{_settings.AssetsRootPath}/skins/skin.json"),
            new SkinModuleJsonConverter());

    public async Task<string> GenerateProfileCard(IUser user)
    {
        string profileCardPath = string.Empty;

        try
        {
            var dbRepo = _databaseService.Query;
            if (dbRepo == null)
                return profileCardPath;

            var userData = await dbRepo.GetUser(user.Id.ToString());

            var xpTotal = userData.Exp;
            var xpRank = await dbRepo.GetLevelRank(userData.UserID, userData.Level);
            var karmaRank = await dbRepo.GetKarmaRank(userData.UserID, userData.Karma);
            var karma = userData.Karma;
            var level = userData.Level;
            var xpLow = _xpService.GetXpLow(level);
            var xpHigh = _xpService.GetXpHigh(level);

            var xpShown = (int)(xpTotal - xpLow);
            var maxXpShown = (int)(xpHigh - xpLow);

            var percentage = (float)xpShown / maxXpShown;

            var u = (IGuildUser)user;
            IRole? mainRole = null;
            foreach (var id in u.RoleIds)
            {
                var role = u.Guild.GetRole(id);
                if (mainRole == null)
                    mainRole = u.Guild.GetRole(id);
                else if (role.Position > mainRole.Position) mainRole = role;
            }

            mainRole ??= u.Guild.EveryoneRole;

            using var profileCard = new MagickImageCollection();
            var skin = GetSkinData();
            if (skin == null)
                return profileCardPath;
            var profile = new ProfileData
            {
                Karma = karma,
                KarmaRank = karmaRank,
                Level = level,
                MainRoleColor = mainRole.Color,
                MaxXpShown = maxXpShown,
                Nickname = ((IGuildUser)user).Nickname,
                UserId = ulong.Parse(userData.UserID),
                Username = user.GetPreferredAndUsername(),
                XpHigh = xpHigh,
                XpLow = xpLow,
                XpPercentage = percentage,
                XpRank = xpRank,
                XpShown = xpShown,
                XpTotal = xpTotal
            };

            var background = new MagickImage($"{_settings.AssetsRootPath}/skins/{skin.Background}");

            var avatarUrl = user.GetAvatarUrl(ImageFormat.Auto, 256);
            if (string.IsNullOrEmpty(avatarUrl))
                profile.Picture = new MagickImage($"{_settings.AssetsRootPath}/images/default.png");
            else
                try
                {
                    Stream stream;

                    using (var http = _httpClientFactory.CreateClient())
                    {
                        stream = await http.GetStreamAsync(new Uri(avatarUrl));
                    }

                    profile.Picture = new MagickImage(stream);
                }
                catch (Exception e)
                {
                    LoggingService.LogToConsole(
                        $"Failed to download user profile image for ProfileCard.\nEx:{e.Message}",
                        LogSeverity.Warning);
                    profile.Picture = new MagickImage($"{_settings.AssetsRootPath}/images/default.png");
                }

            profile.Picture.Resize((uint)skin.AvatarSize, (uint)skin.AvatarSize);
            profileCard.Add(background);

            foreach (var layer in skin.Layers)
            {
                if (layer.Image != null)
                {
                    var image = layer.Image.ToLower() == "avatar"
                        ? profile.Picture
                        : new MagickImage($"{_settings.AssetsRootPath}/skins/{layer.Image}");

                    background.Composite(image, (int)layer.StartX, (int)layer.StartY, CompositeOperator.Over);
                }

                var l = new MagickImage(MagickColors.Transparent, (uint)layer.Width, (uint)layer.Height);
                foreach (var module in layer.Modules) module.GetDrawables(profile).Draw(l);

                background.Composite(l, (int)layer.StartX, (int)layer.StartY, CompositeOperator.Over);
            }

            profileCardPath = $"{_settings.ServerRootPath}/images/profiles/{user.Username}-profile.png";

            using var result = profileCard.Mosaic();
            result.Write(profileCardPath);
        }
        catch (Exception e)
        {
            await _loggingService.LogChannelAndFile($"Failed to generate profile card for {user.Username}.\nEx:{e.Message}", ExtendedLogSeverity.LowWarning);
        }

        if (!string.IsNullOrEmpty(profileCardPath))
            await Task.Delay(100);

        return profileCardPath;
    }
}
