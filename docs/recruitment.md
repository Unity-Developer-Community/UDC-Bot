# Recruitment Advisory

Advisory publishes Guidelines, ensures a `Closed` tag, and delivers a public fact summary
with a private practice acknowledgement form. It permits explicitly confirmed owner
closure/removal. It never automatically deletes, archives, accepts a listing, starts a
cooldown, or counts an acknowledgement failure. Enforce is unavailable in this build.
Checked-in dev/prod settings remain disabled.

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

With Advisory running, empty unmanaged topics are initialized automatically. A nonempty
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
Advisory lifetime but remains available during recoverable setup errors. Corrupt state or
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

A practice window accepts its issued code and later codes actually confirmed in that
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
Do not remove this footer marker. A deleted advisory is replaced and pending practice gets
a fresh window. The full 30 minutes starts after usable controls are delivered. Restart,
gateway gaps, setup failure or unavailable delivery pauses practice and grants a fresh
window on recovery. Archived, locked, pinned and exempt posts are not automatically
reopened or prompted. Imported history receives no retroactive challenge; staff adoption
belongs to the later enforcement/recovery chunk.

## Owner controls

- **Acknowledge guidelines** opens a private code form. Guild, original thread, owner,
  current control generation and live post protection are checked again on submission.
  Five incorrect answers cause a one-minute retry delay; this is not failure history.
- **New practice window** renews an expired window without a penalty.
- **Close listing** is available after practice acknowledgement and requires a two-minute
  confirmation. It locks/archives the post. Archived posts remain accessible in older posts
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

## Reading and changing the implementation

Start with `RecruitService.ProcessWorkAsync`: observations run before public reconciliation.
Its work gate serializes scheduler work with owner/staff mutations. Stop cancels and drains
both before releasing the state writer. The InteractionService remains the sole dispatcher;
the new modules do not add raw interaction subscriptions. Modal opening has a two-second
local-check budget because Discord requires it as the initial response; submission defers
privately before network checks.

| File | Responsibility and reason for the boundary |
| --- | --- |
| `RecruitmentAdvisoryCoordinator` | Confirm forums, process a bounded batch of posts, and summarize recovery; it never evaluates automatic enforcement. |
| `RecruitmentGuidelines` / `RecruitmentGuidelinePublisher` | Pure template/code utilities versus durable Discord publication and adoption. |
| `RecruitmentOwnerActions` | Validate intent, consume confirmations, persist actions and recover outcomes. Call through the managed work gate. |
| `RecruitmentAdvisoryMessage` / `RecruitmentBannerRenderer` | Live, accessible text and controls versus an immutable initial image. |
| `IRecruitmentPublisher` / `DiscordRecruitmentPublisher` | Public Discord boundary, deliberately separate from Observe's read/feed-only adapter. |
| `RecruitmentPublicationModels` / `RecruitmentPublicationValidation` | Schema-v3 receipts, control generations and action intents; no Discord objects are persisted. |

State-store callbacks only change metadata. Network calls stay outside those transactions.
Discord delivery and file writes cannot form one transaction: intent, read-back and stable
identity are what make an interrupted operation recoverable. Tests use the same store with
a fake publisher and clock to exercise those interruptions.

## Validation and staging

Run the suite with `dotnet test DiscordBot.sln --configuration Release --no-restore`.
It covers publication/adoption, rotation, delivery interruptions, text fallback, owner
replays, lifecycle cancellation, schema migration and concurrent profile/banner renders.
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

Production timeouts remain 30 minutes; accelerated clocks belong in tests/staging only.
