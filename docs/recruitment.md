# Recruitment

Recruitment supports three modes. **Observe** inventories listings and reports privately to
staff. **Advisory** adds Guidelines, a `Closed` tag, public summaries and practice owner
controls. **Enforce** adds acceptance, independent automatic-action gates and timeout
accounting. Checked-in dev/prod settings remain disabled; live staging is still required.

## Guidelines and staff setup

The four complete Markdown templates live in
`DiscordBot/Assets/recruitment/guidelines/`. `Recruitment:GuidelinesDirectory` selects their
relative directory under `Storage:AssetsRootPath`. They become the native forum's entire
Guidelines topic; there is no guidelines post to create or configure.

Each template must contain exactly one `{{code}}` in the labelled line
`UDC acknowledgement code: {{code}}`. Other supported tokens are
`{{acknowledgement_minutes}}`, `{{cooldown_days}}`, and `{{unanswered_days}}`. Unknown or
malformed tokens and rendered topics over 4096 characters are rejected. Templates are
read-only inputs and are included in .NET publish output and the Docker image.

With Advisory or Enforce running, empty unmanaged topics are initialized automatically. A nonempty
unmanaged topic, or any change to a previously owned topic (including clearing it), pauses
that forum until a configured moderator or administrator adopts it:

1. Run `/recruitment preview forum:<forum>`. Review the proposed text and the existing
   Guidelines in Discord. `ABCDE` in the private preview is a placeholder.
2. Run `/recruitment publish forum:<forum> expected-topic-hash:<fingerprint from preview>`.
   If the topic changed since preview, the command refuses to overwrite it.
3. For a renamed saved `Closed` tag, review the tag inventory too, then supply
   `repair-tag-hash:<tag fingerprint>` with publish to release that saved binding.
   Conflicting duplicate/moderated `Closed` tags must be resolved by staff first.

Preview works while the component is stopped or degraded. Publish requires a running
Advisory or Enforce lifetime but remains available during recoverable setup errors. Corrupt state or
invalid templates require operator repair and restart; preview does not reset state.

The bot matches `Closed` by trimmed, case-insensitive name and saves its resolved ID in
recruitment state. It appends one unmoderated tag when missing, preserving existing tag
IDs, names, emoji, moderation flags and order. It never removes a tag to make space or
changes `RequireTag`. A full inventory, conflicting matches, or a renamed saved tag pauses
setup. Discord does not provide conditional topic/tag writes: the adapter checks fresh
fingerprints before writing and verifies the result, but staff should avoid simultaneous
manual edits during publication.

## Codes and delivery recovery

Each forum gets a cryptographically generated five-character code, excluding ambiguous
characters. Rotation is due Monday at 00:00 UTC and runs on the next healthy scheduler
pass. A candidate and the expected prior topic fingerprint are saved before writing;
the code becomes usable only after Discord read-back confirms the complete topic. A lost
response is recovered from that candidate instead of generating another code. One forum's
failure does not advance its receipt or stop healthy forums from publishing.

An acknowledgement window accepts its issued code and later codes actually confirmed in that
same forum while it remains open. Code values are never included in public advisory/feed
messages, banners or component IDs; the forum Guidelines are the place to read them.

The public message contains account/server age bands, known activity, payment advice,
placement context, exact timestamps and owner controls. Its optional 900×360 PNG is an
initial snapshot; essential information is also in text and the attachment description.
Payment and practice status update in the live embed. The banner is capped at 512 KiB,
uses bundled fonts and existing process-wide Magick limits, and permits one banner render
at a time. Render/upload failure falls back to text and is visible in staff health/feed.

Before sending, the bot saves an intent. If a response is lost, it searches bot-owned
messages for `udc-recruit-advisory:<guild>:<thread>`, in bounded pages, before resending.
Do not remove this footer marker. A deleted advisory is replaced and pending acknowledgement gets
a fresh window. The full 30 minutes starts after usable controls are delivered. Restart,
gateway gaps, setup failure or unavailable delivery pauses pending acknowledgement and grants a fresh
window on recovery. Archived, locked, pinned and exempt posts are not automatically
reopened or prompted. Imported history receives no retroactive challenge. Staff can review it, adopt an eligible
listing, or explicitly restart acknowledgement.

## Owner controls

- **Acknowledge guidelines** opens a private code form. Guild, original thread, owner,
  current control generation and live post protection are checked again on submission.
  Five incorrect answers cause a one-minute retry delay; this is not failure history.
- **New practice window** renews an expired practice window without a penalty. Enrolled
  Enforce posts cannot self-renew when the timeout gate is enabled.
- **Close listing** is available after practice acknowledgement, or after acceptance for
  enrolled Enforce posts, and requires a two-minute confirmation. It locks/archives the post. Archived posts remain accessible in older posts
  and search. The bot adds `Closed` only when it fits; missing decoration is reported to
  staff without preventing closure or removing existing tags.
- **Remove my post** requires a two-minute confirmation and permanently deletes the post
  and replies. The confirmation is bound to the current listing state and acceptance value.
  Practice completion, a new prompt or changed acceptance invalidates an older confirmation.

An owner action is saved before the Discord call and confirmed by a fresh read afterward.
Recovery checks whether it already completed before retrying. Replayed confirmations
cannot repeat a completed action. Practice creates no accepted history, but explicit removal
preserves deletion history for any previously accepted listing. Adding `Closed` to an
unaccepted post withdraws its practice; Advisory performs no automatic lock/archive for it.

## Enforcement and recovery

Entering Enforce saves an enrollment boundary. New posts created from that boundary can
be enforced; earlier Observe/Advisory attempts remain practice until staff explicitly
adopts or restarts them. The boundary survives a process restart. Leaving Enforce and
entering it again creates a new boundary for unaccepted posts.

Acceptance is separate from the three mutation gates. At the end of the delivered
acknowledgement window, a successfully acknowledged, eligible enrolled post is accepted.
Each author can hold one Recruiting and one ForHire listing across paid/hobby forums.
Each group has independent creation/deletion cooldowns. Posting across groups adds a
placement reminder. A pending attempt reserves its group while it is evaluated.

| Gate | Automatic action |
| --- | --- |
| `EnforceGuidelineTimeouts` | Delete an enrolled, unacknowledged post after its usable window expires. |
| `EnforceListingLimits` | Lock/archive an acknowledged but ineligible enrolled attempt after its grace period. |
| `EnforceLifecycleClosures` | Lock/archive withdrawals marked Closed and accepted listings unanswered for the configured period. |

Automatic actions require fresh identity/protection checks, current policy evidence and a
confirmed staff-feed entry containing the durable intent. Timeout deletion also verifies
that the Guidelines and owned prompt remain available. Missing permissions, evidence or
delivery pauses action. Recovery of a pending acknowledgement grants a fresh full window;
it does not treat an outage as the author's failure. Unanswered closure additionally
requires complete response coverage and no unresolved historical gap. Natural archive
alone never means logical closure. Closed listings remain discoverable in Discord;
acknowledgement timeouts use deletion when their gate is enabled. Acknowledged but
ineligible attempts use lock/archive, as specified by the listing-limit policy.

A dispatched timeout is counted only after deletion is confirmed. Completion and its
counter change share one state transaction, so a lost response cannot count twice. A
correct enforced acknowledgement resets the consecutive counter. The third and later
consecutive timeouts add a staff alert to that attempt's stable feed entry; the alert is
retained even after a later reset. A disappearance before dispatch does not establish a
bot-caused failure.

## Moderator controls

Commands require the configured moderator role or administrator access in the configured
guild. Mutations run through the service's shared work gate and record actor and reason.
Thread parameters accept a complete Discord thread ID or channel mention.

| Command | Purpose |
| --- | --- |
| `status` | Read saved lifecycle, eligibility, counters and holds, including while stopped. |
| `reconcile` | Refresh a thread and its staff entry while the service runs. |
| `adopt` | In Enforce, explicitly accept a reviewed historical listing if capacity and cooldown allow it. |
| `restart-acknowledgement` | Give an open, unaccepted listing a fresh window; enroll it if Enforce is running. |
| `exempt` | Add/remove protection from automatic policy actions; cancel any outstanding action. |
| `waive-cooldown` | Waive the group's current creation/deletion anchors. Future anchors and occupied capacity still apply. |
| `reset-timeouts` | Reset the consecutive counter while retaining the audit trail. |
| `review` | Resolve a finding and optionally attest historical response evidence. Known qualifying replies cannot be erased. |
| `resolve-missing` | Record a verified UTC deletion time for a still-missing thread and resolve its history hold. |
| `close` / `reopen` | Explicit lifecycle changes. Reopen removes Closed, checks group capacity and grants an exemption to prevent immediate reclosure. |
| `remove` | Prepare a private two-minute deletion confirmation bound to the staff member and current listing state. |

These are `/recruitment` subcommands. Public mutations need Advisory or Enforce running.
Reconcile and review metadata remain available in Observe; public setup failures do not
block staff metadata repair. Corrupt state requires stopped operator recovery as described
in the deployment guide. No command clears a Discord pin. To resume normal policy after
reopening, staff explicitly removes the exemption.

Response review distinguishes clearing a finding from claiming complete historical
coverage. An explicit absence attestation clears uncertainty only through that review
instant; a subsequent fresh scan is still required before unanswered closure. Restarting
acknowledgement does not silently clear a separate history review hold.

## State and retention

This feature deploys as one initial **SchemaVersion 1**. There are no recruitment legacy
settings projections or earlier-schema upgrades. Unsupported versions and corrupt state
fail closed; validated backup recovery remains available to operators.

A daily metadata pass removes resolved terminal post details after 12 calendar months and
inactive author aggregates after 24 months. Open listings, unresolved history, pending
actions, recent moderation and unfinished delivery retain their dependencies. Accepted
creation/deletion anchors are compacted before post removal. Minimal thread-ID retirement
markers remain so archive scans cannot recreate expired records; they contain no author
identity. Retired threads cannot be adopted/reopened through expired local records.
Discord posts, staff-feed messages, XP and karma are untouched by this cleanup.

## Reading and changing the implementation

Start with `RecruitService.ProcessWorkAsync`: observations run before public reconciliation, enforcement and retention.
Its work gate serializes scheduler work with owner/staff mutations. Stop cancels and drains
both before releasing the state writer. The InteractionService remains the sole dispatcher;
the new modules do not add raw interaction subscriptions. Modal opening has a two-second
local-check budget because Discord requires it as the initial response; submission defers
privately before network checks.

| File | Responsibility and reason for the boundary |
| --- | --- |
| `RecruitmentPublicCoordinator` | Confirm forums, deliver controls and fresh windows, and reconcile public messages in Advisory/Enforce. |
| `RecruitmentEnforcementCoordinator` | Refresh evidence, accept eligible listings at the deadline, and request gated automatic actions. |
| `RecruitmentLifecycleExecutor` | Persist shared owner/automatic/moderator intents, revalidate before dispatch, and atomically record confirmed outcomes. |
| `RecruitmentGuidelines` / `RecruitmentGuidelinePublisher` | Pure template/code utilities versus durable Discord publication and adoption. |
| `RecruitmentOwnerActions` / `RecruitmentStaffActions` | Validate caller intent and confirmations; delegate lifecycle mutations to the shared executor. |
| `RecruitmentRetention` | Compact resolved history without Discord mutations or archive rediscovery. |
| `RecruitmentAdvisoryMessage` / `RecruitmentBannerRenderer` | Live, accessible text and controls versus an immutable initial image. |
| `IRecruitmentPublisher` / `DiscordRecruitmentPublisher` | Public Discord boundary, deliberately separate from Observe's read/feed-only adapter. |
| Publication/lifecycle models and validation | Schema-1 receipts, control generations, actions and audit records; no Discord objects are persisted. |

State-store callbacks only change metadata. Network calls stay outside those transactions.
Discord delivery and file writes cannot form one transaction: intent, read-back and stable
identity are what make an interrupted operation recoverable. Tests use the same store with
a fake publisher and clock to exercise those interruptions.

## Validation and staging

Run the suite with `dotnet test DiscordBot.sln --configuration Release --no-restore`.
It covers publication/adoption, rotation, delivery interruptions, text fallback, owner
replays, enforcement gates, timeout recovery, moderation, retention, unsupported-schema
rejection, stop/restart cancellation and concurrent profile/banner renders.
Generate an offline preview from the repository root with:

```sh
dotnet run --project DiscordBot -- --recruitment-preview /tmp/recruitment.png DiscordBot/Assets
docker build --platform linux/amd64 -t udc-bot:recruitment-check .
```

The Docker build runs both profile and recruitment native smoke checks. A live staging
check still needs the development forum/feed IDs and Discord access. Before production:

- Start Advisory in four disposable forums; verify empty setup, existing-topic adoption,
  renamed/full/duplicate tag handling, and the required permissions in the deployment guide.
- Inspect Guidelines access, image/text readability, attachment descriptions, the modal,
  timestamps and ephemeral confirmation flows in desktop and mobile Discord.
- Remove Attach Files and then Send Messages in Threads to verify fallback versus paused
  delivery; restore access and confirm a fresh full window.
- Restart during publication, advisory delivery and a confirmed owner action. Verify one
  public message, recovered outcomes, and no automatic timeout removal or cooldown.

- Then stage Enforce with disposable new posts. Enable each gate separately and verify
  timeout deletion, listing rejection, acceptance, unanswered closure and Closed withdrawal.
- Interrupt a dispatched action and restart; verify one recorded outcome/counter update.
  Stop/restart via component controls while work runs and confirm no calls outlive stop.
- Exercise moderator review, waiver, exemption, missing-history resolution, reopen and
  confirmed removal. Confirm unauthorized users and stale/replayed confirmations fail.

Production timeouts remain 30 minutes; accelerated clocks belong in tests/staging only.
