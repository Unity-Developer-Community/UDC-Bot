using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using CasinoWeb.Configuration;
using Discord;
using DiscordBot.Extensions;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Services.Logging;
using Microsoft.Extensions.Options;

namespace CasinoWeb.Games;

/// <summary>
/// Announces a web jackpot or rare slots win in the casino channel through a Discord webhook, worded like the bot's own
/// announcement. The web casino never holds the bot token, so this is the only thing it posts in Discord.
/// </summary>
public sealed class SlotsWinAnnouncer(
    IOptions<CasinoHostOptions> options,
    IHttpClientFactory httpFactory,
    ILoggingService logging,
    SlotsRenderer? renderer = null)
{
    public const string HttpClientName = "slots-announcements";

    // The address carries the webhook's token: it stays in this field and is never logged.
    private readonly Uri? _webhook = options.Value.AnnouncementsConfigured ? new Uri(options.Value.AnnouncementWebhookUrl!) : null;
    private readonly Uri _publicOrigin = options.Value.PublicOriginUri;

    /// <summary>Returns at once; the picture is drawn and the message posted in the background, so a spin never waits for Discord.</summary>
    public void Announce(ulong userId, SlotsPlay play)
    {
        if (_webhook is null || !play.PostPublicly)
            return;

        RenderAndPostAsync(userId, play).SafeFireAndForget(nameof(SlotsWinAnnouncer));
    }

    private async Task RenderAndPostAsync(ulong userId, SlotsPlay play)
    {
        try
        {
            var picture = await RenderPictureAsync(userId, play);
            using var content = BuildContent(userId, play, picture);
            using var client = httpFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsync(_webhook, content);
            if (!response.IsSuccessStatusCode)
                await Warn($"Casino: could not announce the slots win of {userId} ({play.Payout} tokens): the webhook answered {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            // The type only: an HTTP exception's text may carry the request address.
            await Warn($"Casino: could not announce the slots win of {userId} ({play.Payout} tokens): {ex.GetType().Name}");
        }
    }

    /// <summary>The picture of the reels, or <c>null</c> when it cannot be drawn: the embed shows the symbols as emoji anyway.</summary>
    private async Task<byte[]?> RenderPictureAsync(ulong userId, SlotsPlay play)
    {
        if (renderer is null)
            return null;

        try
        {
            using var stream = await renderer.RenderStaticAsync(play.Result!);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            await Warn($"Casino: could not draw the slots picture for {userId} ({play.Payout} tokens), announcing without it: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private HttpContent BuildContent(ulong userId, SlotsPlay play, byte[]? picture)
    {
        var embed = SlotsMessageBuilder.BuildPublicEmbed(play, _publicOrigin, picture is null ? null : SlotsMessageBuilder.PictureFileName);
        var payload = new JsonObject
        {
            ["content"] = SlotsMessageBuilder.PublicText(play, MentionUtils.MentionUser(userId)),
            // Shows the member's name without pinging them, as the bot's AllowedMentions.None does.
            ["allowed_mentions"] = new JsonObject { ["parse"] = new JsonArray() },
            ["embeds"] = new JsonArray(EmbedToJson(embed)),
        };

        if (picture is null)
            return new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

        var file = new ByteArrayContent(picture);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent
        {
            { new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json"), "payload_json" },
            { file, "files[0]", SlotsMessageBuilder.PictureFileName },
        };
    }

    private static JsonObject EmbedToJson(Embed embed)
    {
        var json = new JsonObject
        {
            ["title"] = embed.Title,
            ["url"] = embed.Url,
            ["description"] = embed.Description,
            ["color"] = embed.Color?.RawValue,
            ["footer"] = new JsonObject { ["text"] = embed.Footer?.Text },
        };
        if (embed.Image is { } image)
            json["image"] = new JsonObject { ["url"] = image.Url };
        return json;
    }

    private Task Warn(string message) => logging.LogChannelAndFile(message, ExtendedLogSeverity.Warning);
}
