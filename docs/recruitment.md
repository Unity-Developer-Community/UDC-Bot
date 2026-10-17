# Recruitment management

Recruitment is disabled in the example, development and production settings. It uses four **new** forums, one staff text feed, and the existing configured moderator role. Settings and asset changes take effect after a restart. The administrator portfolio-site registry changes immediately without rewriting settings.

## Member behavior

- Each member has **one listing total** across paid recruiting, paid for hire, hobby recruiting and hobby for hire. An older pending attempt reserves the slot; changing forums does not bypass it.
- Posts by bots, configured moderators and administrators are excluded from member recruitment. Previously managed staff posts become references and lose their bot advisory; accepted history is preserved.
- The bot maintains one **Recruitment · Automated Observations** response with nine factual checks: Account Age, Join Date, Server Roles, Compensation, Duration, Portfolio, DM Requests, Profile Image and Post Detail. Green describes an observed fact, yellow requests clarification, red highlights missing expected detail or a concern, and grey means Unavailable or Not Applicable. No color establishes trust, authorship or endorsement. **Legend** privately explains all nine checks and their current evidence.
- The response uses Discord Components V2: a deterministic 3×3 image, short guidance, operational notices and the author's observed nickname/display name, username, immutable ID and effective avatar. Names and excerpts are sanitized and every send/edit disables mentions. The image excludes names and never fetches avatars or external links. A complete text-only V2 fallback keeps the same instructions and controls if rendering, Attach Files or Embed Links is unavailable, or Discord rejects the media write.
- Amounts need compensation context; unrelated prices and applicant fees do not establish pay. Mixed paid/unpaid/revenue-share terms remain visible. For-hire samples can be recognized work links or attached image/video/audio files; attached samples without a link request clarification in paid for-hire. Negated claims and arbitrary contact links do not establish samples. Detection never fetches URLs, verifies authorship or executes attachments.
- Short evidence-bearing warnings cover applicant fees, payment pressure and private terms. Affirmative DM-first instructions, including “DM to apply”, request public first contact; negation, safety advice and private-information exceptions are handled separately. English keyword heuristics can miss context or other languages; observations never remove a post. Starter/attachment edits and role/avatar/name changes refresh the same response. Clarify scope, samples and payment publicly before moving to DMs.
- Missing facts remain explicit. Community activity is not queried or displayed. Discord's documented [member](https://docs.discord.com/developers/resources/guild#guild-member-object) and [user](https://docs.discord.com/developers/resources/user#user-object) structures do not expose server-profile privacy, so no privacy flag is inferred.
- The owner enters the five-character code from the forum's native Guidelines. The 30-minute window starts after usable controls are delivered. A correct code accepts an eligible listing immediately. Valid submissions received within the window remain timely even if the worker processes them later.
- At the deadline, an unacknowledged attempt is removed. Known duplicate, cooldown and posting-pause conflicts are locked and archived promptly without asking for a code. Closed attempts do not automatically reopen when restrictions expire. Forum-post pins and explicit exemptions protect against automatic actions; pinning only the bot response grants no protection.
- Only **accepted** listings start a cooldown, measured from the thread's original creation time. Closing/deleting on day 2 leaves 28 days; doing so on day 40 adds no wait. An open listing still occupies the slot after day 30, including when Discord naturally archives it. Removing an unaccepted attempt creates no accepted-listing cooldown, but still counts toward the attempt budget.
- **Your listing** opens private owner controls. The owner can close the listing or confirm removal through an expiring, single-use control. Accepted cooldowns survive removal. The `Closed` tag also requests lock-and-archive. Existing unrelated applied tags are preserved if there is no room for another tag.
- Accepted listings remain indefinitely until the owner or a moderator closes/removes them. Owners can edit or bump the existing thread to preserve its conversation history. Natural auto-archive does not free the slot.
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
  "AttemptLimit": 2,
  "AttemptWindowMinutes": 60,
  "AttemptPauseMinutes": 60,
  "PostRetentionMonths": 12,
  "AuthorRetentionMonths": 24
}
```

Replace zero IDs with the new channels. Do not reuse the old `Channels.Recruitment` or manually configure tag IDs. All four IDs must be distinct forum channels in `GuildId`. `Roles.Moderator` must resolve to the intended role.

The bot needs View Channel, Read Message History, Send Messages in Threads, Manage Threads and Manage Channels on each forum. Creating reference posts also requires Send Messages. **Pin Messages** enables the optional response pin inside each listing. Missing pin permission, unavailable permission evidence or a full pin list never holds essential message delivery or acceptance. Attach Files and Embed Links enable the optional observations image and media; missing either permission keeps the text-only V2 response usable. In the feed it needs View Channel, Read Message History and Send Messages. Embed Links enables the status overview embed; without it, the overview uses concise text. Deny View Channel to `@everyone`; explicit access grants are limited to the configured moderator role, administrators and the bot. Existing Guild Members and Message Content intents must also be enabled in the Discord application.

Review `DiscordBot/Assets/recruitment/guidelines/*.md` and `tags.json` before enabling. Templates own the **entire native Guidelines** and contain one `{{code}}` plus the duration placeholders. Rendered Guidelines must fit the 1,000-character budget, including the code and configured durations. They point members to the pinned moderator/admin post at the top of the forum for fuller guidance; moderators maintain that post separately. Each tag definition has a stable key, display name, advisory group and closing flag. Keep keys stable when renaming a tag. The one closing tag is unmoderated. The shipped copy/tag groups are editable defaults pending moderator approval.

Local build/publish artifacts and Docker include the recruitment assets and bundled font. Use a persistent `ServerRootPath`; production remains one replica with `Recreate` deployment. Do not run two bot processes against the same state directory.

Keep member posting closed during initial setup. After configuring and restarting with `Enabled=true`, check `/recruitment status`, preview each forum, and verify native Guidelines, Require Tag and tag IDs. The bot provisions the pinned reference after complete discovery. Unexpected first-use content, direct Guidelines edits, changed managed tag permissions or missing known tags hold processing. `/recruitment repair` explicitly republishes owned setup after review, preserves unrelated tags, and can recreate a deleted managed tag. Discord cannot restore the old ID or its lost applications after a tag is deleted; inspect affected listings when repairing that case. Removing managed definitions is intentionally held for manual review.

Setup first publishes native Guidelines and selectable tags, verifies their assigned IDs and attributes, and only then enables Require Tag. An interrupted publication resumes from its saved intent. Other forums can finish setup while one is held; acceptance still waits for complete cross-forum inventory. Discord error `40066` means that no member-selectable tags were available for validation. Manage Channels alone does not resolve that tag-state rejection. The staged request path is covered by offline SDK tests; confirm the server's behavior in staging.

Discord.Net 3.20.1 drops tag moderation during forum serialization and can send `flags:0` for partial updates. The recruitment transport corrects only its scoped tag payloads, and every forum modification explicitly preserves other flags. Unrelated tags, moderation and emojis remain intact. Weekly topic-only rotation omits unchanged tags.

The persisted code rotates on Monday at 00:00 UTC. Publication is verified per forum; an interrupted rotation retries the same code. A challenge accepts its issued code and codes subsequently confirmed visible during its window. Codes are not included in advisories, staff entries, previews or diagnostic messages.

After setup and complete discovery, the bot reuses a discovered pinned staff reference or creates **Read First · Recruitment Information** in each forum. Existing staff and customized reference titles are preserved. The short introduction directs readers to staff guidance below and the native Guidelines; it does not repeat the rotating code. The reference uses the existing Closed tag, is pinned, locked and active (archiving would clear its pin), and is excluded from member challenges and capacity. Creation intent and the returned thread ID are saved before later writes. Interrupted creates recover by a bot-owned marker; incomplete inventory prevents duplicate creation. A deleted reference is replaced after discovery, and status links to each reference or shows its recovery fault.

Forum posts inherit parent permissions, so a single reference cannot have admin-only permission overwrites. Administrators and moderators with **Manage Threads** can unlock it if needed, add or edit their own messages, then relock it while leaving it active. The bot preserves those messages and rechecks the pin/lock on startup, recovery or an explicit repair/reconcile. It does not immediately relock a thread while staff are editing. [Discord thread permissions](https://docs.discord.com/developers/topics/threads)

`/recruitment manage` → **Designate reference post** still excludes an existing member post from challenges and capacity and removes its advisory. An accepted cooldown remains until explicitly waived. Pinning an ordinary member listing alone does not free its slot.

## Moderator controls

All commands are ephemeral and check the configured guild and the caller's current moderator role or administrator permission. Changes require an audit reason.

| Command | Purpose |
| --- | --- |
| `/recruitment status` | Forum/reference links, setup or reference faults, counts, inventory coverage, next work and recovery-job status. Reads a detached snapshot, including during startup or a delayed Discord request. |
| `/recruitment lookup id` | Retained history, bounded starter versions, private reports, audit reasons and same-server text/link matches. Matches are review hints; shared projects may be legitimate. |
| `/recruitment preview [forum]` | Proposed Guidelines with a masked code, and managed tag names. |
| `/recruitment repair reason [forum]` | Queue publication/verification of all forums or one selected forum after previewing it. |
| `/recruitment reconcile reason` | Retry inventory, message delivery and pending actions. |
| `/recruitment manage operation thread-id reason` | Exempt/unexempt, reset a pending acknowledgement, reset timeout streak or posting attempts, resolve reports, mark material changes reviewed, designate reference, close/reopen, or review uncertain evidence. |
| `/recruitment waive-cooldown author reason` | Clear the author's creation cooldown; never grants a second slot. |

**Confirm unavailable thread is absent** releases a held slot only when Discord availability is uncertain; it does not infer a deletion actor or change the cooldown. Reopening checks capacity. A Closed tag or explicit owner/moderator close still applies.

Repair and reconciliation return a short job ID promptly. The worker rechecks moderator authorization when the job runs; `/recruitment status` shows its latest result. Status uses the gateway's current member/role cache and never waits behind mutation work. Missing authorization evidence denies access.

The feed contains one fixed **Recruitment Status** overview, edited in place. Staff may pin it manually; the bot does not need Manage Messages. Updates coalesce for five seconds with at least 30 seconds between edits. Missing-message recovery and interrupted replacement/cleanup receipts are durable. Ordinary feed traffic does not move it. Cosmetic delivery failures do not block acceptance.

The overview links up to five listings needing report review, material-edit review, recovery, optional response-pin recovery or repeated-attempt attention. It also shows observed creations, accepted listings, closed unaccepted attempts, timeouts and mean prompt-to-accept delay for listings created in the past 24 hours. These are retained-record aggregates, not a complete event-analytics system. Use moderator review and recorded reasons to assess false positives before tightening thresholds.

The staff feed keeps one concise entry per attempt, with full details in lookup. A new unresolved report, first unreviewed material edit, newly triggered posting pause or third consecutive confirmed Guidelines timeout creates a separate review notice. Further timeouts do not repeat that threshold notice until a real acknowledgement or moderator reset clears the streak. Reports/material edits are grouped until reviewed. Delivery receipts recover ambiguous sends instead of duplicating notices. Wrong guesses are rate-limited and do not increment timeouts; independently missing threads or ambiguous delete responses do not invent timeout evidence. Routine queued identities log only in Debug builds; actual inventory/permission/recovery faults still warn.

### Private reports and evidence

**Report concern** opens a private form tied to the listing. Current members can submit 10–1,000 characters of context; self-reports, duplicate reporters on a listing, and more than three reports per person in 24 hours are rejected. A listing retains at most 20 reports. Additional/urgent evidence can go directly to moderators. Reports use the existing validated staff feed and local state; they never change public observations or expose report counts. The bot cannot inspect other people's DMs.

`resolve-reports` records a moderator's reason and resolves the listing's open reports. `review-changes` acknowledges outstanding changes to payment terms, links or attachments. Neither action endorses the author. First-observed/latest starter copies and up to three previous material versions are retained, each capped at 2,000 characters with truncation recorded. Up to eight specific URL paths and ten attachment metadata records are kept per version. “Original” means first observed, not guaranteed creation-time content. An unavailable starter does not erase earlier evidence. Per-listing notice receipts are bounded to eight and coalesce during prolonged delivery failures.

Reports and versions follow listing retention; active listings retain their bounded evidence. Access to the persistent volume/backups should remain restricted to operators. The bot adds no external classifier, link crawler, private-conversation scraper or public reputation database.

## Administrator portfolio sites

`/recruitment portfolio-sites operation [domain] [reason]` lists, adds or removes recognized portfolio hostnames. All replies are ephemeral. This command requires a current administrator in the configured guild; the moderator role alone does not grant access. Both command preconditions and the serialized engine enforce authorization. Add/remove require a nonempty audit reason. Recognition is an observation, never endorsement or proof of authorship.

The effective registry starts with `github.com`, `gitlab.com`, `artstation.com`, `behance.net`, `dribbble.com`, `itch.io`, `vimeo.com`, `youtube.com`, `youtu.be` and `deviantart.com`. It persists independently of settings, including an intentionally empty set, with at most 100 domains and the last 100 changes (actor, UTC time, operation, hostname and reason). Duplicate adds/removes are idempotent. Restarting does not restore removed defaults. Changes make retained open listings due for normal bounded reclassification (at most 20 per scheduled batch), with persisted per-listing registry versions for restart recovery. They do not create starter versions, material-edit notices, slot exemptions or enforcement decisions.

Enter a DNS hostname, such as `portfolio.example.org`, without a scheme, path, port, credentials or wildcard. Names are trimmed, lowercased and normalized to ASCII IDN form. Matching includes explicit subdomains at a dot boundary; `github.com.evil.org` and `evilgithub.com` do not match `github.com`. IP addresses, local/unlisted TLD names and bare ICANN public suffixes (including multi-label and wildcard rules) are rejected. Provider-private suffixes do not invalidate recognized creator platforms.

Initial URL rules are deliberately conservative and offline:

| URL form | Portfolio observation |
| --- | --- |
| Root of a supported platform | Ambiguous external link (yellow), not a work/profile link |
| Non-navigation profile/project path on GitHub, GitLab, ArtStation, Behance, Dribbble, itch.io, Vimeo or DeviantArt | Specific platform link (green), unless the work claim is negated |
| Creator subdomain root on itch.io, ArtStation or DeviantArt (excluding `www`) | Specific platform link |
| YouTube `/watch?v=<11-character ID>`, youtu.be video, `/shorts/<ID>` or `/embed/<ID>` | Specific video; video ID is preserved with case |
| YouTube `/@creator`, `/channel/<id>`, `/c/<name>` or `/user/<name>` | Specific creator link |
| Shared navigation such as `/explore`, `/search`, `/issues`, `/settings` or `/feed` | Ambiguous external link |
| Personal/unrecognized site, including its root; attached media without a supported link | Yellow clarification |
| Invite/contact-only links and negated work claims | Do not establish a portfolio; invites/contact-only links are excluded |

New registry domains use the same conservative path/navigation rules. This is not link crawling. The ICANN validator embeds `DiscordBot/Assets/recruitment/public-suffix-icann.dat`, copied from [the official Public Suffix List](https://publicsuffix.org/list/public_suffix_list.dat), upstream version `2026-10-07_07-28-19_UTC`, commit `3929462652695bad04f0a27afb600974014a3c8b`. Its upstream MPL-2.0 license notice is preserved. Only the ICANN section is bundled; runtime does not fetch it. Update this snapshot deliberately when registry boundaries change.

Reuse evidence retains at most eight canonical specific candidates per observed starter version within existing version/retention limits. Host case and default ports are normalized, fragments and known `utm_*`/click-tracking parameters are removed, while path case and YouTube video identity remain significant. Unknown query-dependent URLs have their entire query scrubbed in URL observations and are explicitly unsuitable for duplicate matching; they are never collapsed into a reusable base-path identity. Stored starter excerpts remain observed member content. Root landing pages and contact/invite links do not produce URL matches. Moderator lookup alone may show up to five different-author same-server matches against retained versions, with a reminder that shared projects can be legitimate. These hints never enter public output, Legend/member Details, reports, colors or enforcement; pruning listing evidence removes its hints.

## Persistence and recovery

State is stored in `{ServerRootPath}/recruitment/`:

- `recruitment-state.json`: schema `udc-recruitment-v2`, string IDs, UTC dates, per-forum setup/reference receipts and cursors, listing records, author aggregates, retry holds and overview delivery receipts, V2 media attachment IDs/filenames and archive/lock restoration intent, plus a separate optional message-pin receipt/fault.
- `recruitment-state.json.bak`: last validated snapshot before the latest replacement.
- `initialized`: matching store identity, used to detect a missing primary.
- `writer.lock`: exclusive process lease. The file may remain after shutdown; its existence does not itself mean the lease is held.

The state reader rejects unknown fields; do not run an earlier binary against snapshots containing fields it does not understand. Writes are serialized, flushed and replaced atomically. A write failure fences subsequent mutations until storage is repaired and the process restarted. Corrupt, unsupported, incomplete, missing or mismatched history is never silently replaced with empty cooldowns. Back up the directory and its identity receipt together. A completely lost volume cannot be distinguished locally from a new volume; existing nonempty forum setup holds first-use initialization. Restore history before repairing setup after a suspected volume loss.

For storage recovery, stop the bot, preserve the damaged files, restore a validated primary (or review the `.bak`) with its matching `initialized` receipt, then restart and reconcile. A backup may precede the latest acceptance/action; inspect the staff feed and Discord before restoring it. Do not change forum/guild mappings, delete the identity receipt, or clear author history to bypass a fault. The current unreleased bot writes its final state directly; no state migration or old-card conversion is provided.

The worker saves intent and staff evidence before destructive calls, then rechecks current identity and protection. Interrupted public sends recover by the exact marker in a recursive V2 Text Display, bot author and expected channel before retrying. Stored IDs and deletion use that same ownership predicate; a member copying the marker never grants ownership. Recovery scans are bounded; a scan that cannot establish completeness holds instead of duplicating a message. An interrupted delete that finds an absent thread records an unknown actor. Archived-message updates save a restoration intent, temporarily unarchive when necessary, and restore the saved archive/lock state after updating the bot's own response, including after a lost edit response or restart. [Discord thread rules](https://docs.discord.com/developers/topics/threads)

Public media filenames contain the SHA-256 digest of the actual image bytes. Identical reconciliation performs no message write or upload. Name/control-only edits retain the actual returned attachment ID; changed observations replace the single attachment. Returned IDs and filenames are persisted, while fresh message reads verify live attachments during recovery. Scoped recruitment response handling preserves component-only `media.attachment_id` metadata that Discord.Net 3.20.1 would otherwise discard, including resolved CDN media URLs. V2 sends/edits have no ordinary content or embeds. Discord.Net 3.20.1's recruitment-only scoped transport removes incompatible null fields and supplies retained attachment IDs on JSON edits; uploads still use the SDK's multipart path, shared request queue, cancellation and rate-limit handling. A definite rejected media write retries as text V2; ambiguous failures retain the saved intent and recover before another create. Optional media never determines the acknowledgement window. Staff entries and the overview retain their active standard formats. [Discord message edits](https://docs.discord.com/developers/resources/message#edit-message)

The bot attempts to pin the exact owned V2 response only after its returned message ID is saved and essential listing processing has completed. This pins a **message inside the thread**, separately from the forum-post pin. It never grants an exemption, frees a slot, delays acceptance, extends a deadline or reopens a closed listing. Pin-system messages, other people's pins and full pin lists are left intact. Once pin success is confirmed, subsequent manual unpins are respected, including restart, repair and explicit reconcile. A deleted response is replaced and the replacement gets its own pin attempt.

An unconfirmed pin retries by reading the actual message pin state first, so a lost response does not repeat a successful pin. Its separate receipt saves archive/lock restoration intent before any temporary unarchive; recovery restores that state even if the message is already pinned. Transient faults use bounded backoff (15 seconds to 15 minutes plus jitter); HTTP 429 respects the server retry deadline. Missing permission and full-capacity failures wait for a relevant permission/access change or explicit repair/reconcile; unfinished archive/lock restoration keeps a bounded retry even when the pin itself was rejected. These recovery commands retry unfinished pin work, including accepted and closed listings, while preserving confirmed manual unpins. A concise optional-pin diagnostic appears in staff status/overview; moderator lookup includes each response pin status, fault, retry eligibility and pending archive restoration. Storage-write failures remain fatal and require storage recovery; they are not cosmetic pin failures.

Discord.Net 3.20.1's `PinAsync` uses the deprecated `PUT channels/{channel.id}/pins/{message.id}` route. A recruitment-only execution scope corrects that exact channel/message PUT to `channels/{channel.id}/messages/pins/{message.id}` inside the existing SDK transport. Unscoped pins and other methods/identities pass through unchanged; authentication, cancellation, the request queue and rate-limit handling stay SDK-owned. Local installed-version tests verify payload routing and restoration; live permission and client behavior still require staging. [Discord message pins](https://docs.discord.com/developers/resources/message#pin-message)

Recruitment uses a single state writer and a bounded, coalesced event queue. Relevant thread/starter edits, member-role/avatar changes, deleted bot controls (including bulk deletion), permissions and readiness events drive targeted work. Acknowledgement deadlines, recovery and pending notifications have scheduled wake-ups. Ordinary acknowledgements do not run forum setup or enumerate all forums. A safety reconciliation runs every 15–18 minutes; incomplete inventory continues in bounded batches. Active-thread enumeration uses one guild request per discovery pass; newly found identities fetch their missing facts separately. The transport observes all identities before the SDK filters the response by forum. Member/starter facts and successful message renders are cached; targeted events or a bounded audit refresh them. Reference provisioning shares discovery instead of enumerating forums separately. Only work still waiting in the queue fences automatic actions; the event being processed does not cancel itself. State changes remain durable before external effects, and destructive actions retain fresh identity/protection checks.

Confirmed thread-deletion events release listing capacity and clear stale holds even while setup is unavailable. Bounded safety audits also probe permanently held records for deletions missed during downtime, without blindly retrying their writes. Invalid setup, permission and ownership failures otherwise hold until a relevant invalidation or explicit recovery. Transient failures back off with jitter. Recruitment hands actual HTTP 429 retry deadlines back to the scheduler, with conservative channel/guild scope and global handling, and retains the SDK's shared request queue. Known SDK bucket exhaustion also fails promptly rather than sleeping through minutes. Repeated identical fault logs are suppressed; status shows the scope and retry eligibility.

Startup, disconnects, lost queue events and delivery failures invalidate inventory evidence and restore full usable windows for pending attempts. Timely queued owner submissions and queued thread facts are processed before automatic actions; a valid acknowledgement is persisted using the Discord interaction timestamp before overdue timeout processing. Archive discovery resumes bounded pages across sweeps, with at most 50 new identities and 10 retained records processed per audit pass; scheduled listing work handles up to 20 due records at a time; incomplete inventory holds acceptance/actions. Retained known records are reconciled directly; terminal details expire after 12 months and inactive author aggregates after 24 months, while open/reference/held/action dependencies remain. The discovery floor prevents expired detail from being recreated by old archive scans.

## Validation before deployment

The offline tests cover policy, acknowledgement timestamps through the real queue, creation limits/deletion/restarts, persistence faults and current V2 delivery/pin/state round-trips, installed-SDK payloads, manual unpins, lost pin responses, archived pin restoration, rate-limit recovery, fixed-overview recovery, privacy/authorization/report limits, bounded evidence, neutral reader presentation, content counterexamples and effective avatars. They do not call live Discord.

Before enabling in production, exercise staging permissions and feed privacy; Guidelines/tag repair; acknowledgement and duplicate/cooldown/pause flows; closed controls; private reports and review notices; starter/attachment/profile edits; reference editing/relocking; optional message pins, missing Pin Messages and full pin capacity; manual unpins; restart recovery; and mobile readability. Confirm old accepted threads remain open and existing closures stay closed. Verify the mounted persistent directory, backup and graceful shutdown.

Consider **10 minutes of native forum creation slowmode** as a pilot, configured by staff. Discord's `rate_limit_per_user` for forum creation is separate from `default_thread_rate_limit_per_user` for replies; keep public clarification practical. Review native AutoMod spam/mention/keyword rules alongside the bot. These server settings are not changed by this implementation. [Discord forum fields](https://docs.discord.com/developers/topics/threads), [AutoMod FAQ](https://support.discord.com/hc/en-us/articles/4421269296535-AutoMod-FAQ)

No live bot login, command registration, staging mutation, deployment or remote push is part of local validation.

### Creation attempts

Every distinct recently observed member listing counts across the four forums, including attempts later deleted. A creation event retains the owner identity even when the thread disappears before its first read; confirmed absent attempts count after checking that the author is a current, non-exempt member. Missing creation events or unavailable membership cannot establish an attempt, which is another reason to use native creation slowmode. Defaults: `AttemptLimit: 2`, `AttemptWindowMinutes: 60`, `AttemptPauseMinutes: 60`. The third creation starts a pause; further creations during the pause do not extend it. Discovery retries and old historical listings do not consume attempts. Earlier eligible pending listings can still be acknowledged. Known slot/cooldown/pause conflicts are closed promptly, with their reason preserved.

Members can use `/recruitment eligibility` for private slot, cooldown and pause information. `/recruitment manage reset-attempts <threadId> <reason>` clears that author's attempt budget; it does not reopen closed attempts, waive the accepted-listing cooldown or grant a second slot.
