# Assign Badge Select Menu

Replace the **Assign Badge** modal with the select menu that **Remove Badge** already uses.

## Goal

The Assign Badge user context command currently opens `AssignBadgeModal`, where the administrator types a
badge ID or title. Remove Badge instead shows a runtime-built select menu of the target's badges. This
change makes Assign Badge use the same interaction.

## Decisions

| Decision | Choice | Rationale |
| --- | --- | --- |
| Control | Message select menu, not a modal select | Modal select options are declared at compile time, so a runtime badge list cannot be shown in one. Remove Badge documents the same constraint. |
| Listed badges | Only badges the target does not already have | Owning a badge makes assignment fail, so offering it would only produce an error. |
| Selection count | Multiple | Matches Remove Badge, so both commands behave the same way. |
| Ordering | Natural title order, then id | Same ordering as the badge list and the Remove Badge options. |
| Overflow | First 25 options plus a notice | Discord caps select menus at 25 options. The notice points at `/admin badge assign`, mirroring how Remove Badge points at `/admin badge remove`. |
| Text sanitizing | Shared helper | `AdminSlashModule.SanitizeSelectText` becomes `StringExtensions.ToSelectOptionText` so both handlers use one implementation instead of two copies. |

## Notes

- `AssignBadgeModal` is deleted along with `ResolveBadgeByIdentifier` and `BadgeIdentifierMaxLength`, which
  only existed to resolve free text typed into the modal.
- The confirmation embed for a single assignment is replaced by a summary line listing assigned and failed
  badges, matching the Remove Badge handler.
- The custom id carries the requester and target ids, and the handler rejects anyone but the original
  requester.

## Subtasks

- [x] Add `ToSelectOptionText` to `StringExtensions` and use it from `AdminSlashModule`.
- [x] Rewrite `AssignBadgeContext` to build and send a select menu.
- [x] Add the component handler for the assign select menu.
- [x] Delete `AssignBadgeModal`, `ResolveBadgeByIdentifier` and `BadgeIdentifierMaxLength`.
- [x] Build and run the test suite.
- [x] Update `docs/features.md` if the badge feature description mentions the modal.
