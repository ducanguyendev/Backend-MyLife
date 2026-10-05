# Backend integration tests

These tests intentionally use a real, isolated PostgreSQL database because refresh-token concurrency, migrations, constraints, and transaction isolation cannot be validated reliably with an in-memory provider.

Set `MYLIFE_TEST_DATABASE` to a disposable database owned by the test runner, then run:

```powershell
$env:MYLIFE_TEST_DATABASE = 'Host=127.0.0.1;Port=55439;Database=mylife_validation;Username=mylife_test'
dotnet test
```

Never point this variable at a production or shared development database. The suite creates application data in the configured database and creates/drops only uniquely prefixed `legacy_*` schemas for migration checks.
