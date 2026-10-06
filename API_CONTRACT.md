# MyLife API contract

All authenticated endpoints accept the `AccessToken` HttpOnly cookie or a Bearer access token. Errors use `{ "success": false, "message": "..." }`; validation responses may additionally contain `errors`.

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

`user.id` is an integer and `role` is always `ADMIN` or `USER`. `authProvider` is retained only as legacy account-origin data; `loginProviders.local` and `loginProviders.google` describe usable methods.

## Avatar

| Method | Route | Notes |
| --- | --- | --- |
| GET | `/api/avatar/{email}` | Redirects to the current Drive-backed avatar. |
| POST | `/api/avatar/upload` | Authenticated multipart upload (`file`, max 5 MB); replaces the user's existing Drive file and returns a cache-busted `avatarUrl`. |
| DELETE | `/api/avatar` | Deletes the Drive file first, then clears avatar metadata. |

`users.avatar_drive_file_id` is the stable storage identity. `avatar_source` is `MANUAL`, `GOOGLE`, or null. A manual avatar is never overwritten by Google login. Google profile images are copied to Drive only when missing or changed; storage failure does not invalidate an otherwise successful Google authentication.

The deployable Apps Script implementation and its backend payload contract are in `GOOGLE_APPS_SCRIPT_AVATAR.gs`.

## Library / albums — Phase 1

Every route below requires an active authenticated user. Albums belong to their
`created_by_user_id`; this ID comes from the session, never a client field. Admins
also access only their own albums in this phase. An inaccessible album/photo
returns 404, and list includes only the caller's albums.

| Method | Route | Body / result |
| --- | --- | --- |
| POST | `/api/library/albums` | JSON `{ "name": "Gia đình 2026", "description": "..." }`; 201 `AlbumDto`, Location points to detail. |
| GET | `/api/library/albums` | 200 `AlbumDto[]`, updatedAt descending, id descending tie-break. |
| GET | `/api/library/albums/{albumId}` | 200 `AlbumDetailDto`, with photos ordered sortOrder, createdAt, id ascending. |
| PUT | `/api/library/albums/{albumId}` | Same JSON as create; 200 `AlbumDto`; does not rename Drive folder. |
| POST | `/api/library/albums/{albumId}/photos` | Multipart `files`, 1–20 files, plus shared photo metadata below; 201 `PhotoDto[]`. |
| PUT | `/api/library/photos/{photoId}` | JSON photo metadata; 200 `PhotoDto`. Edits DB metadata only, preserving Drive file ID/bytes. |
| DELETE | `/api/library/photos/{photoId}` | Drive trash first, then DB delete; 204. Clears cover when applicable. |
| DELETE | `/api/library/albums/{albumId}` | Drive folder trash first, then DB cascade; 204. |

Library success payloads are direct DTOs/arrays, not `{ success, data }` wrappers.
Errors retain `{ "success": false, "message": "..." }`: 400 validation, 401 auth,
404 inaccessible/missing resource, 502 storage failure, 503 persistence/unavailable
folder. Oversized request bodies can be rejected with 413 before the controller.

`AlbumDto`: `id`, `name`, `description`, `photoCount`, `coverPhotoUrl`, `createdAt`,
`updatedAt`. Detail adds `photos`. `PhotoDto`: `id`, `url`, `fileName`, `contentType`,
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

Category is `photos`, `decrees`, `events`, or `temple` (default `photos`);
`all` is a frontend-only filter and is rejected. Title/author max 200,
displayDate max 100, description max 2000; blank strings normalize to null.
DisplayDate is free-form display text and does not change `takenAt`.
Existing photos receive category `photos` from the metadata migration;
the UI displays original filename when title is absent.

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
URLs in DB are stable `https://lh3.googleusercontent.com/d/{fileId}` without cache
query strings. Direct images use anyone-with-link sharing like account avatars;
metadata APIs enforce ownership, while a known direct image link can be viewed.

The complete deployable source remains `GOOGLE_APPS_SCRIPT_AVATAR.gs`, adding
`library_create_album_folder`, `library_upload_photo`, `library_delete_photo`,
`library_delete_album`. Configure a separate `LIBRARY_FOLDER_ID` Script Property;
leave `AVATAR_FOLDER_ID` unchanged. No folder IDs belong in backend config.

## FamilyMember avatar storage contract

The current FamilyTree HTTP CRUD accepts `avatarUrl`; it has no Phase 2 member
avatar upload/delete HTTP endpoint or persisted Drive file ID yet. The isolated
`IGoogleDriveMemberAvatarService` client now defines the matching Apps Script
storage contract for that integration, reusing `GoogleDrive:WebAppUrl`:

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
`deleted: false`. A future HTTP integration must authorize the member and persist
the stable file ID; this storage client does not change current FamilyTree CRUD.

## Family tree

| Method | Route |
| --- | --- |
| GET | `/api/family-tree` |
| GET | `/api/family-tree/generations` |
| GET | `/api/family-tree/{id}` |
| POST | `/api/family-tree` |
| PUT | `/api/family-tree/{id}` |
| DELETE | `/api/family-tree/{id}` |

Family-tree responses wrap payloads in `{ success, data }`. All routes require authentication.

The same suffixes are available under `/api/admin/family-tree`; every admin alias requires role `ADMIN`.

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
