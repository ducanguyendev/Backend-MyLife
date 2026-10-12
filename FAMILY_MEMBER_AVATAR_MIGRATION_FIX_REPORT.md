# FamilyMember Avatar Migration Fix Report

## Result

The EF Core model and migration snapshot are now consistent. The new migration is present in source and remains unapplied to the configured database. No database update, commit, or push was performed.

## Checklist

1. **Initial EF version:** EF Core and `dotnet-ef` 10.0.11.
2. **Initial pending-model result:** EF reported pending model changes.
3. **Root cause:** The explicit `User.AvatarDriveFileId` column mapping was missing from the `User` Fluent configuration, so EF conventions could infer the CLR property name/type rather than the established snake-case `varchar(255)` database column. The previous FamilyMember migration/snapshot also needed to be regenerated so the member property was represented in the snapshot.
4. **Was the User mapping missing/incorrect?** Yes. Restoring it removed the unintended `users` rename/type drift.
5. **User mapping after fix:** `entity.Property(e => e.AvatarDriveFileId).HasColumnName("avatar_drive_file_id").HasMaxLength(255);` in the User entity configuration. The property remains nullable.
6. **Existing account avatar migration preserved?** Yes. `20261005045704_AddAvatarDriveTracking` was not changed.
7. **Account avatar column contract preserved?** Yes: `users.avatar_drive_file_id`, nullable `varchar(255)` (`character varying(255)` in PostgreSQL).
8. **FamilyMember mapping after fix:** `entity.Property(e => e.AvatarDriveFileId).HasColumnName("avatar_drive_file_id").HasMaxLength(255);` in the FamilyMember entity configuration; nullable. The entity data annotations remain consistent.
9. **Old pending member migration removed?** Yes. It was verified as unapplied before removal, then re-scaffolded through EF.
10. **New migration filename:** `Migrations/20261012020531_AddFamilyMemberAvatarDriveTracking.cs` (with its generated Designer file).
11. **New migration Up():** Adds nullable `family_members.avatar_drive_file_id` as `character varying(255)`, max length 255. Down drops that column.
12. **Does the new migration touch `users`?** No. It contains no user table operation.
13. **Snapshot User mapping:** The `MyLife.Shared.Entities.User` block contains `AvatarDriveFileId`, max length 255, type `character varying(255)`, column name `avatar_drive_file_id`.
14. **Snapshot FamilyMember mapping:** The `MyLife.Shared.Entities.FamilyMember` block contains `AvatarDriveFileId`, max length 255, type `character varying(255)`, column name `avatar_drive_file_id`.
15. **`has-pending-model-changes`:** Passed. Output: “No changes have been made to the model since the last migration.”
16. **Migration list:** Prior migrations through `20261007080251_AddPerUserFamilyTrees` were applied; `20261012020531_AddFamilyMemberAvatarDriveTracking` is listed as unapplied/pending against the configured database.
17. **Is the FamilyMember avatar migration pending database apply?** Yes.
18. **Was database update executed?** No.
19. **FamilyTree avatar POST route found?** Yes.
20. **Exact POST route:** `POST /api/family-tree/{id}/avatar` (controller route plus `[HttpPost("{id:int}/avatar")]`).
21. **Exact DELETE route:** `DELETE /api/family-tree/{id}/avatar` (controller route plus `[HttpDelete("{id:int}/avatar")]`).
22. **Frontend route compatibility:** Yes. The routes match the Frontend contract `/api/family-tree/${id}/avatar` for POST and DELETE.
23. **Release build:** Passed with 0 errors. Restore/build emitted NU1900 because the environment could not reach NuGet's vulnerability-data service; packages were already restored.
24. **Tests:** 61 passed, 34 failed. The failures were caused by the unavailable PostgreSQL test database at `127.0.0.1:55439` refusing connections, not by a reported assertion failure. Tests need rerunning when that test DB is available.
25. **`git diff --check`:** Passed; only Git line-ending conversion notices were printed.
26. **Apps Script modified by this task?** No.
27. **Frontend modified by this task?** No.
28. **Mobile modified by this task?** No.
29. **Commit/push?** No.
30. **Manual command after review:** From `Backend-MyLife`, after confirming the selected connection string targets the intended database, run `dotnet ef database update --context AppDbContext`.

## Review notes

- No schema was applied to `mylife_db` during this work.
- The test result is incomplete because the separate test PostgreSQL instance was unavailable.
- Existing unrelated local backend edits were preserved; no reset or restore was run.
