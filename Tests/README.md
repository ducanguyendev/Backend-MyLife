# Backend integration tests

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
