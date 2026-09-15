# Rank, Profile And Server Slash Commands

Convert the remaining account and leaderboard text commands to slash commands.

## Goal

- `/members` replaces `!members`.
- `/join-date [user]` replaces `!join-date`, gaining an optional target.
- `/karma` replaces `!karma`.
- `/top` replaces `!top`.
- `/top-karma [interval]` replaces `!topk`, `!topkw`, `!topkm` and `!topky`.

## Decisions

| Decision | Choice | Rationale |
| --- | --- | --- |
| `/karma` | Static explainer | `!karma` only ever printed an explanation, so the conversion is faithful. The stale `!profile` reference in the text is updated to `/profile`. |
| `/join-date` target | Optional `user` | Requested. Mirrors the existing `/profile [user]`. |
| `/top-karma` interval | `Choice` option defaulting to all time | Collapses four commands into one. Values match the legacy aliases: `week`, `month`, `year`, plus omitted meaning all time. |
| Leaderboard retry behaviour | Errors answered ephemerally | The legacy code returned without replying when the database query was unavailable, which would leave a slash command hanging. |
| Leaderboard lifetime | Still deleted after one minute | Preserves legacy behaviour. |

## Notes

- `RankModule` and `ProfileModule` both lose all of their commands, so both files are deleted. `ProfileModule`
  was already reduced to `!karma`, `!join-date` and the copy of `!profile` that `ProfileSlashModule` replaced.
- Row formatting moves to `RankEmbedFormatter` so the padding rules are unit tested. The legacy width
  calculation was `floor(log10(count)) + 1`, which is correct for aligning ranks 1 through 10, and is kept.
- The legacy embed builder blocked on async work inside a LINQ `Select` via `.Result`. The rewrite awaits
  the username lookups directly.
- `/top` keeps a fixed 10 rows, matching the legacy commands.

## Deliberate differences from the text commands

- `/karma` drops the legacy `{preferred name}, ` prefix. Slash replies are already attributed to the invoker,
  which is the same reason the conversion commands dropped their self-mention.
- Both `!karma` and `!join-date` deleted `Context.Message`, which in `Discord.Net.Commands` is the message
  that *triggered* the command, not the bot's reply: `ServerModule.DisplayHelp` still relies on this when it
  ends with `Context.Message.DeleteAsync()`. Slash commands have no invoking message, so there is nothing
  left to delete and no reply deletion has been lost. The reply to `!karma` was always kept.
- `/join-date` addresses the target in the third person, because the option can now name someone other than
  the invoker.
- `/join-date` uses `yyyy` instead of the legacy `yyy`. Both render a four digit year; `yyyy` is the
  correct format specifier.

## Subtasks

- [x] Add `RankEmbedFormatter` with testable row formatting.
- [x] Add `RankSlashModule` with `/top` and `/top-karma [interval]`.
- [x] Add `/join-date` and `/karma` to `ProfileSlashModule`.
- [x] Add `/members` to `ServerSlashModule`.
- [x] Delete `RankModule.cs` and `ProfileModule.cs`.
- [x] Add unit tests for `RankEmbedFormatter`.
- [x] Build and run the test suite.
- [x] Update `docs/features.md` and `docs/codebase.md`.
