# Library UI integration — 06/10/2026

## Kết quả audit trước khi sửa

Hai repo đang ở branch `feature_minhduc`. Implementation Phase 1 đã tồn tại
trong workspace, gồm toàn bộ backend/Drive cần cho CRUD album và upload/xóa ảnh.
Các file Phase 1 chưa commit được giữ lại. Không tạo lại module, entity,
controller, storage service hoặc tables; không thêm route `/library`.

Các file đã có từ prompt Phase 1:

- `Shared/Entities/LibraryAlbum.cs`, `LibraryPhoto.cs`.
- `Features/Library/Controllers/LibraryController.cs`.
- `Features/Library/DTOs/{CreateAlbumDto,UpdateAlbumDto,AlbumDto,AlbumDetailDto,PhotoDto}.cs`.
- `Features/Library/Services/{ILibraryService,LibraryService,ILibraryStorageService,GoogleDriveLibraryStorageService,LibraryUploadRules}.cs`.
- `Migrations/20261006042455_AddLibraryModule.cs` và `.Designer.cs`.
- `Tests/LibraryFlowsTests.cs`, `GoogleDriveLibraryStorageServiceTests.cs`,
  `apps-script-library.test.cjs`, `LIBRARY_PHASE1_REPORT.md`.

Phase 1 cũng đã sửa `AppDbContext`, snapshot, `DatabaseStartup`, `Program.cs`
(DI/client), Apps Script (Library actions), test fixture/csproj, API contract và
test README. Chi tiết lịch sử: [LIBRARY_PHASE1_REPORT.md](LIBRARY_PHASE1_REPORT.md).

Audit migration trước sửa xác nhận chỉ có **20261006042455_AddLibraryModule**
cho Library, với hai bảng đã được mô tả trong snapshot. Đây là audit source và
migration; không truy cập hoặc khẳng định trạng thái migration của DB ứng dụng.

## Schema được reuse và metadata bổ sung

| Table | Columns có trong Phase 1 |
| --- | --- |
| `library_albums` | `id` bigint identity; `name` varchar(150); `description` varchar(2000) nullable; `drive_folder_id` varchar(255) nullable; `cover_photo_id` bigint nullable; `created_by_user_id` int; `created_at`, `updated_at` timestamptz |
| `library_photos` | `id` bigint identity; `album_id` bigint; `drive_file_id` varchar(255); `url` text; `file_name` varchar(255); `content_type` varchar(100); `file_size` bigint; `caption` varchar(2000) nullable; `sort_order` int default 0; `taken_at` timestamptz nullable; `created_at` timestamptz |

UI còn thiếu title/category/ngày hiển thị/nguồn. `caption` đã tương đương mô
tả nên được reuse; API trả cả `caption` và alias `description`. `taken_at` là
timestamp, không thay thế văn bản năm/ngày tự do trong UI.

Migration bổ sung duy nhất: **20261006063103_ExtendLibraryPhotoMetadata**,
EF scaffold cùng Designer và snapshot. Up chỉ thêm bốn cột vào `library_photos`:

| Column mới | Kiểu / giá trị mặc định |
| --- | --- |
| `title` | varchar(200), nullable |
| `category` | varchar(20), required, default `photos` cho ảnh cũ |
| `display_date` | varchar(100), nullable |
| `author` | varchar(200), nullable |

Không thêm cột description trùng caption, không sửa AddLibraryModule,
không create/drop/reset table hoặc database. FK/index/storage identity của Phase
1 được giữ nguyên. DB ứng dụng chưa được apply migration trong lượt này.

## Backend API

Đã reuse các endpoint Phase 1 và chỉ mở rộng metadata:

| Method | Endpoint | Kết quả / thay đổi |
| --- | --- | --- |
| POST | `/api/library/albums` | 201 album, giữ cơ chế tạo Drive folder |
| GET | `/api/library/albums` | Album của user hiện tại |
| GET | `/api/library/albums/{id}` | Detail + photos, bổ sung metadata trong PhotoDto |
| PUT | `/api/library/albums/{id}` | Sửa tên/mô tả, giữ Drive folder |
| DELETE | `/api/library/albums/{id}` | Drive trước, DB sau, cascade photos |
| POST | `/api/library/albums/{id}/photos` | Batch `files` và metadata chung trong một multipart request |
| PUT | `/api/library/photos/{id}` | **Bổ sung**: sửa metadata trong DB, giữ nguyên Drive ID/bytes/URL |
| DELETE | `/api/library/photos/{id}` | Drive trước, DB sau |

Metadata: `title`, `category`, `displayDate`, `description`, `author`.
Category chỉ nhận `photos`, `decrees`, `events`, `temple`; `all` bị từ chối.
Title/author max200, displayDate max100, description max2000; chuỗi trắng về null.
PUT thay toàn bộ metadata có thể sửa; không sửa bytes, filename, album hoặc takenAt.
Các mutation vẫn kiểm tra ownership và giữ row lock theo album. Admin cũng chỉ
truy cập album của mình, đúng contract Phase 1.

Backend đã được kiểm tra qua HTTP integration trên PostgreSQL tạm và storage
giả lập, gồm batch metadata, GET roundtrip, owner-only PUT, category invalid và
khẳng định PUT không upload thêm hoặc đổi Drive file ID. Không gọi Library API
trên DB/Drive ứng dụng trong lượt này.

## Apps Script và avatar

Audit xác nhận source đã có bốn action:
`library_create_album_folder`, `library_upload_photo`, `library_delete_photo`,
`library_delete_album`, cùng marker `libraryContractVersion: library-phase1-v1`.

Lượt tích hợp UI này không sửa Apps Script, `GoogleDriveLibraryStorageService`,
`ILibraryStorageService`, `GoogleDriveAvatarService`, `AVATAR_FOLDER_ID` hoặc
`AvatarDriveFileId`. Apps Script vẫn hiển thị modified trong Git vì thay đổi
Library từ Phase 1 trước đó, không phải sửa thêm cho metadata UI.

Storage vẫn là `MyLife_Library/album_<id>/photo_<unique>.<ext>`, root từ
`LIBRARY_FOLDER_ID`. Metadata UI lưu PostgreSQL nên không cần action Drive mới.

**Không cần redeploy Apps Script cho các thay đổi UI/metadata này nếu deployment
đã chứa Phase 1 Library.** Nếu trước đó mới deploy bản avatar, vẫn cần setup
`LIBRARY_FOLDER_ID` và deploy source Phase 1 như báo cáo cũ; GET WebAppUrl phải
có marker Library trên. Không tự deploy hoặc sửa Script Properties.

## Frontend

Trước sửa, `LibraryTab` chứa mảng ảnh Unsplash hardcode và upload placeholder
trong `FamilyTreeManager`; chưa có Library API service. Đã xóa hoàn toàn mảng
mock/Unsplash và callback success giả của Library, thay bằng API thật.

Reuse gallery cards/grid, category chips, theme, shared Button/Input/Modal và
`PhotoLightboxModal`. Thêm album filter cạnh chips, tạo/sửa/xóa album,
upload nhiều file kèm metadata chung, sửa metadata từng ảnh và xác nhận xóa.
Thao tác chỉ báo success sau response thành công; failure giữ modal và thông
tin đang nhập để thử lại. Loading/error/retry/empty dùng state thật, không
fallback mock. Empty: **Chưa có tư liệu nào trong thư viện.**

Tất cả API calls tập trung trong service dùng `apiClient`, cookie credentials
và AJAX header hiện có. GET được abort khi đổi album/unmount; không tự retry POST
upload. Khi chọn tất cả album, gallery lấy detail các album rồi lọc category ở
client; Phase 1 chưa thêm pagination. Title trống hiển thị filename gốc.

Lightbox hiển thị title, description, author, displayDate và ảnh thật; thêm sửa,
xóa, Escape và khóa scroll. Render qua portal để fixed overlay nằm đúng viewport
khi FamilyTree chứa ancestor có backdrop/transform. Giữ cấu trúc hiển thị cũ.

Files sửa:

- `src/features/admin/components/FamilyTreeManager.tsx`.
- `src/features/admin/components/family-tree/{LibraryTab,PhotoLightboxModal}.tsx`, `types.ts`.
- `src/shared/locales/{vi,en}/admin.json`, `package.json`.

Files thêm:

- `src/features/admin/services/libraryService.ts`.
- `src/features/admin/components/family-tree/{LibraryAlbumModal,LibraryUploadModal,LibraryPhotoEditModal,LibraryPhotoFormFields}.tsx`.
- `tests/library.test.cjs`, `tests/library-ui.browser.cjs`, `tests/LIBRARY_README.md`.

Backend sửa thêm: LibraryPhoto/AppDbContext/snapshot, LibraryController,
ILibraryService/LibraryService, PhotoDto, LibraryFlowsTests, API contract,
test README; thêm LibraryPhotoMetadataDto và migration metadata nói trên.

## Validation

| Check | Result |
| --- | --- |
| `dotnet build MyLife.csproj -c Release --no-restore` | Pass, 0 warning/error |
| Backend tests (isolated PostgreSQL) | **48/48 pass**, gồm avatar/auth/FamilyTree/Library/migrations |
| Apps Script Node tests | **21/21 pass**, 12 avatar + 9 Library |
| Frontend build | Pass; Vite còn cảnh báo chunk lớn hơn 500 kB |
| Frontend lint | Pass |
| Frontend Library service tests | **3/3 pass** |
| Frontend avatar regressions | **12/12 pass** |
| Edge headless UI, API interception | Pass: gallery/filter, lightbox metadata, album CRUD, JPEG/PNG/WebP multipart, photo edit, delete failure/retry, API error/empty |
| `git diff --check` ở cả hai repo | Pass |

UI harness đọc FormData thật tại fetch boundary (CDP có thể bỏ binary multipart),
giả lập toàn bộ API responses; không viết backend/Drive thật. Đã kiểm tra ảnh chụp
light/dark/mobile và lightbox nằm trọn viewport 390×844. Backend tests dùng
PostgreSQL riêng, không dùng DB ứng dụng. Các giới hạn cleanup/storage của Phase 1
vẫn được mô tả trong báo cáo cũ.

## Manual tiếp theo

1. Dừng backend cũ và chạy bản mới bằng cấu hình DB hiện tại. Startup
   `MigrateAndSeedAsync` sẽ apply migration đang pending theo thứ tự:
   AddLibraryModule (nếu chưa có), rồi ExtendLibraryPhotoMetadata. Không reset DB.
2. Xác nhận `__EFMigrationsHistory` có `20261006063103_ExtendLibraryPhotoMetadata`
   và `library_photos` có bốn cột mới. Với DB legacy có tables nhưng thiếu history,
   xử lý theo hướng dẫn baseline hiện có; không tạo migration Library lần nữa.
3. Nếu Phase 1 Drive chưa setup/deploy, thực hiện bước Library root/property/deploy
   trong `LIBRARY_PHASE1_REPORT.md`; giữ URL deployment và avatar property.
4. Chạy frontend (`npm.cmd run dev`), đăng nhập, mở `/FamilyTree` → Thư viện gia
   đình. Tạo album, upload A.jpg/B.png/C.webp cùng metadata, kiểm tra album/category,
   lightbox, sửa một ảnh, reload để xác nhận metadata persisted.
5. Kiểm tra Drive album folder và file thật; xóa một ảnh rồi album test, xác nhận
   DB và Drive khớp. Kiểm tra user khác không truy cập album test.
6. Kiểm tra avatar trên ứng dụng: đổi ảnh nhiều lần giữ file ID, xóa ảnh, Google
   login không ghi đè avatar manual.

Không commit/push, không deploy, không sửa cấu hình/secret hoặc DB ứng dụng.
