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

| Method | Route |
| --- | --- |
| GET | `/api/avatar/{email}` |
| POST | `/api/avatar/upload` |
| DELETE | `/api/avatar` |

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
