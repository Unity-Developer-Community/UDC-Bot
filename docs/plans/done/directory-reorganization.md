# Directory Reorganization Plan

**Status: COMPLETED**

## Goal

Clean separation of directories by purpose:
- **Settings/** — Human-authored config, read-only by bot
- **SERVER/** — Bot-generated runtime data, read/write
- **Assets/** — Static visual assets (fonts, images, skins), human-authored, read-only

## File Moves

| File | From | To | Code Change |
|------|------|----|-------------|
| FAQs.json | `{ServerRootPath}/FAQs.json` | `Settings/FAQs.json` | UpdateService.cs L125 |
| reminders.json | `Settings/reminders.json` | `{ServerRootPath}/reminders.json` | ReminderService.cs L61, L65 |
| skins/skin.json | `{ServerRootPath}/skins/` | `{AssetsRootPath}/skins/` | UserService.cs L308, L373, L407 |
| images/default.png | `{ServerRootPath}/images/` | `{AssetsRootPath}/images/` | UserService.cs L377, L395 |
| fonts/ | SERVER/fonts/ | Assets/fonts/ | No code change (font names only) |

## New Setting

Add `AssetsRootPath` to `BotSettings` with default `"./Assets"`.

## Unchanged Paths (remain in SERVER/)

- `botdata.json`, `userdata.json`, `feeds.json` (bot-generated)
- `log.txt`, `logXP.txt`, `log_backups/` (bot-generated)
- `images/profiles/` (bot-generated profile cards)
- `unitymanual.json`, `unityapi.json` (downloaded cache)
- `{TipImageDirectory}/` (bot-generated tips)

## Docker Changes

- Assets/ lives inside DiscordBot/ for native + Docker compatibility
- Dockerfile: `COPY ./DiscordBot/Assets/` for baking into image
- docker-compose: `.\DiscordBot\Assets\:/app/Assets:ro` volume mount (read-only)

## Subtasks

- [x] Add `AssetsRootPath` to Settings.cs + Settings.example.json + Settings.json
- [x] Update UpdateService.cs: FAQs.json → `Settings/FAQs.json` (hardcoded)
- [x] Update ReminderService.cs: → `{ServerRootPath}/reminders.json`
- [x] Update UserService.cs: skins + default.png → AssetsRootPath
- [x] Move assets into DiscordBot/Assets/ (via DiscordBot/SERVER/ → Assets/)
- [x] Update Dockerfile COPY paths
- [x] Update docker-compose volume mounts (with `:ro`)
- [x] Fix AssetsRootPath/ServerRootPath null in Settings.json
- [x] Update .gitignore for SERVER/ runtime data
- [x] Remove stale static assets from DiscordBot/SERVER/
- [x] Remove empty profiles/subtitles dirs from Assets/images/
- [x] Build and test
- [x] Peer review
