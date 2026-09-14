# Fun Slash Commands

Convert the remaining "fun" text commands to slash commands and drop the prefix versions.

## Goal

- `/coinflip`, `/roll`, `/slap` replace `!coinflip`, `!roll`, `!slap` and `!d20`.
- `/roll` gains flat additive modifiers so expressions like `2d6+4` work.
- No prefix version of these commands survives.

## Decisions

| Decision | Choice | Rationale |
| --- | --- | --- |
| `!d20` | Removed, not ported | `/roll 1d20` covers it; `D20` was only a thin wrapper around `RollDice("1d20", needed)`. |
| Modifier scope | Additive terms only | Terms are `NdM` dice or flat constants joined by `+`/`-`. No keep/drop, exploding, reroll or success-counting. |
| `/slap` targets | Up to 5 optional `user` options | Discord has no varargs; 5 is the agreed arity. |
| `/slap` context menu | Not added | Explicitly out of scope. |
| Roll display | Every die is shown | No keep/drop is implemented, so there is nothing to mark as dropped; per-term dice and the flat modifier are both displayed. |
| Legacy bare number | Preserved | A lone bare number stays "sides of one die" (`20` -> `1d20`). A bare number alongside dice terms is a flat constant (`2d6+4`). |
| Roll reply wording | Lives in `FunRollFormatter` | Keeps `DiceCountWords` and the message text out of the module and under test. |

## Parsing rules

- Whitespace is stripped before parsing, so `2d6 + 4` is accepted.
- Term forms: `NdM`, `dM`, `N` (flat constant).
- Bounds: 1-10 dice per term, 2-1000 sides, at most 10 terms, flat modifier within +/-1000.
- The expression must contain at least one dice term, so `4+4` is rejected rather than silently treating both as constants.
- Defaults mirror the legacy parser: `d20` is 1d20, `2d6` is two d6.

## Subtasks

- [x] Add `Domain/Dice/DiceExpression.cs` parser plus roll result type.
- [x] Add `DiscordBot.Tests/Domain/Dice/DiceExpressionTests.cs` covering parsing and evaluation.
- [x] Add `Modules/Fun/FunSlashModule.cs` with `/coinflip`, `/roll` and `/slap`.
- [x] Delete `Modules/Fun/FunModule.cs`.
- [x] Build and run the test suite.
- [x] Update `docs/features.md` and `docs/codebase.md`.
- [x] Extract roll reply formatting to `FunRollFormatter` and cover it with `FunRollFormatterTests`.

## Review follow-ups

- Roll reply for an expression with a modifier marks the die rather than the total, so a natural 20 on
  `1d20+5` reads `showing [20] (natural) + 5 for a total of **25**!`.
- `/slap` reads its `FuzzTable` at most once per process. Modules are instantiated per invocation, so the
  guard is `static`; otherwise every slap would repeat the load and spend a `LogChannelAndFile` Discord call
  inside the three second interaction window.
- `/slap` options are named `target1` through `target5` for consistency in the Discord picker.

## Notes

- Slap tables stay lazy-loaded static `FuzzTable` instances in the module, preserving the existing
  `Settings.FunCommands` config (`SlapObjectsTable`, `SlapChoices`, `SlapFails`).
- `!slap`'s two hardcoded self-slap user IDs are preserved as-is.
- Errors are ephemeral replies; successful rolls and slaps stay public to match legacy behaviour.
- Legacy replies carried the emoji derived from the `needed` target (`:game_die:`, `:white_check_mark:`, `:x:`);
  that decoration stays in the module rather than in `FunRollFormatter`.
