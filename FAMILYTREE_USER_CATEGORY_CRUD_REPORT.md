# FAMILYTREE USER / CATEGORY CRUD REPORT

Ngày: 07/10/2026 (Asia/Saigon). Backend và Frontend đều ở branch `feature_minhduc`.
Đã audit source trước khi sửa; không tạo lại module, không duplicate components/services.
Thay đổi được giữ local để review.

## 1. FamilyTree trước sửa

`FamilyTreeController` dùng `[Authorize]`: USER và ADMIN-only đều được gọi.
`AdminFamilyTreeController` nằm cùng file controller chính, expose
`/api/admin/family-tree` với role ADMIN.
Frontend mount `FamilyTreeManager` duy nhất tại
`src/features/family-tree/pages/FamilyTreePage.tsx`; bốn route alias chỉ yêu cầu login.
Desktop UserMenu và mobile web Header hiện link FamilyTree cho mọi account đăng nhập.
AdminDashboard không mount manager, không có FamilyTree sidebar hoặc Library entry riêng.

## 2. Backend FamilyTree sau sửa

Controller chính dùng `[Authorize(Roles = AppRoles.User)]`.
Áp dụng cho list, generations, member detail, POST, PUT, DELETE.
USER được dùng; ADMIN-only 403; anonymous 401.
Không thay đổi FamilyTree service, relationships, validation hay avatar storage.

## 3. AdminFamilyTreeController

Đã remove class và route exposure khỏi file `FamilyTreeController.cs`.
Search toàn workspace không tìm thấy client đang gọi alias này; chỉ có controller và test cũ.
Giữ shared base controller/service để reuse logic.
Các suffix legacy GET/list/detail/generations/POST/PUT/DELETE trả 404, không còn đường CRUD cho Admin.

## 4. Library authorization

Toàn bộ `LibraryController` dùng role USER, bao gồm albums, photos và categories.
ADMIN-only 403; anonymous 401; account có cả ADMIN và USER được phép vì có USER thật.
Ownership hiện tại của album/photo/category giữ nguyên, lấy user ID từ active authenticated session.
`Program.cs` đã nạp lại toàn bộ role từ database trong OnTokenValidated; không cần đổi policy/JWT/cookie.
`AppRoles.Admin = ADMIN`, `AppRoles.User = USER`; không sửa seed hoặc role database ứng dụng.

## 5. Remove FamilyTree khỏi Admin frontend

`UserMenu.tsx` chỉ render FamilyTree khi `isUser`.
`Header.tsx` áp dụng cùng điều kiện cho mobile web navigation.
Không để menu disabled hoặc coming soon.
AdminDashboard vốn không có FamilyTree mount/link, nên không cần sửa dashboard.
Cả bốn route aliases được bọc `ProtectedRoute requiredRole="USER"`.
ADMIN-only redirect `/Home/Admin` trước khi mount FamilyTree; account thiếu role phù hợp khác về /403.
Anonymous vẫn theo login flow hiện tại.

## 6. User navigation / role representation

Reuse trang User FamilyTree và navigation sẵn có, không tạo dashboard mới.
Routes: /FamilyTree, /family-tree, /Home/FamilyTree, /home/family-tree.
Members, Anniversaries, Library, Map và Mindmap vẫn dùng các components hiện tại.
Auth response thêm trường `roles` chứa actual roles, giữ trường primary `role` cho client cũ.
AuthContext dùng roles để tính isAdmin/isUser; response cũ thiếu roles fallback đúng role đã trả về.
Không suy luận ADMIN có USER. Explicit roles=[] cũng không cấp USER.

## 7. Components / technical debt

Không move components khỏi `src/features/admin/components`.
FamilyTreePage đã reuse manager tại vị trí này từ trước.
Move toàn bộ manager, family-tree components, services và test imports không cần thiết cho yêu cầu,
tăng phạm vi regression. Technical debt: tên folder admin còn chứa components phục vụ User.
Không duplicate, redesign hoặc viết lại UI.

## 8. Category PUT endpoint

`PUT /api/library/categories/{categoryId:long}`
Body: `{ "name": "Du lịch gia đình" }`.
Success 200: `{ "id": 5, "name": "Du lịch gia đình", "slug": "du-lich", "isDefault": false }`.
Response là DTO trực tiếp, giữ convention Library hiện tại.

## 9. UpdateLibraryCategoryDto / service

Thêm `Features/Library/DTOs/UpdateLibraryCategoryDto.cs`, chỉ có `Name`.
Thêm `ILibraryService.UpdateCategoryAsync(int userId, long categoryId, UpdateLibraryCategoryDto dto, CancellationToken ct)`.
Không có writable id/slug/createdByUserId/isDefault.
Test gửi thêm các field này chứng minh chúng không thay đổi dữ liệu server.

## 10. Validation

Name được Trim, required, tối đa 100 ký tự sau trim.
Null, empty, whitespace-only và 101 ký tự: 400 `LIBRARY_CATEGORY_INVALID`.
Chỉ lưu tên đã trim.
Modal create/edit dùng noValidate, custom validation và không có dấu *.
Blank submit không call API, không đóng modal; lỗi VI/EN:
“Vui lòng nhập tên danh mục.” / “Please enter a category name.”

## 11. Ownership / locking

Query edit ràng buộc Id và CreatedByUserId cùng authenticated user.
Foreign/missing category: 404 `LIBRARY_CATEGORY_NOT_FOUND`, không leak bằng 403.
Reuse transaction và per-user PostgreSQL row lock `LockCategoryRegistryAsync`.
Không thay cơ chế ownership hoặc locking cho photo assignments.

## 12. Default categories

photos/decrees/events/temple có IsDefault=true.
Edit: 409 `LIBRARY_CATEGORY_CANNOT_EDIT`.
Delete giữ nguyên: 409 `LIBRARY_CATEGORY_CANNOT_DELETE`.
Frontend không có Pencil/Trash khi default đang selected.
Default labels vẫn qua locale; custom names hiển thị nguyên tên người dùng.

## 13. Custom category CRUD

Create: trim, validate, owner server-assigned, slug normalization/collision suffix hiện tại, 201.
Read: list categories riêng của User, giữ defaults idempotent và sorting.
Edit: custom category owned, kể cả đang có ảnh sử dụng; chỉ đổi Name/UpdatedAt, 200.
Delete unused: 204. In-use: 409 `LIBRARY_CATEGORY_IN_USE`.
Không tự move ảnh; delete selected custom thành công đưa filter về all.

## 14. Rename giữ slug

LibraryPhoto.Category lưu slug string.
Update không gọi slug generator, không sửa photo rows, không gọi Drive.
Slug giữ nguyên identity để ảnh cũ tiếp tục lọc đúng, tránh bulk rewrite/concurrency risk.
UI cập nhật category DTO tại chỗ, không reload page hoặc gallery khi rename.
Selected slug không reset.

## 15. Ví dụ

Trước: name=Du lịch, slug=du-lich.
PUT name=Du lịch gia đình.
Sau: name=Du lịch gia đình, slug=du-lich, IsDefault=false.
Chip label đổi; selected filter và photo category vẫn du-lich.
Backend regression rename khi đang có ảnh, reload categories/album,
so sánh persisted photo snapshot trước/sau và kiểm tra không phát sinh upload/delete Drive.

## 16. i18n

Thêm:
- `admin.library_ui.edit_category`: Sửa danh mục / Edit category.
- `admin.library_ui.category_updated`: Cập nhật danh mục thành công. / Category updated successfully.
- `admin.library_ui.category_cannot_edit`: Không thể chỉnh sửa danh mục mặc định. / Default categories cannot be edited.
- `featureSuccessKeys.categoryUpdate`.
- Mapping `LIBRARY_CATEGORY_CANNOT_EDIT` trong featureMessages.ts.

Giữ mapping CANNOT_DELETE, IN_USE, NOT_FOUND, INVALID.
Không parse English backend message hoặc render raw text cho known codes.
Các thông báo member/album/photo/category CRUD khác vẫn dùng localized success keys.
Modal create/edit reuse LibraryCategoryModal, có prefill và nút Tạo/Create hoặc Lưu/Save.

## 17. Backend FamilyTree role tests

`SystemFlowsTests.Family_tree_and_library_require_actual_user_role_and_retire_admin_alias`:
anonymous 401, ADMIN-only 403 cho list/generations/detail và toàn bộ mutations.
Legacy admin suffixes 404.
USER list/generations 200; existing SystemFlows kiểm tra member CRUD/relations.
Actual ADMIN+USER account truy cập FamilyTree và admin stats được phép;
GET /api/me trả đủ roles.
Test cũ kỳ vọng alias 403 đã đổi thành 404.

## 18. Library authorization tests

Cùng HTTP role matrix bao phủ:
GET/POST/PUT/DELETE categories; album list/detail/CRUD; upload, photo PUT/DELETE.
Anonymous 401; ADMIN-only 403.
USER category CRUD/ownership và actual multi-role access được suite kiểm tra.
Không thay mocked remote storage boundary.

## 19. Backend category CRUD tests

Mở rộng LibraryFlowsTests:
create Du lịch → du-lich; collision/default ensure giữ nguyên.
PUT trimmed Du lịch gia đình → slug không đổi.
Foreign PUT 404/code; tất cả bốn defaults PUT 409/code.
Null/empty/whitespace/101 chars PUT 400/code.
Rename in-use success, Name/UpdatedAt đổi, CreatedAt/owner/slug/default giữ nguyên.
Persisted photo response và storage calls giữ nguyên.
Reload vẫn đúng; delete default/in-use/unused và foreign giữ nguyên.

## 20. Frontend role/navigation tests

Thêm `tests/family-role.test.cjs`, script `npm run test:roles` (6 tests).
Thực thi actual AuthProvider, ProtectedRoute, UserMenu và Header với hook harness.
Kiểm tra ADMIN-only/USER/ADMIN+USER, legacy response fallback, anonymous/loading.
Kiểm tra bốn route aliases và AdminDashboard không mount FamilyTree.
Browser mocked API: Admin-only direct /FamilyTree redirect /Home/Admin;
không gọi FamilyTree/Library API; desktop và mobile web không có FamilyTree menu.

## 21. Frontend category edit tests

`test:library`: PUT đúng endpoint, body chỉ name, coded error propagate.
`test:library-ui`: edit title/prefill/Save, trim, blank validation VI/EN.
`test:i18n`: actual LibraryTab create/edit/delete callbacks cả VI/EN;
default selected không có actions; rename label đổi và slug vẫn selected;
upload preselection vẫn du-lich; delete reset all.
Browser: create/edit/blank validation, selected chip, PUT body, in-use delete error,
album/photo CRUD, upload preview cleanup, image fallback/lightbox/original, dark/mobile.
Đã xem screenshot library-mobile: nhãn mới và actions hiển thị trong viewport.

## 22. Backend build/test result

| Check | Result |
| --- | --- |
| dotnet clean (Debug ban đầu) | Không thành công: DLL Debug bị khóa và profile môi trường thiếu đường dẫn NuGet |
| Restore backend + test project với profile/config NuGet tạm | PASS, packages đã có trong cache |
| dotnet clean -c Release | PASS, 0 warning/error |
| dotnet build -c Release --no-restore | PASS, 0 errors; NU1900 do không truy cập được vulnerability feed |
| dotnet test Tests/Backend-MyLife.Tests.csproj -c Release --no-restore | PASS: 82/82, 0 skipped |
| Apps Script mocked regression harness | PASS: 42/42 |

Test database là PostgreSQL cluster mới trong temp, chỉ bind 127.0.0.1:55439.
MYLIFE_TEST_DATABASE trỏ mylife_validation trong cluster này.
Không dùng application database. Chỉ áp dụng migrations hiện có vào isolated test DB.
Windows EventLog logging bị tắt riêng trong test process vì runner không có quyền ghi log.
Không thay cấu hình môi trường toàn máy hoặc dừng backend đang chạy của người dùng.
Lần chạy đầu gặp lỗi test timestamp precision; đã so sánh persisted-before với
persisted-after để tránh khác biệt timestamp memory/roundtrip, suite cuối pass.

## 23. Frontend build/lint/test result

| Check | Result |
| --- | --- |
| npm install | PASS, không thay dependency/lockfile |
| npm run build | PASS; Vite cảnh báo chunk >500kB |
| npm run lint | PASS |
| npm run test:i18n | PASS 11/11 |
| npm run test:library | PASS 4/4 |
| npm run test:library-ui | PASS 7/7 |
| npm run test:roles | PASS 6/6 |
| npm run test:avatar | PASS 12/12 |
| Browser regression | PASS, actual Chromium + mocked API |

Tổng Node frontend tests: 40/40.
Edge/cached Chromium launch mặc định timeout trong restricted runner; browser pass
với cached Chromium và optional LIBRARY_UI_NO_SANDBOX=1 chỉ dành cho test harness.
Normal harness vẫn giữ browser sandbox mặc định.
Screenshots: C:/Users/admin/AppData/Local/Temp/mylife-library-browser-yF1UG1/
(library-mobile.png, library-dark.png, lightbox-mobile.png).

Regression evidence: Account Avatar, FamilyMember Avatar storage, Auth,
Library Album, Photo upload, Preview, Drive image fallback, Lightbox,
FamilyTree USER đều pass bằng automated tests/mocked storage.
Không thực hiện live upload vào Drive hoặc full manual UAT với account thật.

## 24. Migration

NO NEW MIGRATION.
Không sửa 20261006090341_AddLibraryCategories hoặc migration/snapshot khác.
Schema library_categories đã có Name và UpdatedAt.
Không chạy dotnet ef migrations add; không reset/drop application database.

## 25. Apps Script / Mobile / Git / Deploy

NO Apps Script modification.
Không thay GOOGLE_APPS_SCRIPT_AVATAR.gs, properties, folder, version hoặc deploy.
Không sửa Mobile.
Không git push, không deploy, không tạo commit.
Các thay đổi giữ local trên feature_minhduc.

## 26. Manual steps tiếp theo

1. Review diff và report; khởi động lại backend đang chạy để nạp controller/DTO mới.
   Debug output bị khóa nên automated validation dùng Release; không kill app đang chạy.
2. Refresh frontend/session sau khi backend mới chạy để /api/me trả roles hiện tại.
3. Smoke test account ADMIN-only: không có menu, /FamilyTree redirect,
   /api/family-tree và /api/library/categories trả 403.
4. Smoke test USER: Members CRUD, Anniversaries, Library, Map/Mindmap.
   Tạo Du lịch, gán ảnh, rename Du lịch gia đình, reload; slug/filter vẫn du-lich.
   Kiểm tra custom unused delete, in-use localized error, VI/EN, dark/mobile.
5. Không cần migration hoặc Apps Script deployment cho thay đổi này.

Để chạy lại suite, dùng disposable MYLIFE_TEST_DATABASE như Tests/README.md;
không trỏ vào development/production DB. Browser check intercept mọi /api request.
