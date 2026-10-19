# MyLife API contract

All authenticated endpoints accept the `AccessToken` HttpOnly cookie or a Bearer access token. Errors use `{ "success": false, "message": "..." }`; validation responses may additionally contain `errors`.

FamilyTree/Library business errors also include a machine-readable `code` for
frontend localization. HTTP status and English diagnostic messages are retained.
Frontend owns success messages, prefers error codes, then status/action-based
i18n fallbacks; it does not render raw backend messages in these features.
Automatic model-validation ProblemDetails may have `errors` without `code`.

Browser mutations send `credentials: include` and `X-Requested-With: MyLife`; JavaScript never receives or stores tokens. Native clients send `X-Client-Platform: mobile` and receive token fields in login/Google/refresh responses.

## Auth

| Method | Route | Notes |
| --- | --- | --- |
| POST | `/api/login` | Local credentials. Returns `user`, tokens for native clients, and `accessTokenExpiresIn` / `refreshTokenExpiresIn`. |
| POST | `/api/auth/register` | Creates a local account. |
| POST | `/api/auth/google` | Send verified Google `idToken`, or web `code` plus `redirectUri`; never send an email as a credential. |
| POST | `/api/refresh-token` | Rotates the refresh token. Web uses cookies; native clients send both tokens in the body. |
| POST | `/api/logout` | Revokes the current refresh token and clears cookies. |
| GET | `/api/me` | Source of truth for the session and role. |
| PUT | `/api/me/profile` | Authenticated profile update. |
| POST | `/api/auth/change-password` | Local-provider accounts only. |

Account identity fields are distinct: `fullName` is the original/account name captured from local registration or the initial verified Google profile; `displayName` is the user-editable UI name. New accounts derive `displayName` by normalizing whitespace and combining the first and last words of `fullName` (for example, `Nguyễn Huỳnh Minh Đức` becomes `Nguyễn Đức`). Google relogin does not overwrite either stored name. The additive `users.display_name` migration backfills from the previously visible `full_name` value to preserve existing custom names; only when `full_name` is blank does it fall back to the email local-part. Profile updates accept `displayName`, `phoneNumber`, `gender`, and `dateOfBirth`; `fullName` is not an editable profile request field. `/api/me` and auth responses return both `fullName` and `displayName`; the legacy `name` response alias now means `displayName`.

Register errors use machine-readable codes with unchanged semantic HTTP statuses:
`AUTH_PASSWORD_MISMATCH`, `AUTH_PASSWORD_TOO_LONG`, `AUTH_GMAIL_REQUIRED`,
`AUTH_EMAIL_ALREADY_REGISTERED` (409), `AUTH_DOB_INVALID`, and
`AUTH_SERVICE_NOT_INITIALIZED` (503). `RATE_LIMITED` remains 429. Other field
validation errors retain the standard validation response.

`user.id` is an integer and `role` is always `ADMIN` or `USER`. The business model is ADMIN XOR USER: exactly one supported role. The compatibility `roles` array contains only that primary role. Ambiguous supported-role assignments are refused by login, refresh and JWT validation. ADMIN does not imply USER. `authProvider` is retained only as legacy account-origin data; `loginProviders.local` and `loginProviders.google` describe usable methods.

## Avatar

| Method | Route | Notes |
| --- | --- | --- |
| GET | `/api/avatar/{email}` | Redirects to the current Drive-backed avatar. |
| POST | `/api/avatar/upload` | Authenticated multipart upload (`file`, max 5 MB); replaces the user's existing Drive file and returns a cache-busted `avatarUrl`. |
| DELETE | `/api/avatar` | Deletes the Drive file first, then clears avatar metadata. |

`users.avatar_drive_file_id` is the stable storage identity. `avatar_source` is `MANUAL`, `GOOGLE`, or null. A manual avatar is never overwritten by Google login. Google profile images are copied to Drive only when missing or changed; storage failure does not invalidate an otherwise successful Google authentication.

The deployable Apps Script implementation and its backend payload contract are in `GOOGLE_APPS_SCRIPT_AVATAR.gs`.

## Library / albums — Phase 1

Every route below requires an active account with role `USER`. Admin-only
accounts receive 403; anonymous requests receive 401. Albums belong to their
`created_by_user_id`; this ID comes from the session, never a client field.
An inaccessible album/photo returns 404, and list includes only the caller's albums.

| Method | Route | Body / result |
| --- | --- | --- |
| POST | `/api/library/albums` | JSON `{ "name": "Gia đình 2026", "description": "..." }`; 201 `AlbumDto`, Location points to detail. |
| GET | `/api/library/albums` | 200 `AlbumDto[]`, updatedAt descending, id descending tie-break. |
| GET | `/api/library/overview` | 200 `LibraryOverviewDto`; USER-only and current-user isolated. Includes the caller's categories and ordered album summaries with ordered photo metadata. Contains no image bytes and makes no Google Drive calls. Added for the Library's initial metadata load; existing category, album-list, and album-detail routes remain available. |
| GET | `/api/library/albums/{albumId}` | 200 `AlbumDetailDto`, with photos ordered sortOrder, createdAt, id ascending. |
| GET | `/api/library/photos/{photoId}/image` | Streams validated image bytes for an owned photo; foreign/missing photo is 404, rejected/unavailable upstream is 502. Private revalidated ETag; no Drive redirect. |
| PUT | `/api/library/albums/{albumId}` | Same JSON as create; 200 `AlbumDto`; does not rename Drive folder. |
| POST | `/api/library/albums/{albumId}/photos` | Multipart `files`, 1–20 files, plus shared photo metadata below; 201 `PhotoDto[]`. |
| PUT | `/api/library/photos/{photoId}` | JSON photo metadata; 200 `PhotoDto`. Edits DB metadata only, preserving Drive file ID/bytes. |
| DELETE | `/api/library/photos/{photoId}` | Drive trash first, then DB delete; 204. Clears cover when applicable. |
| DELETE | `/api/library/albums/{albumId}` | Drive folder trash first, then DB cascade; 204. |
| GET | `/api/library/categories` | 200 `LibraryCategoryDto[]`; ensures this user's four defaults idempotently; sorted isDefault descending, name then id ascending. |
| POST | `/api/library/categories` | JSON `{ "name": "Du lịch" }`; 201 `LibraryCategoryDto`. Server owns slug, owner and isDefault. |
| PUT | `/api/library/categories/{id}` | JSON `{ "name": "Du lịch gia đình" }`; 200 `LibraryCategoryDto`. Only owned custom categories can be renamed; slug stays unchanged. |
| DELETE | `/api/library/categories/{id}` | 204 for an unused custom category owned by the caller; 404 when missing/not owned; 409 for default/in-use categories. |

Library success payloads are direct DTOs/arrays, not `{ success, data }` wrappers.
The image route calls the existing Apps Script `library_read_photo` action, accepts JPEG/PNG/WebP/GIF up to 5 MiB, and validates returned file identity, MIME, stored size, decoded byte count, MD5 and file signature. It caches bytes for 30 minutes and failures for 20 seconds, and evicts photo bytes after photo or album deletion. Browser gallery and lightbox use this route; only the explicit open-original action uses a Drive view link.
Errors retain `{ "success": false, "message": "..." }`: 400 validation, 401 auth,
404 inaccessible/missing resource, 502 storage failure, 503 persistence/unavailable
folder. Oversized request bodies can be rejected with 413 before the controller.

`AlbumDto`: `id`, `name`, `description`, `photoCount`, `coverPhotoUrl`, `coverPhotoId`, `createdAt`,
`updatedAt`. Detail adds `photos`. `PhotoDto`: `id`, `driveFileId`, `url`, `fileName`, `contentType`,
`fileSize`, `caption`, `sortOrder`, `takenAt`, `createdAt`, `title`, `category`,
`displayDate`, `description`, `author`. IDs are Int64; timestamps are UTC.
`description` aliases the existing `caption` column. TakenAt/cover selection
still have no editing API.

Upload accepts flat multipart fields `title`, `category`, `displayDate`,
`description`, `author` applied to every file in the batch. Photo PUT accepts
the same fields as JSON and replaces all editable metadata (omitted optional
fields clear to null):

```json
{ "title": "Họp mặt 2026", "category": "events", "displayDate": "06/10/2026",
  "description": "Ảnh họp mặt gia đình", "author": "Gia đình" }
```

Category is optional metadata. If a non-empty category is supplied, it must be
a slug (max 64) from the caller's custom category registry; legacy category
endpoints and existing category records remain supported. An upload without a
category receives the internal non-null value `__album__` for the legacy photo
column; no category row is created. An update without category preserves the
photo's current value. If category is explicitly supplied, the server validates
ownership before assigning it; `all` is rejected. Title/author max 200,
displayDate max 100, description max 2000; blank strings normalize to null.
DisplayDate is free-form display text and does not change `takenAt`.
Existing photos retain their category; the UI displays original filename when title is absent.

Name is required, trimmed and max 150 characters; description max 2000, blank
normalizes to null. Photos support JPEG/PNG/WebP/GIF, normalize image/jpg to
image/jpeg, and reject paths/control characters in filenames, extension/MIME
mismatches, empty files, unsupported types and mismatched image signatures.
Each file is max 5 MiB (5 × 1024 × 1024 bytes); max request size is 101 MiB.

Batch strategy is all-or-nothing best effort: no photo rows are inserted until
every upload is verified; storage/DB failure rolls back the transaction and
compensates every known newly uploaded Drive ID. Failed cleanup remains failure
and is logged with IDs. A transport timeout can leave an unknown remote write,
since Drive and PostgreSQL have no distributed transaction; see
`LIBRARY_PHASE1_REPORT.md` for recovery limits.

Drive identity is `album_<DB id>` and a generated `photo_<uuid>.<mime extension>`.
Original filenames are retained in DB for display, never used as Drive identity.
Legacy URLs in DB remain stable `https://lh3.googleusercontent.com/d/{fileId}` for
compatibility, but the browser image route does not read from them. Library upload
continues its existing Drive sharing/write behavior; image bytes are read through
the ownership-checked backend route and Apps Script validates direct album membership.

The complete deployable source remains `GOOGLE_APPS_SCRIPT_AVATAR.gs`, adding
`library_create_album_folder`, `library_upload_photo`, `library_delete_photo`,
`library_delete_album`, and `library_read_photo`. The GET contract reports
`libraryContractVersion: library-read-photo-v2`. The read action accepts
`{ action, folderId, fileId }`, verifies the active album folder and direct file
parent, and returns `{ success, fileId, contentType, fileSize, md5Checksum,
fileBase64 }` without a public image URL. Configure a separate `LIBRARY_FOLDER_ID` Script Property;
leave `AVATAR_FOLDER_ID` unchanged. No folder IDs belong in backend config.

## FamilyMember avatar storage contract

FamilyTree avatar operations require an authenticated `USER` and are scoped to
that user's tree. A foreign member is indistinguishable from a missing member
(404). Managed avatar IDs are never returned in `FamilyMemberDto`; `hasManagedAvatar`
indicates whether the server has a managed file. Managed `avatarUrl` is omitted,
while legacy external avatar URLs remain available.

| Method | Route | Result |
| --- | --- | --- |
| POST | `/api/family-tree/{id}/avatar` | Authenticated multipart upload (`file`, max 5 MiB). |
| DELETE | `/api/family-tree/{id}/avatar` | Deletes the managed avatar and clears its metadata. |
| GET | `/api/family-tree/{id}/avatar/image` | Streams a validated managed image; 404 `MEMBER_AVATAR_NOT_FOUND`, 502 `MEMBER_AVATAR_UPSTREAM_FAILED`. |

The image route serves server-fetched bytes, not a Drive redirect. It accepts
JPEG/PNG/WebP/GIF only, limits the response to 5 MiB, returns a private revalidated
ETag response, and caches successful bytes for 30 minutes. Upstream failures are
negative-cached briefly to bound repeated requests. Upload, avatar deletion and
member deletion evict the prior version. The read path uses the stable file ID and
a stable revision query on the server-side upstream URL:
`https://lh3.googleusercontent.com/d/{fileId}?v={updatedAtUtcTicks}`. The same
revision reuses the backend cache; replacement advances `UpdatedAt` so the next
read uses a new upstream URL and ETag. It does not retry.
The isolated `IGoogleDriveMemberAvatarService` uses the existing Apps Script
upload/delete actions and `GoogleDrive:WebAppUrl`:

```json
{ "action": "member_avatar_upload", "memberId": 15, "fileBase64": "...",
  "contentType": "image/jpeg", "existingFileId": null }
```

```json
{ "action": "member_avatar_delete", "memberId": 15, "fileId": "X" }
```

`MEMBER_AVATAR_FOLDER_ID` is a separate Script Property, distinct from both account
avatar and Library roots. Identity is `member_avatar_<memberId>`. An explicit
existing ID must be available in that folder and is overwritten using Drive v2
`title`/`mimeType`, preserving its ID. No explicit ID permits legacy stable-name
lookup, followed by create only when absent. Upload accepts JPEG/PNG/WebP/GIF,
normalizes JPG to JPEG, max 5 MiB, and validates the image signature.

Success returns `fileId`, `md5Checksum`, `contentType`, numeric `fileSize`, stable
`url`/`directUrl`, and `fileUrl`. Apps Script verifies stored ID/checksum/MIME/size
and sharing before success; the backend client also verifies checksum/MIME/size
and expected identity, generating its own canonical URL. No timestamps are stored
in URLs. Failure never supplies a successful URL; the client retains a known ID
only on a failed first create for possible compensation, not on failed overwrite.

Delete checks the explicit file's parent before trashing it and also removes
stable-name legacy duplicates in the member folder. It permits retries of a
tracked file already trashed; an unavailable/outside explicit ID fails without
legacy fallback. Without ID, deleting an absent legacy avatar succeeds with
`deleted: false`. Apps Script is unchanged by the image-delivery integration.

## Family tree

| Method | Route |
| --- | --- |
| GET | `/api/family-tree` |
| GET | `/api/family-tree/generations` |
| GET | `/api/family-tree/{id}` |
| POST | `/api/family-tree` |
| PUT | `/api/family-tree/{id}` |
| DELETE | `/api/family-tree/{id}` |

Family-tree responses wrap payloads in `{ success, data }`. All routes require role `USER` on an active account (Admin-only: 403; anonymous: 401).

Each USER has one private FamilyTree, lazily created by the first member request. The server resolves its unique owner from the validated session; clients cannot select an owner or FamilyTreeId. Member list/detail/update/delete and all relationship validation are scoped to that tree. A missing or foreign member ID returns 404 `FAMILY_MEMBER_NOT_FOUND`; a foreign father/mother/spouse/child/horizontal reference returns 400 `FAMILY_RELATED_MEMBER_NOT_FOUND`. Names can repeat across trees. Generation records remain a shared read-only reference catalog.

Family member read, create and update payloads include `lifeStatus` (`LIVING` or `DECEASED`) and nullable `deathDate` (`YYYY-MM-DD`). Omitted status defaults to `LIVING`; the server trims and uppercases supported status values. Living members always have `deathDate: null`. Deceased members require a date no later than today's date in Vietnam and no earlier than `dateOfBirth` when one is recorded. Invalid values return `FAMILY_MEMBER_INVALID_LIFE_STATUS`, `FAMILY_MEMBER_DEATH_DATE_REQUIRED`, `FAMILY_MEMBER_DEATH_DATE_FUTURE`, or `FAMILY_MEMBER_DEATH_BEFORE_BIRTH`.

The legacy `/api/admin/family-tree` route family has been removed (404); it cannot be used to bypass the USER requirement.

FamilyTree codes: `FAMILY_MEMBER_NOT_FOUND`, `FAMILY_RELATED_MEMBER_NOT_FOUND`,
`FAMILY_SELF_RELATION`, `FAMILY_PARENTS_MUST_DIFFER`, `FAMILY_SPOUSE_IN_USE`,
`FAMILY_RELATIONSHIP_INVALID`, `FAMILY_RELATIONSHIP_CYCLE`, `FAMILY_VALIDATION_FAILED`.
Library codes: `LIBRARY_NOT_FOUND`, `LIBRARY_INVALID_IMAGE`,
`LIBRARY_IMAGE_SIZE_INVALID`, `LIBRARY_FILE_COUNT_INVALID`, `LIBRARY_METADATA_INVALID`,
`LIBRARY_INVALID_DISPLAY_DATE`,
`LIBRARY_VALIDATION_FAILED`, `LIBRARY_STORAGE_FAILED`, `LIBRARY_SAVE_FAILED`,
`LIBRARY_ALBUM_UNAVAILABLE`, `LIBRARY_CLEANUP_FAILED`; an invalid active-user
session can return `AUTH_SESSION_INVALID` with 401.

`LibraryCategoryDto`: `id`, `name`, `slug`, `isDefault`. Category name is required,
trimmed, max 100 characters. Vietnamese slug normalization removes accents,
converts Đ/đ to d, lowercases and collapses punctuation to hyphens; collisions
use suffixes -2, -3 while staying within 64 characters. Empty ASCII slugs use
`category`; `all` is reserved and becomes `all-category`. A per-user PostgreSQL
row lock serializes category creation/rename/deletion and photo category assignments;
unique `(created_by_user_id, slug)` provides a database constraint too.
Category errors: `LIBRARY_CATEGORY_INVALID` (400), `LIBRARY_CATEGORY_NOT_FOUND`
(404), `LIBRARY_CATEGORY_CANNOT_EDIT`, `LIBRARY_CATEGORY_CANNOT_DELETE` and `LIBRARY_CATEGORY_IN_USE` (409).
PUT accepts only `UpdateLibraryCategoryDto.Name`: it changes Name and UpdatedAt,
never slug, ownership, default flag or photo rows. In-use custom categories can
be renamed (Du lịch → Du lịch gia đình retains slug `du-lich`).
DriveFileId is read-only in PhotoDto; photo write DTOs cannot change storage
identity. The UI tries stored URL then the Drive thumbnail once per candidate,
shows a localized placeholder on failure, and opens the original Drive view
when the ID is available. Existing stored URLs and media are unchanged.
Photo metadata `displayDate` is optional; when supplied, it must be a valid
non-future `DD/MM/YYYY` calendar date (including leap-year validation).

## Administration

Every administrative endpoint requires role `ADMIN`.

| Method | Route | Body |
| --- | --- | --- |
| GET | `/api/admin/stats` | |
| GET | `/api/admin/users?search=text` | Searches email and full name, case-insensitively. |
| PUT | `/api/admin/users/{id}/status` | `{ "isActive": true }` |
| PUT | `/api/admin/users/{id}/role` | `{ "role": "ADMIN" }` or `{ "role": "USER" }` |
| DELETE | `/api/admin/users/{id}` | |
| GET | `/api/admin/logs` | Latest login audit entries. |

Deletion of a user with a nonempty tree returns 409 `USER_HAS_FAMILY_MEMBERS`; it never cascades family members. An empty tree is explicitly removed in the user-delete transaction.

## Web runtime

Auth login/register/Google: 10 requests/minute/IP; refresh: 30/minute/IP; account avatar and Library uploads: 10/minute/validated user. Configurable named fixed-window policies use no queue. Raw forwarded headers are not trusted. Exceeding a limit returns 429 `{ success: false, code: "RATE_LIMITED", message: "..." }` with Retry-After.

`GET /health/live` checks the process only; `GET /health/ready` checks PostgreSQL (200 healthy, 503 unavailable), exposing no connection details. Responses expose a validated/generated X-Request-ID. FamilyTree/Library JSON and health responses support gzip/Brotli when requested; token-issuing auth responses are excluded. No private response output caching is enabled.

## Family anniversaries

All `/api/family-tree/anniversaries` routes require role `USER` and are scoped to the caller's family tree. `GET` lists records with `nextSolarDate` and `daysUntil`; legacy rows with null `originalSolarDate` and `lunarYear` remain readable and use the recurring lunar day/month/leap flag.

Death anniversary writes require `originalSolarDate` (`YYYY-MM-DD`) and `lunarYear` (1800–2200), alongside `lunarDay`, `lunarMonth`, and `isLeapMonth`. The backend converts that full lunar date and requires it to equal the solar date. A mismatch returns `FAMILY_ANNIVERSARY_DATE_MISMATCH`; an invalid date or unavailable leap month returns `FAMILY_ANNIVERSARY_INVALID_LUNAR_DATE`. The original year is historical only; recurring dates use day/month/leap status.

When a death anniversary links `relatedMemberId`, that member must belong to the caller's tree, have `lifeStatus: DECEASED`, and have a `deathDate` matching `originalSolarDate`. Failures return `FAMILY_ANNIVERSARY_MEMBER_NOT_DECEASED`, `FAMILY_ANNIVERSARY_MEMBER_DEATH_DATE_REQUIRED`, or `FAMILY_ANNIVERSARY_DEATH_DATE_MISMATCH`. `FAMILY_EVENT` may link any member in the caller's tree; a null member remains valid for either event type.

`GET /api/family-tree/anniversaries/next-occurrence?day={1..30}&month={1..12}&isLeapMonth={true|false}` is the authoritative preview. It returns the nearest converted solar date on or after today's date in Vietnam (`nextLunarYear`, `nextSolarDate`, `daysUntil`, `solarYear`) and `isCurrentYearPassed`. Unsupported lunar dates and missing leap months are skipped while searching for the next valid occurrence. `GET /api/family-tree/anniversaries/lunar-to-solar?day=&month=&year=&isLeapMonth=` remains available for validating historical date pairs. The calendar algorithm supports lunar years 1800–2200.

Production defaults Database:AutoMigrate to false and fails startup when migrations are pending. Apply reviewed migrations separately before startup; seed runs only with explicitly enabled migration initialization. Development defaults remain convenient. See MYLIFE_WEB_OPTIMIZATION_REPORT.md for the unapplied AddPerUserFamilyTrees migration and review steps.
