# MyLife Library / Album — Phase 1, 06/10/2026

## Phạm vi

Backend branch `feature_minhduc`; thêm module Library riêng. Không triển khai
UI, không sửa frontend/mobile, không commit/push, không sửa secret/appsettings.
Không thay controller/storage/avatar identity, checksum, sync Google avatar.
Apps Script mở rộng bằng các action Library riêng; bodies của các hàm upload,
delete và sync avatar được giữ nguyên.

## Schema và migration

Entity mới: `LibraryAlbum`, `LibraryPhoto` trong `Shared/Entities`.
Migration EF Core 10.0.11 tạo: **20261006042455_AddLibraryModule** và Designer;
`AppDbContextModelSnapshot` được EF scaffold tự động. Migration Up chỉ thêm hai
bảng, FK và index; không sửa/drop bảng hiện có hoặc migration cũ.

| Table | Columns |
| --- | --- |
| library_albums | id bigint identity, name varchar(150) required, description varchar(2000) nullable, drive_folder_id varchar(255) nullable, cover_photo_id bigint nullable, created_by_user_id int required, created_at/updated_at timestamptz UTC |
| library_photos | id bigint identity, album_id bigint required, drive_file_id varchar(255) required, url text required, file_name varchar(255) required, content_type varchar(100) required, file_size bigint, caption varchar(2000) nullable, sort_order int default 0, taken_at timestamptz nullable, created_at timestamptz UTC |

FK:

- Album.created_by_user_id → users.id, **Restrict**; album metadata không bị mất
  khi user bị xóa mà Drive chưa được cleanup. Cần xóa album trước khi xóa user.
- Photo.album_id → library_albums.id, **Cascade** DB photo records.
- Album.cover_photo_id → library_photos.id, **SetNull**; cover không có file riêng.

Index:

- Unique `library_albums.drive_folder_id` (nullable trong transaction tạo).
- Unique `library_photos.drive_file_id`.
- Album.cover_photo_id; Album(created_by_user_id, updated_at).
- Photo(album_id, sort_order, created_at).

Không lưu bytes/base64 trong PostgreSQL. Không có API chọn cover/edit caption
hoặc takenAt ở Phase 1; schema đã chuẩn bị. API tương lai phải kiểm tra cover
photo thuộc chính album trước khi gán.

`DatabaseStartup` được điều chỉnh để Library tables chưa tồn tại không làm hỏng
opt-in legacy baseline trước migration; nếu Library tables đã có mà thiếu
migration history thì dừng để review, không tự baseline/đè schema.
Đã test migration trên cluster PostgreSQL tạm riêng; chưa apply DB ứng dụng.
Backend startup hiện có `MigrateAndSeedAsync`, sẽ apply migration khi chạy bản mới.

## API và ownership

| Method | Endpoint | Success |
| --- | --- | --- |
| POST | /api/library/albums | 201 AlbumDto + Location |
| GET | /api/library/albums | 200 AlbumDto[] |
| GET | /api/library/albums/{albumId} | 200 AlbumDetailDto |
| PUT | /api/library/albums/{albumId} | 200 AlbumDto |
| POST | /api/library/albums/{albumId}/photos | 201 PhotoDto[], multipart field files |
| DELETE | /api/library/photos/{photoId} | 204 |
| DELETE | /api/library/albums/{albumId} | 204 |

Routes cần authenticated active user, từ cookie HttpOnly hoặc Bearer theo hệ
thống hiện có. Lấy user từ claims + DB, không nhận ownership userId từ client.
Mọi GET/PUT/DELETE/UPLOAD chỉ truy cập album chính mình; admin không có bypass.
Khác owner trả 404. List sort updatedAt DESC (id DESC tie-break); photos sort
sortOrder ASC, createdAt ASC, id ASC.

Success trả DTO/array trực tiếp; errors `{success:false,message}`. Validation
400, auth 401, inaccessible 404, storage 502, DB/unavailable folder 503.
Body quá lớn có thể bị từ chối 413 trước controller.

Name required/trim/max150, description max2000. Files 1..20, mỗi file max5MiB,
request max101MiB kể cả multipart overhead. MIME JPEG/PNG/WebP/GIF, jpg alias
normalize jpeg; kiểm tra extension, safe basename và image magic signature ở
backend lẫn Apps Script. Không nhận SVG/HTML/PDF/EXE hoặc filename có path.

## Storage và Apps Script

Service Library riêng: `ILibraryStorageService` / `GoogleDriveLibraryStorageService`.
Reuse `GoogleDrive:WebAppUrl` và IHttpClientFactory; client Library timeout45s,
DI scoped. Không gọi GoogleDriveAvatarService từ Library.

Source đầy đủ để copy: **GOOGLE_APPS_SCRIPT_AVATAR.gs**. Thêm:

```text
library_create_album_folder
library_upload_photo
library_delete_photo
library_delete_album
```

Root lấy từ Script Property **LIBRARY_FOLDER_ID**, phải khác AVATAR_FOLDER_ID;
không hardcode hoặc thêm root ID vào appsettings.
Album folder `album_<DB id>` dưới Library root; lookup getFoldersByName trước
create, cùng script lock hiện có bảo đảm retry không tạo duplicate.
Backend gửi Int64 albumId dạng decimal string để JavaScript không làm tròn ID lớn.
Rename album chỉ sửa DB, giữ nguyên Drive folder ID/name.

Photo name do backend tạo `photo_<Guid N>.<mime extension>`; original filename chỉ
ghi metadata DB. File tạo đúng album; album folder phải là child trực tiếp của
Library root và có stable name hợp lệ. Delete photo kiểm tra parent thuộc album;
delete album không thể trash Library root, avatar root hay folder bên ngoài.
Delete Drive đã trashed là idempotent để retry sau lỗi DB.

Sau upload, Apps Script đọc Drive API v2 metadata `id,md5Checksum,mimeType,fileSize`
và so với blob thực tế. Response success kèm checksum/MIME/size. Backend lại tính
MD5 từ bytes đã gửi, đối chiếu MIME/size và kiểm tra file ID trước khi insert DB.
DB tự tạo URL canonical, không tin URL tùy ý từ response và không lưu t/v.
MD5 chỉ là integrity check media, không dùng cho password/auth.

File chia sẻ ANYONE_WITH_LINK để Web/Mobile đọc trực tiếp, giống avatar. Ownership
bảo vệ metadata API; ảnh qua link trực tiếp vẫn đọc được khi biết link.

## Atomicity, cleanup và concurrency

Create album trong transaction: insert lấy ID → Drive folder → lưu folder ID →
commit. Storage/DB save lỗi rollback row ban đầu và cleanup folder khi biết ID;
không trả success hoặc để album orphan trong DB.

Batch all-or-nothing best effort: validate metadata → upload/verify từng ảnh →
chỉ insert toàn bộ photo rows sau khi tất cả verified → commit. C lỗi sau A/B:
rollback DB, trash tất cả known IDs trong request (kể cả failed upload trả ID).
DB save lỗi sau upload cũng cleanup. Album/photos có sẵn không bị dọn theo batch.
Cleanup dùng cancellation timeout riêng60s, không phụ thuộc request đã abort;
rollback DB lỗi cũng không ngăn thử cleanup Drive. Failed cleanup log album,
folder/file IDs và vẫn trả failure kèm thông báo cleanup incomplete; giữ inner
exception để không mất lỗi gốc. Không log base64/tokens/secret/payload.

Delete photo/album: Drive success trước, rồi DB transaction. Drive fail giữ DB.
Photo đang là cover được clear reference trước DB delete. Album cascade photo rows.
Nếu Drive đã trash nhưng DB delete lỗi, DB còn metadata; retry trash idempotent
giúp hoàn tất DB delete. Không có distributed transaction hoặc đảm bảo rollback
Drive hoàn hảo khi mất kết nối. Timeout sau remote create có thể để lại file/folder
không có ID trong response; log album identity để reconcile thủ công. Phase1 chưa
có durable outbox/reconciliation worker. API POST batch không tự retry để tránh
nhân đôi ảnh; không báo thành công khi cleanup hoặc media verification lỗi.

Mutations giữ PostgreSQL row lock `FOR UPDATE` theo album qua storage/commit.
Các API instance upload/delete cùng album được serialize; test concurrency xác
nhận delete chờ upload và cascade cả photo mới.

## Files

Tạo:

- `Shared/Entities/LibraryAlbum.cs`, `LibraryPhoto.cs`.
- `Features/Library/Controllers/LibraryController.cs`.
- `Features/Library/DTOs/{CreateAlbumDto,UpdateAlbumDto,AlbumDto,AlbumDetailDto,PhotoDto}.cs`.
- `Features/Library/Services/{ILibraryService,LibraryService,ILibraryStorageService,GoogleDriveLibraryStorageService,LibraryUploadRules}.cs`.
- Migration AddLibraryModule + Designer.
- `Tests/LibraryFlowsTests.cs`, `GoogleDriveLibraryStorageServiceTests.cs`, `apps-script-library.test.cjs`.
- Báo cáo này.

Sửa:

- `Shared/Data/AppDbContext.cs`, `DatabaseStartup.cs`, `Migrations/AppDbContextModelSnapshot.cs`.
- `Program.cs` (Library DI/client).
- `GOOGLE_APPS_SCRIPT_AVATAR.gs` (Library actions/helper, giữ avatar).
- `Tests/SystemFlowsTests.cs` (test-only mock Library DI), `Tests/Backend-MyLife.Tests.csproj` (copy image fixtures).
- `API_CONTRACT.md`, `Tests/README.md`.

Factory design-time dùng để EF scaffold không khởi động app/DB thật đã được
remove sau khi tạo migration; không để lại override connection thiết kế.
Không thay avatar tests hoặc source frontend/mobile.

## Build và tests

Kết quả cuối:

- Backend clean/restore/build Release: pass, build 0 warning/error.
- Backend tests: **47/47 pass**, gồm Library, avatar/auth/family-tree và legacy migration regressions.
- Apps Script Node: **21/21 pass** = 12 avatar + 9 Library.
- `git diff --check`: pass.
- Snapshot không có pending model changes; migration đã apply/test trên PostgreSQL tạm.

Commands:

```powershell
dotnet clean -c Release -v quiet
dotnet restore Tests/Backend-MyLife.Tests.csproj
dotnet build MyLife.csproj -c Release --no-restore
dotnet test Tests/Backend-MyLife.Tests.csproj -c Release --no-restore
node --test Tests/apps-script-avatar.test.cjs Tests/apps-script-library.test.cjs
```

Integration dùng cluster PostgreSQL tạm riêng127.0.0.1:55439/mylife_validation,
không dùng connection DB ứng dụng. Tests bao gồm schema snapshot, migration,
CRUD/single/batch, lỗi storage/DB, cleanup failure, cover/cascade, ownership,
validation và concurrency. Apps Script test dùng Drive giả lập có persisted
bytes/MIME/size và signed bytes; chưa test Library trên Drive thật vì root/property
và deployment mới cần setup. Không cần build frontend hoặc chạy Flutter.

## Manual setup và deployment — chưa thực hiện online

1. Google Drive: tạo folder riêng **MyLife_Library** ngoài Account_Avatars.
2. Copy ID từ URL folder.
3. Apps Script → Project Settings → Script Properties:
   thêm **LIBRARY_FOLDER_ID=<folder id>**. Giữ **AVATAR_FOLDER_ID** hiện tại.
4. Copy toàn bộ **GOOGLE_APPS_SCRIPT_AVATAR.gs** vào **Code.gs** → Save.
   Giữ Advanced Drive API **v2**, identifier **Drive**, runtime **V8**.
5. **Deploy → Manage deployments → Edit → New version → Deploy**.
   Giữ deployment URL để backend reuse WebAppUrl.
6. GET WebAppUrl: marker avatar giữ nguyên; thêm
   `libraryContractVersion: "library-phase1-v1"`.
7. Chạy backend mới; startup hiện có apply migration AddLibraryModule. Chưa sửa
   DB ứng dụng hoặc deploy code từ lượt làm việc này.

## Manual API test sau setup

Trong Postman/Swagger, dùng Bearer access token hợp lệ; với Web cookie dùng thêm
`X-Requested-With: MyLife` theo API contract hiện có.

1. POST `/api/library/albums`:

   ```json
   { "name": "Gia đình 2026", "description": "Ảnh gia đình năm 2026" }
   ```

   Kỳ vọng201, DB1album với folder ID, Drive `MyLife_Library/album_<id>`.
2. POST `/api/library/albums/<id>/photos`, multipart3 field cùng tên **files**:
   A.jpg, B.png, C.webp. Kỳ vọng201/3photoDTO; DB3rows, Drive3file `photo_<uuid>`.
3. GET list/detail: photoCount3, cover null, filename gốc, URL ổn định, MIME/size
   chính xác. Download file Drive và đối chiếu ảnh, không chỉ thumbnail.
4. PUT name/description: Drive folder name và ID không đổi.
5. Dùng user khác: không thấy album ở list, GET/PUT/UPLOAD/DELETE trả404.
6. DELETE mộtphoto: Drive trash đúngfile trước, DB còn2rows.
7. DELETE album: Drive albumfolder trash, DBalbum và photos không còn.
8. Test lỗi storage trên account test: API failure, DB giữ record khi delete
   fail; batch lỗi giữa chừng không thêmrows, các file mới được cleanup/log.
9. Regression avatar hiện có: uploadA → B → C giữID, delete bình thường;
   Google login không overwrite MANUAL avatar.

Chưa tạo MyLife_Library thật, chưa thêm Script Property, chưa redeploy Apps Script,
chưa chạy API Library trên DB/Drive ứng dụng; không commit/push.
