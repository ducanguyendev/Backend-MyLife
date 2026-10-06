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
