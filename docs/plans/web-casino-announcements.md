# #414 Web casino: announce big wins in Discord

Status: finished, waiting for the user's review.

## Goal

A jackpot or a rare slots win played in the web casino is announced in the Discord casino channel, like the same win played with `/casino slots spin`: the same text and a picture of the reels, marked as won on the web with a link to the web casino. The web casino posts through a Discord webhook, so it still never holds the bot token. Big wins are the only thing it posts in Discord.

## Acceptance criteria

- AC1 WHEN a web spin or respin returns a play with `SlotsPlay.PostPublicly` true (a jackpot, or a line rarer than one spin in `SlotMachine.PublicWinOneIn`), THE SYSTEM SHALL send one webhook message: the text of `SlotsMessageBuilder.PublicText` with the player as a `<@id>` mention, `allowed_mentions` that ping nobody, and the public embed whose image is the attached static picture of the reels (`spin.png`), whose title links to `Casino:PublicOrigin` and whose footer says it was won on the web casino. Check: `CasinoWeb.Tests` hub test with `ScriptedSlotRandom`, a fake renderer and a fake webhook handler asserting the multipart body.
- AC2 WHEN rendering the picture fails, THE SYSTEM SHALL still send the announcement without a picture (the embed shows the symbols as emoji either way) and log the rendering failure as a warning. Check: hub test with a failing renderer.
- AC3 WHEN a web play is not announced (an ordinary win, a loss, a refusal), THE SYSTEM SHALL render and send nothing. Check: hub test.
- AC4 WHEN the webhook answers an error, times out or cannot be reached, THE SYSTEM SHALL still return the spin result unchanged and log a warning naming the player, the payout and the status code or exception type. The spin result never waits for the picture or the post. Check: hub test with a failing handler and a handler that never answers.
- AC5 WHEN `Casino:AnnouncementWebhookUrl` is empty in Development, THE SYSTEM SHALL start, log once that big wins are not announced, and render and send nothing; WHEN it is empty outside Development, or set to anything but a `https://` Discord webhook URL in any environment, THE SYSTEM SHALL refuse to start with a message naming the setting. Check: `StartupTests`.
- AC6 The webhook URL (it carries the webhook's token) never appears in a log line. Check: hub test with a capturing logger provider, asserting no captured line contains the fake token.
- AC7 The bot's own announcement is unchanged (text, footer, GIF). Check: existing `SlotsMessageBuilderTests` pass unchanged, plus a test of the web variant.
- AC8 WHEN the casino image starts with a read-only root filesystem and a writable `/tmp`, as in production, THE SYSTEM SHALL render the picture (a startup log line says the slots pictures are ready) and stay under its 384Mi memory limit. Check: `docker run --read-only --tmpfs /tmp` of the built image, then the log and `docker stats`.
- AC9 WHEN the casino runs in production and a member hits a big win on `casino.udc.ovh`, THE SYSTEM SHALL post it with the picture in the casino channel under the webhook's name. Check: manual, after the push.

## Design

- **Sending:** plain `HttpClient` from `IHttpClientFactory` (a named client, as `DiscordGuildMembershipChecker` does), `POST multipart/form-data` to the webhook URL: a `payload_json` part (`content`, one embed with `image.url = attachment://spin.png`, `allowed_mentions: { "parse": [] }`) and a `files[0]` part with the PNG. Without a picture (AC2), plain JSON with the same payload minus the image. Not Discord.Net's `DiscordWebhookClient`: its constructor fetches the webhook synchronously, and its HTTP cannot be faked in the tests the way the existing Discord fakes are. No new dependency.
- **The named client drops the default HTTP logging** (`RemoveAllLoggers()`, Microsoft.Extensions.Http 8+): `IHttpClientFactory` logs every request URI at Information level, which would print the webhook token to Loki. Error logs carry the status code or exception type only, never the URL or the request.
- **Message:** reuse `SlotsMessageBuilder` so both front ends word wins the same way. `PublicText(play, MentionUtils.MentionUser(userId))` gives the text; `BuildPublicEmbed` gains optional `Uri? webCasino` and `string? pictureFileName`: when `webCasino` is set, the title links to it, the footer reads `Won on the web casino · play at <host> or with /casino slots spin (<min>–<max> tokens)`, and the image is `attachment://<pictureFileName>` when one is given; when both are null (the bot), the embed is exactly today's. The announcer maps the `Embed` (title, url, description, color, footer, image) to the webhook JSON. The mention shows the member's name in Discord without a ping, as the bot's `AllowedMentions.None` does.
- **Picture:** the bot's `SlotsRenderer.RenderStaticAsync` (the last frame of the bot's GIF, as a PNG), rendered only for announced plays. `SlotsService` keeps its `null` renderer: the browser animates every spin, and the picture is only for Discord. The casino registers its own `SlotsRenderer` built from a copy of the bot settings with `AssetsRootPath` replaced by `Casino:AssetsPath` (default `Assets`; `../DiscordBot/Assets` in `appsettings.Development.json`, as `BotSettingsPath` does), so the bot's `./Assets` relative path stays the bot's. A hosted service warms the renderer in the background at start (about 3 s, never delays readiness) and logs `slots pictures ready`, or an error when the art is missing; the announcer then still posts without a picture. Not registered at all when no webhook URL is set.
- **Image:** `CasinoWeb/Dockerfile` copies `DiscordBot/Assets/slots/` and `DiscordBot/Assets/fonts/ConsolasBold.ttf` into `/app/Assets/` of the runtime stage (the files `DiscordBot.Tests.csproj` copies for the renderer tests). Magick.NET already ships with the `DiscordBot` reference. The bot's pod has a writable root filesystem and the casino's does not, hence AC8; if rendering needs more than `/tmp`, or memory peaks above about 300Mi, raise the limit or add the needed `emptyDir` in `casino.yaml`.
- **Announcer:** `CasinoWeb/Games/SlotsWinAnnouncer` (singleton). `Announce(ulong userId, SlotsPlay play)` returns at once: when `play.PostPublicly` and a URL is configured, it starts render-then-post with `SafeFireAndForget()`; the work catches every exception and logs the warnings of AC2 and AC4 through `ILoggingService`. Post timeout 10 s. No retry: a lost post costs nothing but the announcement. A 429 is logged like any error (a few posts a minute at most even from one player at the hub's 5 actions per second, far below a webhook's limit). A graceful stop may drop a post in flight.
- **Hub:** `CasinoHub.SlotsSpin` and `SlotsRespin` call `Announce` with the play before returning its DTO. A respin can win, and the bot announces respin wins too.
- **Setting:** `CasinoHostOptions.AnnouncementWebhookUrl` (`Casino__AnnouncementWebhookUrl`, user-secrets locally). Validated at start (AC5): scheme `https`, host `discord.com`, `ptb.discord.com`, `canary.discord.com` or `discordapp.com`, path `/api/webhooks/<digits>/<token>`, no query or fragment. The host allow-list keeps a mistyped or planted URL from sending player names elsewhere. Required outside Development, like `KeysPath`, so a missing secret stops the deploy instead of silently dropping every announcement (approved by the user).
- **Channel and identity:** the webhook is created in Discord on the channel where members play `/casino` (the bot has no casino channel setting: it posts where the command ran). Its name and avatar, set in Discord, are the author members see.
- **Production secret:** ExternalSecret `casino-announcements` in `k8s/prod/casino.yaml`, from the 1Password item `Casino` (UDC vault, the item for everything casino-specific), field `Announcement Webhook URL`, to the secret key `webhook-url`; the Deployment reads it into `Casino__AnnouncementWebhookUrl`. The item exists. The dev environment does not run the casino yet (#415 adds it).
- **Not in this plan:** announcing blackjack or other games (the bot announces only slots); an animated GIF in web announcements.

## Checklist

### #414.1 Announce web big wins through a webhook

- [x] `SlotsMessageBuilder.BuildPublicEmbed(play, webCasino, pictureFileName)` as in Design; a `DiscordBot.Tests` test of the web footer, link and image, existing tests unchanged (AC7).
- [x] `CasinoHostOptions.AnnouncementWebhookUrl` and `AssetsPath` with the validation and the Development / non-Development rule; `appsettings.Development.json`; `StartupTests` for AC5.
- [x] The casino's `SlotsRenderer` registration and warm-up hosted service, `SlotsWinAnnouncer` with its named `HttpClient` without default logging, registration in `HubSetup.AddCasinoHub`, calls in `CasinoHub.SlotsSpin` and `SlotsRespin`.
- [x] `CasinoWebFactory`: a fake webhook URL, a fake renderer (a `SlotsRenderer` subclass returning fixed bytes, or throwing) and a fake handler for the named client; hub tests for AC1–AC4 and AC6.
- [x] `CasinoWeb/Dockerfile` copies the slots art and the font (Design); build the image and run the AC8 check.
- [x] Docs: `CasinoWeb/README.md` (settings table, local setup: an optional webhook on a test channel through user-secrets), `docs/casino.md` (Slots "Public posts" and "Web Casino": web wins go through the webhook with a static picture), `docs/features.md` (Web Casino row).
- [x] `dotnet build` with no new warnings, `dotnet test` green.

Visible after: locally, with a webhook URL set in user-secrets, a big win in the browser appears with a picture of the reels in that webhook's channel. Without one, nothing changes.
Needs you: optionally, create a webhook on a test channel, set it with `dotnet user-secrets set Casino:AnnouncementWebhookUrl <url>` in `CasinoWeb/` and spin until a big win (or trust the tests) (at the end).
Decided during the run: the casino's `SlotsRenderer` and its warm-up hosted service are always registered and stay idle when no webhook URL is set (the renderer loads its art on first use, the warm-up returns at once, the announcer returns before rendering), instead of not being registered at all. Why: `AddCasinoHub` cannot read the setting at registration without reading configuration eagerly, which `WebApplicationFactory` test overrides do not reach reliably. Alternative: pass `IConfiguration` to `AddCasinoHub` and register conditionally. To change: move the three registrations in `HubSetup.AddSlotsAnnouncements` behind that check.
Decided during the run: the casino's renderer is built from `new BotSettings { AssetsRootPath = <Casino:AssetsPath> }` instead of a copy of the full bot settings. Why: the renderer reads only `AssetsRootPath` (`SlotsRenderer` constructor), and a minimal object leaves the shared `BotSettings` singleton, which holds the bot's secrets, untouched. Alternative: deserialize the bot settings and replace `AssetsRootPath`. To change: build the settings in `HubSetup.AddSlotsAnnouncements` from `BotSettingsPath`.

### #414.2 Production secret

Mechanical: edits fully specified below; check is `kubectl apply --dry-run=client -f k8s/prod/casino.yaml`, or a YAML parse when no cluster is reachable.

- [x] `k8s/prod/casino.yaml`: ExternalSecret `casino-announcements` and the `Casino__AnnouncementWebhookUrl` env var, as in Design (plus any `emptyDir` or memory change AC8 showed was needed).
- [x] `docs/deployment.md`: "The Web Casino" gets a webhook bullet (created on the casino channel, 1Password item `Casino`, required at start); the External Secrets table gets the `casino-announcements` row.

Visible after: nothing until pushed to `master`; then web big wins appear with a picture in the casino channel.
Needs you: after the push, check one web big win in the channel (AC9) (at the end).

## Finishing

Add `docs/decisions.md` D5: the web casino announces through a webhook, not through the bot; rejected: a table of web wins the bot polls and posts (a schema change, and announcements depend on the bot running). Then delete this plan.
