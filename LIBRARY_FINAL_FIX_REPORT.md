# Library final fixes — 06/10/2026

Áp dụng trên code hiện có của hai repository, branch `feature_minhduc`.
Đã giữ các thay đổi i18n FamilyTree/Library trước lượt này. Không tạo lại module,
không sửa Mobile, không commit/push/deploy và không apply migration vào DB ứng dụng.

## 1. Nguyên nhân của năm lỗi

| Lỗi | Nguyên nhân trong code trước sửa | Cách sửa |
| --- | --- | --- |
| Tên album có `*` | Dấu nằm trong VI/EN locale; input dùng HTML required bên cạnh custom validation. | Bỏ dấu trong locale và HTML required, giữ noValidate + kiểm tra trim bằng error key. |
| Nút tạo album có hai `+` | Button có icon Plus và locale có thêm ký tự `+`. | Text locale không có `+`, giữ một icon. |
| Field lệch và thiếu preview | Input/select/textarea dùng style khác nhau; upload chỉ liệt kê tên file. | Wrapper riêng cho Library với input/select cùng style; preview grid bằng object URL. |
| Ảnh Drive không có đường hiển thị dự phòng | Gallery/lightbox dùng img trực tiếp; DTO/mapping chưa truyền DriveFileId, nên không thể thử thumbnail hoặc mở đúng file gốc. | DTO read-only + LibraryImage dùng chung, giới hạn số lần thử, placeholder localized. Không xác nhận storage corrupt: live test bytes vẫn đúng. |
| Không tạo được category riêng | Frontend fixed union/array; backend validation chỉ nhận bốn giá trị. | Registry per-user + API CRUD + migration mới; upload/edit kiểm tra slug trong registry của caller. |

## 2. File sửa trong lượt này

Backend:

- `Features/Library/Controllers/LibraryController.cs`
- `Features/Library/DTOs/LibraryPhotoMetadataDto.cs`, `PhotoDto.cs`
- `Features/Library/Services/ILibraryService.cs`, `LibraryService.cs`
- `Shared/Data/AppDbContext.cs`, `DatabaseStartup.cs`
- `Migrations/AppDbContextModelSnapshot.cs`
- `Tests/LibraryFlowsTests.cs`, `API_CONTRACT.md`

Frontend:

- `src/features/admin/services/libraryService.ts`, `featureMessages.ts`
- `src/features/admin/components/family-tree/LibraryTab.tsx`, `LibraryAlbumModal.tsx`
- `LibraryUploadModal.tsx`, `LibraryPhotoEditModal.tsx`, `LibraryPhotoFormFields.tsx`
- `PhotoLightboxModal.tsx`, `types.ts` trong cùng thư mục
- `src/shared/locales/vi/admin.json`, `src/shared/locales/en/admin.json`
- `package.json`, `tests/library.test.cjs`, `tests/feature-i18n.test.cjs`
- `tests/library-ui.browser.cjs`, `tests/LIBRARY_README.md`

Workspace còn có diff FamilyTree, NotificationContext, API error codes và tests
từ lượt i18n trước; những thay đổi đó được giữ nguyên, không rollback.

## 3. File mới

Backend:

- `Shared/Entities/LibraryCategory.cs`
- `Features/Library/DTOs/LibraryCategoryDto.cs`, `CreateLibraryCategoryDto.cs`
- `Features/Library/Services/LibraryService.Categories.cs`, `LibraryCategorySlug.cs`
- Hai file migration mới liệt kê dưới đây
- `Tests/LibraryCategoryMigrationTests.cs`, `Tests/verify-live-library-images.cjs`
- Báo cáo này

Frontend:

- `src/features/admin/components/family-tree/LibraryCategoryModal.tsx`
- `src/features/admin/components/family-tree/LibraryImage.tsx`
- `tests/library-ui.test.cjs`

## 4. Migration mới duy nhất

EF CLI 10.0.11 đã scaffold:

```text
Migrations/20261006090341_AddLibraryCategories.cs
Migrations/20261006090341_AddLibraryCategories.Designer.cs
```

Snapshot cập nhật qua EF. Up chỉ tạo `library_categories`, unique index/FK và
nới `library_photos.category` từ varchar(20) thành varchar(64). Không đổi URL,
không drop/recreate albums/photos. Hai migration `AddLibraryModule` và
`ExtendLibraryPhotoMetadata` không thay đổi. Design-time factory tạm để scaffold
đã được xóa. Migration chưa apply vào DB ứng dụng/production.

Down do EF sinh sẽ xóa registry mới và thu nhỏ cột category; chỉ dùng sau khi
đã đánh giá dữ liệu custom. Không có thao tác Down trong lượt này.

## 5. Schema library_categories

| Cột | Kiểu / ràng buộc |
| --- | --- |
| id | bigint identity, PK |
| created_by_user_id | integer NOT NULL, FK users.id, Restrict |
| name | varchar(100) NOT NULL |
| slug | varchar(64) NOT NULL |
| is_default | boolean NOT NULL |
| created_at / updated_at | timestamptz NOT NULL, CURRENT_TIMESTAMP |

Unique `(created_by_user_id, slug)`. FK Restrict phù hợp ownership Library và
không tạo cascade DB dẫn tới Drive orphan. `LibraryPhoto.Category` vẫn là string.

## 6. Default category strategy

Ensure idempotent theo từng user trước GET categories, upload validation và
photo edit validation. Defaults: photos, decrees, events, temple; display name
backend giữ VI, frontend dùng locale cho `isDefault=true`. Custom name giữ nguyên.
Sort isDefault DESC, name ASC, id ASC. Không cần seed user thủ công.

## 7. Slug và concurrency

Trim name, required, max 100; Unicode FormD bỏ dấu kết hợp, Đ/đ thành d,
lowercase, ký tự khác ASCII chữ/số thành `-`, collapse/trim, tối đa 64.
Du lịch → du-lich; Đám giỗ → dam-gio; Ảnh cưới 2026 → anh-cuoi-2026.
Tên không còn ASCII dùng `category`; `all` dành riêng cho UI nên thành
`all-category`. Collision dùng -2, -3… và cắt base để tổng vẫn ≤64.

Transaction khóa row user bằng PostgreSQL FOR UPDATE trước category/album lock,
giữ đến commit. Các instance API của cùng user được tuần tự hóa khi ensure,
tạo/xóa category và gán category cho photo. Unique index là ràng buộc bổ sung.
Sáu GET đồng thời chỉ tạo bốn defaults; sáu POST cùng tên đều 201 và slug khác nhau.

## 8. Category API

| Method / path | Kết quả |
| --- | --- |
| GET /api/library/categories | 200 array `{id,name,slug,isDefault}` |
| POST /api/library/categories | Body chỉ `{name}`; 201 category |
| DELETE /api/library/categories/{id} | 204 khi xóa custom chưa dùng |

Active authentication như Library hiện có. Owner/slug/isDefault không nhận từ
client; các field client cố chèn bị bỏ qua. Không có category PUT trong phase này.

## 9. Ownership validation

Upload/edit nhận string slug tối đa 64, ensure defaults rồi kiểm tra registry
thuộc authenticated user. Unknown/foreign slug bị từ chối; `all` không được lưu.
Album/photo ownership hiện có vẫn giữ. Category ID của user khác trả 404 khi xóa.

## 10. Quy tắc xóa

- Default: 409 `LIBRARY_CATEGORY_CANNOT_DELETE`.
- Custom đang được photo trong album của caller dùng: 409 `LIBRARY_CATEGORY_IN_USE`.
- Custom chưa dùng: 204.
- Không tồn tại/khác owner: 404 `LIBRARY_CATEGORY_NOT_FOUND`.

Không xóa ảnh hoặc đổi category của ảnh khi xóa category. UI chỉ hiện icon xóa
cho custom đang chọn, dùng modal xác nhận và thông báo VI/EN theo error code.

## 11. Upload preview

Files → `{file, previewUrl}` bằng `URL.createObjectURL`; grid thumbnail, tên,
kích thước và nút bỏ ảnh. Không convert base64 để preview. JPEG/PNG/WebP/GIF,
≤5 MiB/file, 1–20 files; validation MIME/kích thước/count vẫn hiện error key.
Backend/Apps Script magic signature và integrity verification giữ nguyên.

## 12. Object URL cleanup

Effect cleanup revoke toàn bộ URLs của selection trước khi files thay đổi,
remove hoặc component unmount. Khi bỏ một ảnh, URL cũ của các ảnh còn lại cũng
được revoke rồi tạo mới; không giữ URL đã revoke trong render mới. Đóng/upload
thành công làm modal unmount và thu hồi URLs. Unit và browser spy xác nhận tổng
URLs tạo bằng tổng URLs revoke, không bỏ sót.

## 13. Field sizing

Wrapper riêng Library: min-height 56px, radius 16px, cùng border/theme/padding/
font/focus cho Album, Title, Category, DisplayDate và Author. Label nổi được
đặt thống nhất; category/date cùng row desktop, stack mobile. Textarea giữ
chiều cao riêng với theme/radius tương ứng. Không sửa global Input.tsx.
Edge kiểm tra computed input/select heights đều 56px, padding/radius/font giống nhau.

## 14. Album *

VI “Tên album”, EN “Album name”, không HTML required. Form noValidate; blank
hoặc whitespace không gọi API/không đóng modal, hiện lỗi theo locale. Error lưu
key nên khi đổi ngôn ngữ, lỗi đang hiện được dịch lại.

## 15. Duplicate +

VI “Tạo album”, EN “Create album”; icon Plus vẫn một lần. Audit text tạo
category và upload không thêm `+` bên cạnh icon.

## 16. DriveFileId trong DTO

PhotoDto và TypeScript LibraryPhotoDto trả `driveFileId`; mapped LibraryPhoto
giữ ID cho gallery/lightbox. Write metadata DTO không có ID nên không thay đổi
storage identity từ client. Không cần migration riêng cho DTO.

## 17. Fallback ảnh

LibraryImage dùng chung trong gallery (cover) và lightbox (contain): stored URL,
sau đó `https://drive.google.com/thumbnail?id={encodedId}&sz=w1600`. Candidate
trùng được loại, mỗi URL một lần; stale onError không bỏ qua candidate kế.
Đổi source remount attempt state theo key; hết URLs hiện icon + VI/EN placeholder,
không broken image icon hoặc retry vô hạn. Metadata/edit/delete vẫn giữ.
Original mở `/file/d/{id}/view` với noopener,noreferrer; legacy không ID dùng URL.

## 18. Kết quả ảnh thật và Dog.webp

`node Tests/verify-live-library-images.cjs` đã PASS với Drive Web App đang deploy.
Tạo album test riêng, chọn file thật trong modal → preview → upload storage thật
→ gallery → F5 → lightbox → kiểm tra link original. Application API được intercept
để tránh ghi DB ứng dụng; HTTP/PostgreSQL thật được kiểm tra riêng bởi backend tests.

| Fixture | Bytes | MD5/MIME/size và original download | Gallery / F5 / lightbox |
| --- | ---: | --- | --- |
| Dog.webp | 562 | PASS | PASS; primary lh3 |
| A.jpg | 767 | PASS | PASS; primary lh3 |
| B.png | 134 | PASS | PASS; primary lh3 |
| C.gif | 42 | PASS | PASS; primary lh3 |

Dog.webp là fixture màu 32×32 được đặt tên để kiểm tra WebP; chưa kiểm tra file
Dog.webp gốc của người dùng vì file đó không được cung cấp. GIF là fixture 1×1.
Original link đúng Drive ID và bytes gốc tải xuống khớp MD5; không mở tab tương tác
đăng nhập của người dùng. Album test `1ApHkHwe7FxKpG5vwjbxlJ2PZWxm6MgGP` đã trash
trong finally. Không thao tác lên account/member avatar hoặc album hiện có.

Artifacts live: `C:\Users\admin\AppData\Local\Temp\mylife-library-live-VLRBS7`
(preview/gallery/lightbox từng định dạng).
Browser API-mock: `C:\Users\admin\AppData\Local\Temp\mylife-library-browser-Tlqvt8`
(mobile/lightbox/dark). Đã xem ảnh preview và mobile.
Fallback primary lỗi → thumbnail và cả hai lỗi → placeholder PASS trong browser
với response giả lập; không kết luận primary Drive đang lỗi vì live lần này primary hoạt động.

## 19. Apps Script

Không sửa `GOOGLE_APPS_SCRIPT_AVATAR.gs`, không đổi contracts, sharing, MD5,
Drive overwrite/delete hoặc Script Properties. Kiểm tra git xác nhận source .gs
và appsettings.json không có diff.

## 20. Redeploy

Không cần redeploy Apps Script. Cần chạy backend code mới và database migration
category trước khi dùng frontend mới; backend đang chạy của người dùng không bị dừng.

## 21. I18n regression

`npm.cmd run test:i18n`: 11/11 PASS. Bao gồm FamilyTree create/update/delete VI/EN,
không hiển thị raw backend English, Library album/photo/category callbacks,
mapping error code/status và lỗi đang mở đổi locale. Giữ NotificationContext
localization trước đó; category create/delete errors và image placeholder có VI/EN.

## 22. Backend build/test

Clean/restore/build dùng Release: PASS, build 0 warnings/errors. Debug exe đang
bị process backend người dùng giữ nên không dừng process để build Debug.

```powershell
dotnet clean -c Release
dotnet restore Tests/Backend-MyLife.Tests.csproj
dotnet build Tests/Backend-MyLife.Tests.csproj -c Release --no-restore
$env:MYLIFE_TEST_DATABASE = '<isolated PostgreSQL connection>'
dotnet test Tests/Backend-MyLife.Tests.csproj -c Release --no-restore
```

81/81 PASS trên PostgreSQL test loopback port 55439, DB `mylife_validation`.
Gồm Library CRUD/ownership/batch rollback/concurrency/storage, Auth/FamilyTree,
account/member avatar, category defaults/collisions/ownership/delete/slug/migration.
Không dùng app DB. Server PostgreSQL/Vite test riêng được dừng sau kiểm tra.

## 23. Frontend và Apps Script regression

| Check | Kết quả |
| --- | --- |
| npm install --ignore-scripts --no-audit --no-fund | PASS, up to date |
| npm run build | PASS; Vite còn cảnh báo bundle >500kB hiện có |
| npm run lint | PASS |
| npm run test:i18n | 11/11 |
| npm run test:library | 3/3 |
| npm run test:library-ui | 6/6 |
| npm run test:avatar | 12/12 |
| node tests/library-ui.browser.cjs | PASS, API/image fault responses giả lập |
| node Tests/verify-live-library-images.cjs | PASS, Drive thật và DB ứng dụng giả lập |
| Apps Script account/member/library node regressions | 42/42 |

Browser gồm album CRUD/blank validation, file previews/remove/cleanup bốn định dạng,
computed field sizing, category create/preselection/filter/used-delete conflict/
unused delete, fallback/reload/placeholder/original, photo edit/delete retry,
empty/error states và mobile/light/dark. Không có pageerror.

## 24. Migration/data-preservation test

Test tạo schema PostgreSQL riêng bằng GUID, migrate đến
`ExtendLibraryPhotoMetadata`, insert user/album/photo cũ rồi upgrade latest.
Album/name, photo ID/category decrees/URL/Drive ID giữ nguyên; category table mới
rỗng, category dài 64 lưu được; snapshot không có pending model changes.
Cuối test drop đúng schema test riêng. Không reset hoặc drop tables ứng dụng.
DatabaseStartup legacy baseline cũng nhận biết table mới, giữ các tests cũ pass.

## 25. Các bước tiếp theo cho người dùng

1. Review diff hai repo và migration mới. Chuẩn bị backup DB theo quy trình hiện có.
2. Chọn thời điểm chạy backend mới. Nếu dùng startup hiện tại có auto-migrate,
   khởi động backend mới sẽ apply migration; hoặc apply chủ động bằng EF trước.
   Trong lượt này chưa apply DB ứng dụng.
3. Nếu dùng EF CLI: từ Backend-MyLife chạy
   `dotnet ef database update --context AppDbContext --configuration Release`
   với connection/config dành cho môi trường muốn cập nhật. Chỉ thực hiện sau review.
4. Chạy frontend mới. Vào Library lần đầu sẽ tạo bốn defaults cho tài khoản.
5. Thử tạo album trống/whitespace (modal giữ và lỗi VI/EN), tạo album có tên,
   tạo “Du lịch”, upload ảnh thật của bạn, kiểm tra preview/bỏ file/filter/F5/
   lightbox/original. Thử xóa category đang dùng (bị chặn), xóa custom chưa dùng
   (thành công). Đổi VI/EN để kiểm tra default labels và lỗi.
6. Không cần sửa/deploy Apps Script hoặc đổi Script Properties. Không cần upload
   lại ảnh cũ: fallback tận dụng DriveFileId đã có trong DB.
