# Family Map Real Data and Leaflet Report

## Result

The FamilyTree map now uses authenticated backend data, persistent address geocoding cache fields, a rate-limited background geocoding queue, and React Leaflet with OpenStreetMap tiles. No demo region counts or fallback marker data remain in `FamilyMapTab`.

## Files added

- `Features/FamilyTree/DTOs/FamilyMapDto.cs`
- `Features/FamilyTree/Services/IGeocodingService.cs`
- `Features/FamilyTree/Services/NominatimGeocodingService.cs`
- `Features/FamilyTree/Services/FamilyLocationGeocodingQueue.cs`
- `Migrations/20261012090714_AddFamilyMemberGeolocation.cs` and its designer
- `FAMILY_MAP_REAL_DATA_LEAFLET_REPORT.md`
- Frontend `src/features/admin/services/familyMapService.ts`
- Frontend `src/features/admin/components/family-tree/FamilyMapTab.css`
- Frontend `tests/family-map.test.cjs`

## Files modified for this task

- Backend `Features/FamilyTree/Controllers/FamilyTreeController.cs`
- Backend `Features/FamilyTree/Services/FamilyTreeService.cs`
- Backend `Shared/Entities/FamilyMember.cs`
- Backend `Migrations/AppDbContextModelSnapshot.cs`
- Backend `Program.cs`
- Backend `appsettings.example.json`
- Backend `Tests/FamilyIsolationTests.cs` and `Tests/SystemFlowsTests.cs`
- Frontend `src/features/admin/components/FamilyTreeManager.tsx`
- Frontend `src/features/admin/components/family-tree/FamilyMapTab.tsx`
- Frontend `src/shared/locales/vi/admin.json` and `src/shared/locales/en/admin.json`
- Frontend `package.json` and `package-lock.json`

There were already uncommitted Anniversary changes in both repositories when this task began. Those existing Anniversary files were preserved. The model snapshot also already contained the Anniversary model from that work; the new geolocation migration itself only adds the location columns listed below.

## Implementation details

1. **Removed hardcoded data:** Removed demo regions, fake member totals, percentages, branch contacts, and fixed origin map details from `FamilyMapTab.tsx`, and removed their unused fake regional labels from both locale files.
2. **Database columns:** nullable `latitude` and `longitude` (`double precision`), `geocoded_address` (`varchar(500)`), `location_display_name` (`varchar(500)`), and `geocoded_at` (`timestamp with time zone`).
3. **Migration:** `20261012090714_AddFamilyMemberGeolocation` (`AddFamilyMemberGeolocation`). It contains only five `family_members` column additions and matching drops in `Down`.
4. **Geocoding architecture:** `IGeocodingService` abstracts lookups; `NominatimGeocodingService` handles the configured Nominatim endpoint; `FamilyLocationGeocodingQueue` performs background work. The map API responds from its current DB snapshot without waiting on HTTP calls.
5. **Cache logic:** A successful result is reused when trimmed `address` matches `geocoded_address` and valid cached coordinates/display name exist. A no-result/upstream-failure response is also cached for 30 days to avoid repeated requests. Unchanged successful addresses are not re-geocoded.
6. **Address invalidation:** `UpdateMemberAsync` clears all five cache fields when the trimmed address changes. The queue checks the address again after geocoding before saving, preventing stale results from overwriting an address update.
7. **Nominatim handling:** Provider, URL, user agent, enable flag, timeout, failure cache period, throttle, and per-request queue limit are configurable. `429`, other unsuccessful statuses, timeout/network errors, invalid JSON, empty results, and invalid coordinates become a cached unresolved location; they do not fail the member or map request.
8. **Rate/throttle:** A single background channel reader processes one lookup at a time. At most five unresolved addresses are enqueued per map request, with at least a 15-second interval (4 requests per minute) between upstream requests, including if the service runs regularly for long periods. No request waits for the geocoder.
9. **API endpoint:** `GET /api/family-tree/map`, returning total/located/unlocated/pending counts and aggregated locations with only member ID, name, and generation in each popup list.
10. **Ownership/security:** The endpoint shares the controller's `[Authorize(Roles = USER)]` policy and resolves the tree by authenticated user ID before querying members. No global member query or public map endpoint was introduced. Anonymous requests remain 401, ADMIN remains 403, and each USER receives only their own tree.
11. **Map aggregation/privacy:** Members group by the geocoder-provided locality/province/country display name. Coordinates are rounded to one decimal place (about city scale) before map responses, then aggregated within the group. Street and house fields are never included in the map DTO or popup.
12. **Leaflet implementation:** React Leaflet `MapContainer`, `CircleMarker`, `Tooltip`, and `Popup`; bounded marker radius grows with count. Viewport centers on Vietnam when empty, uses a reasonable zoom for one location, and uses `fitBounds` for multiple locations.
13. **OpenStreetMap attribution:** Leaflet tile attribution remains visible and links to OpenStreetMap contributors.
14. **KPIs:** All four values come from API totals or `locations.length`. Region percentage is calculated as `memberCount / locatedMembers`.
15. **Loading/error/empty:** Loading indicator, retryable API error, empty-tree state, and member-no-location state are provided. No fake-data fallback exists.
16. **Responsive/theme:** KPI grid uses two columns on narrow screens and four on wide screens; map height adapts at the small breakpoint. UI uses shared semantic theme classes; Leaflet popup background/text use theme variables. OSM tiles stay standard.
17. **i18n:** Added Vietnamese and English keys for the title, description, KPIs, loading/error/retry, empty/no-location, regional distribution, counts, generations, extra members, and pending geocoding.
18. **Backend tests added:** Role and tree ownership isolation, cached data reuse, address-change invalidation, no-address exclusion, and asynchronous geocoding/cache success and failure scenarios. Tests use a deterministic fake geocoder.
19. **Frontend tests added:** API endpoint use, Leaflet/OSM and attribution presence, no hardcoded demo counts, loading/error/empty states, and required VI/EN keys.

## Validation

- Backend `dotnet build -c Release --no-restore`: **passed** (one NU1900 vulnerability-feed warning due unavailable NuGet network access).
- Frontend `npm run build`: **passed**.
- Frontend `npm run lint`: **passed**.
- Frontend `npm run test:family-map`: **passed, 3 tests**.
- `npm install` reported one high-severity advisory in the dependency tree; it was not audited or auto-fixed as part of this map change.
- Backend `dotnet test Tests/Backend-MyLife.Tests.csproj -c Release --no-restore`: **compiled successfully, but test host produced no results and remained running for about 90 seconds; stopped**. A filtered run of existing non-database lunar-calendar unit tests exhibited the same test-host hang. Integration assertions therefore need rerunning in an environment where the .NET test host completes.
- `git diff --check`: **passed** in both repositories (Git reported only line-ending conversion notices).
- `dotnet ef migrations has-pending-model-changes --no-build --configuration Release`: **passed**; no model changes remain after the generated migration.
- Database update: **not run**. Migration is generated and unapplied.
- Branch: `feature_minhduc` in both repositories.
- Commit: **not created**.
- Push: **not performed**.
- Mobile changes: **none**.
- Library changes: **none**.
- Anniversary business logic changes for this map task: **none**; pre-existing Anniversary work was preserved.

### Repository status recorded

Both repositories remain on `feature_minhduc` with a dirty working tree and no new commit. Frontend status includes the new map service/component/style/test and Leaflet dependency updates, alongside the pre-existing Anniversary and shared UI changes. Backend status includes the geocoding service/queue, map DTO/controller route, migration/snapshot, map tests and configuration, alongside the pre-existing Anniversary files and model changes. The Mobile repository was not edited.

## Database change required

**YES** — apply `AddFamilyMemberGeolocation` when ready. From the backend folder, set the connection string locally and run:

```powershell
cd D:\DUAN\MYLIFE\Backend-MyLife
$env:MYLIFE_DESIGN_DATABASE = "<user's connection string>"
dotnet ef database update
```

This command was not run as part of this task.
