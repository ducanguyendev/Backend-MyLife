# W10 Backend stabilization

Date: 2026-11-10, Asia/Saigon. Starting branch `feature_minhduc`, HEAD `19e192a774554741acb47529063773f8c6be87fd`.

The worktree already contained changes to AccountController, FamilyRelationshipService, Program, FamilyIsolationTests, SystemFlowsTests and untracked AuthTransportPolicy/AuthTransportPolicyTests. W10 preserves that work. Its incremental changes are listed below; no migration or Mobile source was edited.

## Confirmed defects and fixes

1. A copied access token remained valid after logout. TokenService now adds the hash identifying its existing refresh record as `sid`; the existing database-backed validation query also checks that session is active. Logout/rotation/lock/role mutation invalidate the corresponding access session. No new table, migration or authentication subsystem is introduced. `W10SessionRegressionTests` checks logout, independent sessions, rotation and replay. Existing role tests now reauthenticate after intentional session revocation and still check current USER/ADMIN permissions. A pre-W10 access token can use the normal refresh flow to obtain the updated token when its refresh cookie remains valid.
2. JSON registration validation sometimes omitted documented machine codes because `ModelState.AttemptedValue` is not populated by JSON input formatters. The response factory reads the bound RegisterViewModel and recognizes the JSON date path. Existing real HTTP tests verify mismatch, excessive password bytes, Gmail restriction, duplicate account and invalid birth date. Passwords/tokens are not returned by this validation path.
3. Year-only timeline events sorted after every event with a full date. Ordering now compares calendar years and a consistent day-number within each year. The existing PostgreSQL HTTP test verifies birth/year-only address change/death chronology, CRUD and ownership.
4. Explicit legacy baselining compared a historical schema against today's model, incorrectly demanding later tables/columns. It now validates the frozen target model of `AddFamilyRelationships`. Opt-in, column types/nullability, unique keys, foreign keys and transaction guards remain enabled. Old migrations were not edited.
5. The Library migration test attempted to insert today's User model into an older schema that lacks `display_name`. Its fixture now inserts using that historical schema before applying subsequent migrations; preservation assertions remain intact. The Google-name test now uses a unique synthetic provider subject so reruns cannot reuse a previously renamed account. These are test-fixture repairs, not live Google validation.

## W10 files

- `Program.cs`
- `Features/Auth/Controllers/AccountController.cs`
- `Features/Auth/Services/TokenService.cs`
- `Features/FamilyTree/Services/FamilyTimelineService.cs`
- `Shared/Data/DatabaseStartup.cs`
- `Tests/SystemFlowsTests.cs`
- `Tests/LibraryCategoryMigrationTests.cs`
- `Tests/W10SessionRegressionTests.cs`
- `API_CONTRACT.md`
- This document.

## Verification and limits

Fresh initial run: 181 tests, 176 pass, 5 fail. After stabilization: 182 tests pass, zero fail/skip, against disposable PostgreSQL 18. Backend and test-project Release build succeeds with zero warnings/errors. The 45 Apps Script Node tests also pass.

The ASP.NET test host uses the real application, migrations, EF Core, PostgreSQL, authorization and rate limiter, with external Google/Drive/geocoding services substituted. These results do not verify live provider credentials or storage. A separate Kestrel/real-browser runner uses a new local PostgreSQL cluster and no intercepted API responses. Its final results and lifecycle are recorded under `../Frontend-MyLife/docs/review-w10/test-results/`.

From Frontend, run `npm run test:integration` with PostgreSQL binaries on PATH or `PG_BIN` set. The runner creates a unique database in its own loopback cluster, applies migrations only there, runs Backend tests, starts Kestrel and Vite, runs real HTTP/browser checks, then drops the database and stops the cluster in `finally`. No normal application connection string is used. Test accounts and keys are synthetic; credentials/cookies/JWTs are not printed.

No deployment, commit, push, merge or PR was performed. Public release still requires the untested live provider and production HTTPS/cookie configuration checks identified in the Frontend release-readiness report.
