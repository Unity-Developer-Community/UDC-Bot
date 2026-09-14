# Conversion Slash Commands

Replace the conversion text commands with slash commands and delete the rest of the module.

## Goal

- `/ftoc`, `/ctof` and `/curr` replace `!ftoc`, `!ctof` and `!curr` (both `!currency` overloads).
- The remaining commands in the module (`!translate` and `!currencyname`) are deleted outright.
- `/curr` autocompletes currency codes.

## Decisions

| Decision | Choice | Rationale |
| --- | --- | --- |
| Command name | `/curr` | Explicitly requested over `/currency`. |
| `!currencyname` / `!currname` | Deleted, not ported | Requested. `CurrencyService.GetCurrencyName` therefore loses its only caller and is removed, superseded by the autocomplete accessor. |
| `!translate` | Deleted, not ported | Requested. It read either a message id or raw text, so it was never a good slash command. |
| Self-mention | Dropped | Slash replies are already attributed to the invoker by Discord's UI. |
| Legacy mention guard | Dropped | `HasAnyPingableMention` guarded against `@` spam in a text command; slash options are typed strings, so it cannot trigger. |
| Option order for `/curr` | `from` (required), then `amount`, then `to` | Discord rejects a required option that follows an optional one, so the required currency must come first. |
| Autocomplete matching | Code prefix or name substring | Lets `krona` find `SEK` as well as `sek` finding it. |

## Notes

- `CurrencyService` caches its currency list lazily and rebuilds it when the API was down at startup.
  Autocomplete can call that path concurrently, so the lazy build is now serialised behind a semaphore
  with a double-checked guard, and the dictionary is filled with the indexer so a rebuild cannot throw
  a duplicate-key exception.
- `Program.cs` sets `EnableAutocompleteHandlers` explicitly. It defaults to true, but the option is now a
  hard dependency of `/curr`, so the default is not relied upon.
- Only the suggestion building is unit tested; the service's network path stays untested, matching how it
  was before this change.
- Conversion replies become public (matching the legacy text command) and errors are ephemeral.
- The `_currencies.Count <= 1` rebuild threshold is carried over from the original `IsCurrency`. It is kept
  rather than tightened to `Count == 0` because the API returns hundreds of currencies, so the case it
  would help with cannot occur, and keeping it avoids changing unrelated behaviour.
- `_buildLock` is not disposed. The service is a process-lifetime singleton, so disposal would never run
  before exit.

## Subtasks

- [x] Add a currency accessor to `CurrencyService` and make its lazy build thread safe.
- [x] Add `CurrencyAutocompleteHandler` with testable suggestion building.
- [x] Add `ConvertSlashModule` with `/ftoc`, `/ctof` and `/curr`.
- [x] Delete `Modules/Utils/ConvertModule.cs`.
- [x] Add unit tests for suggestion building.
- [x] Build and run the test suite.
- [x] Update `docs/features.md` and `docs/codebase.md`.
