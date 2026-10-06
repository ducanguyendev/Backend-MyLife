# Backend integration tests

Library Phase 1 regression tests use the same isolated PostgreSQL fixture and
mock only the remote Drive storage boundary. They cover ownership, multipart
validation, successful and failing batches, compensating cleanup, DB save
failure, cover deletion, cascade deletion and upload/delete concurrency.
The migration snapshot is checked against the runtime model.
Library UI integration adds batch photo metadata and owner-only metadata PUT
roundtrip coverage, rejecting the frontend-only `all` category and confirming
that editing metadata preserves Drive identity/bytes. It also checks the small
ExtendLibraryPhotoMetadata migration alongside AddLibraryModule.

```powershell
dotnet test Tests/Backend-MyLife.Tests.csproj -c Release
node --test Tests/apps-script-avatar.test.cjs Tests/apps-script-library.test.cjs
```

The Library Node harness executes the complete deployable .gs source with
mocked Drive folders/files and signed bytes. It verifies idempotent album
folder creation, root separation, media checksums/MIME/size, cleanup failures,
safe filenames and delete boundaries. It does not create real Library folders
or deploy Apps Script.

These tests intentionally use a real, isolated PostgreSQL database because refresh-token concurrency, migrations, constraints, and transaction isolation cannot be validated reliably with an in-memory provider.

Set `MYLIFE_TEST_DATABASE` to a disposable database owned by the test runner, then run:

```powershell
$env:MYLIFE_TEST_DATABASE = 'Host=127.0.0.1;Port=55439;Database=mylife_validation;Username=mylife_test'
dotnet test
```

Never point this variable at a production or shared development database. The suite creates application data in the configured database and creates/drops only uniquely prefixed `legacy_*` schemas for migration checks.

Avatar regressions also execute the actual Apps Script source with a mocked
Drive service, validating persisted bytes, MIME changes, identity, failure,
checksum verification, and duplicate cleanup:

```powershell
node --test Tests/apps-script-avatar.test.cjs
dotnet test Tests/Backend-MyLife.Tests.csproj -c Release
```

The frontend avatar tests run with `npm.cmd run test:avatar` from
`Frontend-MyLife`. These tests do not call the deployed Apps Script or real Drive.

Member-avatar storage and all three Apps Script groups:

```powershell
node --test Tests/apps-script-avatar.test.cjs Tests/apps-script-member-avatar.test.cjs Tests/apps-script-library.test.cjs
dotnet test Tests/Backend-MyLife.Tests.csproj -c Release --filter 'FullyQualifiedName~GoogleDrive'
```

The member harness uses one mocked Drive with distinct account/member/Library
roots and inspects persisted bytes, metadata and sharing. It covers first upload,
100 overwrites with one ID, primary-ID failure boundaries, legacy cleanup,
signature validation, corrupt metadata/no-op updates, cleanup/sharing failures,
delete retry and combined dispatcher/version diagnostics. The C# filter runs
storage-client contract tests without PostgreSQL or a real Web App; it is not
the full HTTP/DB integration suite. Neither command deploys or writes real Drive.

Optional live regression (writes one uniquely named test avatar and trashes it
afterward, without touching account avatars):

```powershell
node Tests/verify-live-avatar.cjs
```

This reads `GoogleDrive:WebAppUrl` from appsettings.json, or the
`MYLIFE_AVATAR_WEBAPP_URL` environment override. It uploads five valid fixtures
from `Tests/fixtures/avatar-replacements.json` (JPEG → WebP → PNG → WebP → JPEG),
checks every response's ID/MIME/checksum, downloads original Drive media after
each overwrite and verifies its checksum. No URL, base64 or secrets are logged.
