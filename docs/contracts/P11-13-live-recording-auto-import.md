# P11-13 — Live recording import: automatic (Google Workspace) or by hand (personal Google account)

Status: **FROZEN** (2026-10-10). Owner decision (2026-10-10): "build it now, wait for the future Workspace account; support both account kinds —
a personal Google account uploads by hand (the instructor picks the clip), a Workspace account is fully automatic."
Builds on P11-06 (`AttachSessionRecording` — a recording is an ordinary lesson) and P11-03 (instructor Google account).

## 1. Behaviour in one page

| Instructor's Google account | What the platform does after a class |
|---|---|
| **Personal** (`hd` claim absent — Gmail etc.) | Nothing automatic. The session row says "upload the recording yourself" (the existing manual upload + attach). Unchanged. |
| **Workspace**, recording scopes not yet granted | Same as personal, plus a card "Turn on automatic recording import" (a second, optional Google consent). |
| **Workspace**, recording scopes granted, feature on | Fully automatic: after the class ends the platform finds the Meet recording, copies it to Bunny Stream, waits for transcoding, attaches it as a lesson, tells the instructor. Nothing to click. |

Account kind is **detected, not asked**: Google's userinfo `hd` (hosted domain). `hd` present = Workspace. (`hd` says "Workspace", not "can record":
a Workspace plan without recording simply yields no file — see `NoRecording` below, which falls back to the manual upload.)

The whole automatic path is behind `Live:Recording:AutoImport:Enabled` (default **false**): the Google app is unverified for the Restricted scope
(`drive.meet.readonly`), so the owner turns it on when a Workspace account exists. With it off every instructor sees only the manual path.

## 2. Configuration (`LiveOptions.Recording`, section `Live:Recording:AutoImport`, Options + `ValidateOnStart`)

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Master switch. Off: no discovery, no import rows, no consent button, `mode` is always `Manual`. |
| `FirstSearchDelayMinutes` | 10 | First search this long after the scheduled end. |
| `SearchWindowHours` | 12 | Keep looking this long after the scheduled end; then `NoRecording`. |
| `MaxAttempts` | 6 | Transient failures (network, 5xx, 429) before `Failed`. |
| `MaxFileSizeMegabytes` | 8192 | Larger files are refused (`file_too_large`). |
| `TransferLeaseMinutes` | 180 | A `Transferring` row not finished by then is reclaimed and restarted. |
| `BatchSize` | 5 | Rows processed per job run. |
| `DevSampleFilePath` | empty | Dev only (Live:Provider=Logging): the file the fake recording provider serves. |

Validation: all positive, `SearchWindowHours` ≤ 72. `DevSampleFilePath` must be empty in Production (`ProductionConfigurationGuard`).

## 3. Data (Live schema, UPPERCASE convention, one additive migration `AddLiveRecordingImport`)

**`LIVE.INSTRUCTOR_GOOGLE_ACCOUNTS`** gains
- `HOSTED_DOMAIN varchar(255) NULL` — userinfo `hd`, stored at every connect/reconnect. `NULL` = personal.
- `ACCOUNT_KIND_CHECKED_AT_UTC timestamptz(3) NULL` — when `HOSTED_DOMAIN` was last read from Google. Rows connected before this change have `NULL`: the account is
  reported `accountKind: "Unknown"` until `TryResolveAccountKindAsync` (best effort, once, on the next status read or import pass, needs a valid access token) fills it.
- Domain: `AccountKind` computed (`Workspace` when `HOSTED_DOMAIN` non-empty, `Personal` when checked and empty, `Unknown` when never checked);
  `HasRecordingScopes` computed from `SCOPES` (`GoogleScopes.HasRecordingScopes`); `Reconnect(...)` / `Connect(...)` take the hosted domain.

**`LIVE.SESSION_RECORDING_IMPORTS`** — one row per live session at most (UNIQUE `SESSION_ID`).

| Column | Type | Notes |
|---|---|---|
| `SESSION_RECORDING_IMPORT_ID` | uuid PK | UUIDv7 |
| `SESSION_ID` | uuid, unique | the Catalog live session (no FK, cross-schema) |
| `COURSE_ID` | uuid | |
| `INSTRUCTOR_USER_ID` | uuid | owner; used to get the access token and as asset owner |
| `STATUS` | varchar(24) | `RecordingImportStatus` below |
| `ATTEMPTS` | int | transient failures so far in the current run |
| `NEXT_ATTEMPT_AT_UTC` | timestamptz(3) null | when the job may touch it next |
| `LEASE_UNTIL_UTC` | timestamptz(3) null | set while `Transferring` |
| `GOOGLE_RECORDING_NAME` | varchar(200) null | Meet resource name |
| `GOOGLE_FILE_ID` | varchar(200) null | Drive file id |
| `MEDIA_ASSET_ID` | uuid null | the Bunny asset once created |
| `EPISODE_ID` | uuid null | the lesson once attached |
| `ERROR_CODE` | varchar(60) null | stable code, never a message with ids/urls |
| `SEARCH_UNTIL_UTC` | timestamptz(3) | `session end + SearchWindowHours` |
| `COMPLETED_AT_UTC` | timestamptz(3) null | terminal states |
| `ROW_VERSION` | bytea | concurrency token (`ConcurrencyTokenInterceptor`) |
| audit | `CreatedAtUtc` … | PascalCase C# names (interceptor rule) |

Index `(STATUS, NEXT_ATTEMPT_AT_UTC)` for the job. No cascade deletes. Identifier names ≤ 63 bytes (architecture test).

`RecordingImportStatus`: `Waiting` (looking for the recording), `Transferring` (copying Drive → Bunny), `Processing` (Bunny is transcoding),
`Attached` (done), `NoRecording` (window ended with no file → manual), `Failed` (gave up → manual or retry), `NeedsReconnect` (token revoked or scope
missing → instructor must reconnect/consent), `Skipped` (nothing to import: session cancelled, or a recording lesson already exists).
Terminal: `Attached`, `NoRecording`, `Failed`, `NeedsReconnect`, `Skipped`. `Failed`/`NoRecording`/`NeedsReconnect` may be reset to `Waiting` by Retry (§6).

## 4. Google (Siri.Integrations.Google) — interfaces already in the tree

`IGoogleMeetRecordingProvider` (file `IGoogleMeetRecordingProvider.cs`), `GoogleScopes.MeetSpaceReadonly/DriveMeetReadonly/RecordingScopes/HasRecordingScopes`,
`GoogleUserInfo.HostedDomain`, `IGoogleOAuthService.BuildRecordingAccessAuthorizationUrl`. Real implementation uses plain `HttpClient` like the rest (no SDK):

- find: `GET https://meet.googleapis.com/v2/conferenceRecords?filter=space.meeting_code="{code}"` (URL-encoded; paginate; keep records whose start is in the window),
  then `GET https://meet.googleapis.com/v2/{conferenceRecordName}/recordings`. Map `STARTED/ENDED/FILE_GENERATED`; `driveDestination.file` = Drive file id.
- download: `GET https://www.googleapis.com/drive/v3/files/{id}?alt=media&supportsAllDrives=true` with `HttpCompletionOption.ResponseHeadersRead` (stream); size from
  the response `Content-Length` (fallback `files.get?fields=size,name`). **Verify these against Google's current Meet REST / Drive docs before coding**
  (`developers.google.com/meet/api/reference/rest/v2/conferenceRecords.recordings`, `.../guides/artifacts`).
- Errors: 401 → `google.unauthorized`; 403 (missing scope / no access) → `google.forbidden`; 404 → `google.not_found`; 429/5xx → `google.transient`.
- `LoggingGoogleMeetRecordingProvider` (dev fake, only with `Live:Provider=Logging`): returns one `FileGenerated` recording once the window opened and serves the file at
  `Live:Recording:AutoImport:DevSampleFilePath` (a few KB of bytes if unset). The fake OAuth service reports a hosted domain for fake accounts whose e-mail ends with
  `@workspace.example.test` and supports `BuildRecordingAccessAuthorizationUrl`.
- OAuth state (`GoogleOAuthState`) gains `Purpose` (`Calendar` default | `RecordingAccess`). Callback for `RecordingAccess` requires **both** recording scopes in the grant
  (else error reason `recording_scope_missing`, nothing stored); it also requires the calendar scope, as today. It stores the new refresh token/scopes via the same `Reconnect`.

Meeting code: parse the last path segment of the decrypted `SESSION_MEETINGS.MEET_URL_ENCRYPTED` (`https://meet.google.com/abc-defg-hij`), accept only `^[a-z]{3}-[a-z]{4}-[a-z]{3}$`,
never log URL, code or file id.

## 5. Other modules

- **Siri.Integrations.Video**: `IVideoProvider.UploadVideoAsync` (declared). `BunnyVideoProvider`: `PUT https://video.bunnycdn.com/library/{LibraryId}/videos/{videoId}`,
  `AccessKey` header, `StreamContent` + `Content-Length` when known, no retry, dedicated named `HttpClient` with `Timeout = InfiniteTimeSpan` (the caller's token bounds it).
- **Siri.SharedKernel.Contracts.IMediaIngestContract** (declared) implemented by Media (`MediaIngestContractService`): `CreateVideoAsync` → `MEDIA_ASSET.Create` (owner = given user) → `UploadVideoAsync`
  → `MarkProcessing` → save; on failure delete provider video + remove row. `BunnyTranscodePollJob`/webhook then move it to `Ready` (the poll job already selects `Processing` assets).
- **Catalog `ILiveRecordingAttacher`** (declared) implemented over `AttachSessionRecordingHandler` (EndedLiveSession/AttachedLiveRecording map from COURSE_LIVE_SESSION; instructor user id via
  `InstructorProfile.UserId`). Default lesson title: the handler's own `บันทึก: {session title}`.

## 6. Live module behaviour

**Capability** (pure function, unit-tested) of an instructor: `Manual` when the feature is off, or provider is not `GoogleMeet`, or no connected/active account, or account kind is `Personal`
or `Unknown`; `AutoNeedsConsent` when `Workspace` and recording scopes are missing; `Auto` when `Workspace` and scopes granted.

**Job `live-recording-import`** (`LiveRecordingImportJob`, recurring `*/5 * * * *`, `[DisableConcurrentExecution]`, registered in `RecurringJobsRegistration` and wherever the
other Live jobs are scheduled for the API-hosted server), no-op when the feature is off:
1. *Discovery*: `ILiveRecordingAttacher.ListEndedAsync(now-48h, now - FirstSearchDelayMinutes)` → for each session with no import row: cancelled or `HasRecording` → no row
   (a row is only written for work); instructor capability `Auto` and a Google meeting exists → insert `Waiting` (`NEXT_ATTEMPT_AT_UTC = end + FirstSearchDelay`, `SEARCH_UNTIL_UTC = end + SearchWindowHours`).
   Capability not `Auto` → no row (the UI shows the manual path).
2. *Work*: pick due rows (`STATUS in (Waiting, Transferring-with-expired-lease, Processing)` and `NEXT_ATTEMPT_AT_UTC <= now`), `BatchSize`, oldest first, claim with the row version.
   - Always first: re-read the session; `HasRecording` or cancelled → `Skipped`.
   - `Waiting`: get an access token (`InstructorGoogleAccountService.TryGetAccessTokenAsync`; unauthorized → `NeedsReconnect`). `FindRecordingsAsync(code, end-6h .. end+SearchWindowHours)`.
     None/`Started`/`Ended` → stay `Waiting` with backoff (10 m, 20 m, 40 m, then hourly); after `SEARCH_UNTIL_UTC` → `NoRecording`. A `FileGenerated` recording: if several, take the longest
     (largest `EndedAt - StartedAt`); `google.forbidden` → `NeedsReconnect` (`recording_scope_missing`); then → `Transferring` (lease).
   - `Transferring`: open the download; size > `MaxFileSizeMegabytes` → `Failed(file_too_large)`; `IMediaIngestContract.IngestAsync(instructor, "บันทึก: {title}", stream, length)`;
     success → store `MEDIA_ASSET_ID`, `Processing`, next attempt in 2 min. Transient failure → `Waiting`/retry with attempts+1 and backoff, `Failed` after `MaxAttempts`.
     Drive `not_found` → `NoRecording`. A reclaimed expired lease first deletes the half-made asset (`MEDIA_ASSET_ID` set) if any.
   - `Processing`: `IMediaAssetContract.GetAssetSummaryAsync`; `Ready` → `ILiveRecordingAttacher.AttachAsync` → `Attached` (+`EPISODE_ID`, `COMPLETED_AT_UTC`, alert e-mail "recording added");
     `Failed` → `Failed(transcode_failed)`; still processing after 6 h → `Failed(transcode_timeout)`; attach `asset_in_use`/already attached → `Attached`/`Skipped` idempotently.
   - On `Failed` and `NeedsReconnect` send the instructor one alert (existing `IInstructorAlertSender`, once per row) explaining the fallback: upload by hand.
3. Every state change is one `SaveChangesAsync`; no external call inside a DB transaction; the job never throws out of a row (log the code, keep going).

**Retry**: `RecordingImportService.RetryAsync(instructorUserId, sessionId)` — owner only (404 for unknown/not-owner is NOT used here: a session id is not secret from instructors; follow Catalog's convention: unknown = 404, someone else's = 403);
allowed for `Failed`/`NoRecording`/`NeedsReconnect` rows (and for a session with no row whose capability is `Auto` → creates `Waiting` immediately), within 30 days of the end; resets attempts, sets `Waiting`, next attempt now. Otherwise 409 `live.recording_import_not_retryable`.

## 7. HTTP (Live controllers, additive — nothing is removed or renamed)

`GET /api/live/instructor/google/status` response adds:
```json
{ "accountKind": "Personal" | "Workspace" | "Unknown" | null,
  "hostedDomain": "example.com" | null,
  "recording": { "mode": "Manual" | "AutoNeedsConsent" | "Auto", "autoImportAvailable": true, "scopesGranted": false } }
```
`accountKind` is `null` when not connected. `autoImportAvailable` = server feature flag on. `mode` = the capability of §6. (`Unknown` is shown as "checking" by the UI and resolves after the best-effort lookup.)

`POST /api/live/instructor/google/recording-access/connect` body `{ "returnPath": "/instructor/live-settings" }` → `200 { "authorizationUrl": "…" }`.
`409 live.recording_not_available` when the feature is off or the account is not Workspace; `503` as the calendar connect; same rate limit policy as the calendar connect. The existing callback finishes it
(`?google=connected` / `?google=error&reason=recording_scope_missing|…`).

Instructor session list item and detail gain
```json
"recordingImport": { "mode": "Manual" | "Auto",
                     "status": null | "Waiting" | "Transferring" | "Processing" | "Attached" | "NoRecording" | "Failed" | "NeedsReconnect" | "Skipped",
                     "errorCode": null | "…", "nextAttemptAtUtc": null | "…Z", "canRetry": false }
```
`mode` = `Auto` when a row exists or the instructor's capability is `Auto`, else `Manual` — **but always `Manual` while `Live:Recording:AutoImport:Enabled=false`, even for a session that already has a row** (§2 wins; QA finding F4, decided 2026-10-10). With the flag off `canRetry` is `false`; `status` still shows what the row says. `status` is `null` when there is no row. `canRetry` per §6. Never a Drive id, asset id, URL or message text.

`POST /api/live/instructor/sessions/{sessionId}/recording-import/retry` → `200` the same `recordingImport` object; 404/403/409 as §6. Rate-limited like the room endpoints.

Error codes added to `LiveErrors`/reasons: `live.recording_not_available`, `live.recording_import_not_retryable`, `recording_scope_missing` (callback reason).

## 8. UI (siri_upskill_ui, th + en i18n, loading/error/empty states, tests)

- **Live settings page** (`/instructor/live-settings`): when connected show the account kind ("Personal Google account" / "Google Workspace · {domain}"; `Unknown` → "checking…") and a **Recording** card:
  `Manual` → text "after a class, upload the recording yourself" (+ for Personal: why: only Google Workspace can import automatically); `AutoNeedsConsent` (only when `autoImportAvailable`) →
  explanation + button "Turn on automatic recording import" (calls the new connect endpoint, redirects like the calendar connect; callback toast for `recording_scope_missing`); `Auto` → "On — recordings are added to the course automatically".
  When `autoImportAvailable` is false the Recording card shows only the manual text.
- **Session rows** (course builder live tab `live-session-row`, instructor sessions page): for an ended session show the recording block by `recordingImport`:
  no recording + `Manual` → existing upload (unchanged); `Auto` + `Waiting/Transferring/Processing` → "Importing the recording automatically…" (status text, no upload button needed but upload stays available);
  `Attached` → existing "recording attached"; `NoRecording` → "No recording was found — upload it yourself" + upload + Retry; `Failed` → "Import failed" + Retry + upload; `NeedsReconnect` → "Reconnect Google to allow import" + link to live-settings + upload; `Skipped` → nothing.
  Poll the list while any ended session is `Waiting/Transferring/Processing` (reuse `pollWhile`, 30 s).
- Models mirror §7 exactly (`RecordingImportInfo`); no mock data; i18n keys under `live.recordingImport.*`.

## 9. Security / privacy / ops notes

- Tokens, meeting codes, Drive ids, Bunny ids and URLs are never logged or returned (codes only). The Drive stream is never buffered whole.
- The import acts only for the session's own instructor and only attaches assets that instructor owns (the asset is created for them).
- A recording shows the learners' faces/voices and becomes a lesson all enrolled learners can watch: the instructor is e-mailed when it is added; removal of a lesson from a sold course remains blocked by the domain
  (the instructor can replace it by attaching another recording). Owner may set `Enabled=false` at any time; in-flight rows simply stop being processed.
- Restricted scope: until Google verifies the app, only ≤ 100 users can consent (with the "unverified app" warning). Documented in the runbook; the feature ships **off**.
- Migration is additive; the owner applies it (`scripts/migrate-bundle.sh`) before deploying the image. Do not apply it to the shared/production database from an agent session.

## 10. Test expectations

Unit: capability matrix; import state machine for every transition above (fake provider/clock/ingest/attacher); backoff schedule; meeting-code parsing (accept/reject); Meet/Drive client against a fake `HttpMessageHandler`
(pagination, states, 401/403/404/429, streaming not buffering, nothing sensitive in logs); `hd` → account kind; OAuth state purpose + callback scope rules; Bunny PUT request shape; media ingest success + cleanup on failure;
Catalog attach contract mapping; controllers' contract test lists the two new routes. UI: models/service, settings page states, session-row states + retry + polling.
Integration (Testcontainers/external services mode): migration applies; import row repository unique-per-session; retry endpoint ownership.
