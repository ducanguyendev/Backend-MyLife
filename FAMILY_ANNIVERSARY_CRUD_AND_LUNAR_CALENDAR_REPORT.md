# FamilyTree Anniversary CRUD and Lunar Calendar

## Delivered

- Replaced the sample anniversary cards in the FamilyTree tab with data from the authenticated API. The tab supports search, create, edit, delete confirmation, loading, retryable load errors, empty results, countdowns, and VI/EN labels.
- Added an optional link to a member in the current user's FamilyTree. The form previews the next solar date from the Vietnamese lunar date and handles leap months that do not occur every year.
- Added the backend anniversary entity, owner-scoped service, `USER`-role controller, lunar conversion endpoint, and EF Core migration.
- Added calendar unit tests, API ownership/role/CRUD integration coverage, and frontend service/UI/i18n checks.

## Database model

Migration: `20261012081253_AddFamilyAnniversaries`.

It creates `family_anniversaries` with a required FamilyTree FK (`CASCADE`), an optional FamilyMember FK (`SET NULL`), lunar day/month and leap-month flag, event type, title, optional place/host/notes, and timestamps. Indexes cover `family_tree_id`, `(family_tree_id, lunar_month, lunar_day)`, and `related_member_id`.

The migration and model snapshot were generated. **The migration has not been applied to any database.** Apply it when ready from the backend repository:

```powershell
cd D:\DUAN\MYLIFE\Backend-MyLife
dotnet ef database update
```

## Access and behavior

All API routes require the `USER` role. Each request resolves the signed-in user's private FamilyTree and scopes anniversary reads and mutations to that tree. Related-member IDs are checked against that same tree; a foreign or missing member returns `404`. Foreign anniversary IDs also return `404`. Deleting a linked member clears the optional reference through the database FK.

The lunar converter uses the Vietnamese UTC+7 new-moon and solar-longitude algorithm. The API returns ISO solar dates, and list records include the next occurrence and its day countdown using the current date in Vietnam.

## Verification

- Frontend production build: passed.
- `npm run test:anniversary`: 3 passed.
- `npm run test:i18n`: 12 passed.
- Backend Release build: passed with 0 warnings and 0 errors.
- Backend test project Release build: passed with 0 warnings and 0 errors.
- Calendar/API xUnit execution could not complete: VSTest reported that its testhost did not connect after 90 seconds. The environment reported the testhost process was still running. The integration and calendar tests are present but are not reported as passed.
- `git diff --check`: passed in both repositories.

No database update, commit, or push was performed. Mobile, Library, Apps Script, avatar flows, authentication implementation, and theme visuals were not changed.
