# Recruitment management

Recruitment is disabled in the example, development and production settings. It uses four **new** forums, one staff text feed, and the existing configured moderator role. Settings and asset changes take effect after a restart. There is no migration from the old single-forum babysitter or `feat/babysitter` state.

## Member behavior

- Each member has **one listing total** across paid recruiting, paid for hire, hobby recruiting and hobby for hire. An older pending attempt reserves the slot; changing forums does not bypass it.
- The bot posts one advisory with an accessible initial PNG snapshot, essential text and owner controls. Database/member facts can be **unknown**. Payment and tag suggestions are advisory; there is no trust score or automated scam accusation.
- The owner enters the five-character code from the forum's native Guidelines. The 30-minute window starts after usable controls are delivered. A correct code accepts an eligible listing immediately. An acknowledged blocked attempt can become accepted if its blocker clears during the window.
- At the deadline, an unacknowledged attempt is removed. An acknowledged attempt still blocked by another listing/cooldown is locked and archived. Pins and explicit exemptions protect against automatic actions.
- Only **accepted** listings start a cooldown, measured from the thread's original creation time. Closing/deleting on day 2 leaves 28 days; doing so on day 40 adds no wait. An open listing still occupies the slot after day 30, including when Discord naturally archives it. Removing an unaccepted attempt creates no cooldown.
- The owner can close the listing or confirm removal through an expiring, single-use control. Accepted cooldowns survive removal. The `Closed` tag also requests lock-and-archive. Existing unrelated applied tags are preserved if there is no room for another tag.
- Accepted listings without a qualifying reply close after 30 days. Replies from other humans count; owner, bot, webhook, configured moderator and administrator replies do not. Recorded qualifying evidence survives message deletion. Incomplete response history holds unanswered closure for staff review.
- Public replies and owner editing remain available while a listing is open. There is no short edit lock, reply deletion, or parent permission rewrite. Configured recruitment forums and their threads do not earn new XP, including while recruitment is disabled; existing totals are unchanged.

## Configure new forums

Set these fields under `Recruitment` in the existing settings file:

```json
{
  "Enabled": false,
  "FeedChannelId": "0",
  "Forums": {
    "PaidRecruitingId": "0",
    "PaidForHireId": "0",
    "HobbyRecruitingId": "0",
    "HobbyForHireId": "0"
  },
  "AcknowledgementMinutes": 30,
  "CooldownDays": 30,
  "UnansweredDays": 30,
  "PostRetentionMonths": 12,
  "AuthorRetentionMonths": 24
}
```

Replace zero IDs with the new channels. Do not reuse the old `Channels.Recruitment` or manually configure tag IDs. All four IDs must be distinct forum channels in `GuildId`. `Roles.Moderator` must resolve to the intended role.

The bot needs View Channel, Read Message History, Send Messages in Threads, Manage Threads and Manage Channels on each forum. Attach Files enables the PNG; a definitively rejected image upload falls back to text. In the feed it needs View Channel, Read Message History and Send Messages. Deny View Channel to `@everyone`; explicit access grants are limited to the configured moderator role, administrators and the bot. Existing Guild Members and Message Content intents must also be enabled in the Discord application.

Review `DiscordBot/Assets/recruitment/guidelines/*.md` and `tags.json` before enabling. Templates own the **entire native Guidelines** and contain one `{{code}}` plus the duration placeholders. Each tag definition has a stable key, display name, advisory group and closing flag. Keep keys stable when renaming a tag. The one closing tag is unmoderated. The shipped copy/tag groups are editable defaults pending moderator approval.

Local build/publish artifacts and Docker include the recruitment assets and bundled font. Use a persistent `ServerRootPath`; production remains one replica with `Recreate` deployment. Do not run two bot processes against the same state directory.

Keep member posting closed during initial setup. After configuring and restarting with `Enabled=true`, check `/recruitment status`, preview each forum, and verify native Guidelines, Require Tag and tag IDs. Unexpected first-use content, direct Guidelines edits, changed managed tag permissions or missing known tags hold processing. `/recruitment repair` explicitly republishes owned setup after review, preserves unrelated tags, and can recreate a deleted managed tag. Discord cannot restore the old ID or its lost applications after a tag is deleted; inspect affected listings when repairing that case. Removing managed definitions is intentionally held for manual review.

The persisted code rotates on Monday at 00:00 UTC. Publication is verified per forum; an interrupted rotation retries the same code. A challenge accepts its issued code and codes subsequently confirmed visible during its window. Codes are not included in advisories, staff entries, previews or diagnostic messages.

Create, edit and pin the moderator reference post yourself. Use `/recruitment manage` → **Designate reference post** with its thread ID. This excludes it from challenges and capacity and removes the bot's provisional advisory. An already accepted cooldown remains until explicitly waived. Pinning alone does not free an accepted member slot, and ordinary moderator personal listings follow the normal listing policy.

## Moderator controls

All commands are ephemeral and check the configured guild and the caller's current moderator role or administrator permission. Changes require an audit reason.

| Command | Purpose |
| --- | --- |
| `/recruitment status` | Availability, setup/storage faults and inventory holds. |
| `/recruitment lookup id` | Retained thread/author history and creation cooldown. |
| `/recruitment preview [forum]` | Proposed Guidelines with a masked code, and managed tag names. |
| `/recruitment repair reason` | Explicitly publish/verify managed setup after previewing it. |
| `/recruitment reconcile reason` | Retry inventory, message delivery and pending actions. |
| `/recruitment manage operation thread-id reason` | Exempt/unexempt, reset a pending acknowledgement, reset timeout streak, designate reference, close/reopen, or review uncertain evidence. |
| `/recruitment waive-cooldown author reason` | Clear the author's creation cooldown; never grants a second slot. |

**Confirm qualifying reply** preserves positive evidence. **Confirm no qualifying reply** certifies missing coverage after staff review and allows the normal unanswered deadline to apply. **Confirm unavailable thread is absent** releases a held slot only when Discord availability is uncertain; it does not infer a deletion actor or change the cooldown. Reopening checks capacity and suppresses repeated automatic unanswered closure for that listing. A closed-tag or later explicit close still applies.

The staff feed keeps one entry per attempt, updated with identity, eligibility, pending action, outcome and recent audit reasons. The third consecutive confirmed Guidelines timeout marks that attempt for moderator attention; later timeouts do not repeat the threshold alert until a real acknowledgement resets the streak. Wrong guesses are rate-limited and do not increment it. An independently missing thread or an ambiguous delete response is not counted as a confirmed bot timeout.

## Persistence and recovery

State is stored in `{ServerRootPath}/recruitment/`:

- `recruitment-state.json`: schema `udc-recruitment-v2`, string IDs, UTC dates, per-forum receipts/cursors, listing records and author aggregates.
- `recruitment-state.json.bak`: last validated snapshot before the latest replacement.
- `initialized`: matching store identity, used to detect a missing primary.
- `writer.lock`: exclusive process lease. The file may remain after shutdown; its existence does not itself mean the lease is held.

Writes are serialized, flushed and replaced atomically. A write failure fences subsequent mutations until storage is repaired and the process restarted. Corrupt, unsupported, incomplete, missing or mismatched history is never silently replaced with empty cooldowns. Back up the directory and its identity receipt together. A completely lost volume cannot be distinguished locally from a new volume; existing nonempty forum setup holds first-use initialization. Restore history before repairing setup after a suspected volume loss.

For storage recovery, stop the bot, preserve the damaged files, restore a validated primary (or review the `.bak`) with its matching `initialized` receipt, then restart and reconcile. A backup may precede the latest acceptance/action; inspect the staff feed and Discord before restoring it. Do not change forum/guild mappings, delete the identity receipt, or clear author history to bypass a fault. No legacy state converter is provided.

The worker saves intent and staff evidence before destructive calls, then rechecks current identity and protection. Interrupted message sends recover bot-owned messages by marker before retrying. Recovery scans are bounded; a scan that cannot establish completeness holds instead of duplicating a message. An interrupted delete that finds an absent thread records an unknown actor. Archived-message updates save a restoration intent, temporarily unarchive when necessary, and restore archiving after updating the bot's own advisory. [Discord thread rules](https://docs.discord.com/developers/topics/threads)

Startup, disconnects, lost queue events and delivery failures invalidate relevant negative reply evidence and restore full usable windows for pending attempts. Timely queued owner submissions and reply facts are processed before automatic actions; acknowledgement uses the Discord interaction timestamp. Archive discovery resumes bounded pages across sweeps, with at most 50 new identities and 50 retained records processed per pass; incomplete inventory holds acceptance/actions. Retained known records are reconciled directly; terminal details expire after 12 months and inactive author aggregates after 24 months, while open/reference/held/action dependencies remain. The discovery floor prevents expired detail from being recreated by old archive scans.

## Validation before deployment

The automated tests cover policy, persistence faults, setup/rotation, lost responses, evidence gaps, owner/staff authorization, interaction registration and the real Linux PNG renderer. They do not call live Discord.

Before opening posting access, exercise the new forums in staging: effective permissions and feed privacy, native Guidelines/tag repair, acknowledgement and immediate acceptance, duplicate/cooldown behavior, removal confirmation, pinned/reference protection, unanswered/Closed closure, restart recovery, image descriptions and mobile readability. Verify the mounted persistent directory and graceful shutdown. No live bot login, guild command registration, staging mutation, deployment or remote push is part of local implementation validation.
