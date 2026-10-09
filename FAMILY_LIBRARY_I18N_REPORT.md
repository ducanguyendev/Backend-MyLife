# FamilyTree / Library notifications — 06/10/2026

## 1. Những chỗ trước đây dùng backend message hoặc sai locale

Audit các pattern `showToast`, `showNotification`, `data.message`,
`getApiErrorMessage`, `setError`, `throw new Error`, `alert`, success/failed trong
FamilyTreeManager, FamilyTreePage, LibraryTab và các modal liên quan.

- Xóa FamilyMember ưu tiên `data.message`, làm backend success
  **Family member deleted.** xuất hiện ngay cả khi app đang VI.
- Lưu FamilyMember hiển thị trực tiếp `data.message` hoặc nối toàn bộ
  `data.errors`; validation/relationship errors có thể là tiếng Anh.
- Library loading, album save, photo save/upload/delete và album delete dùng
  `getApiErrorMessage`, có thể đưa raw backend error hoặc default English ra UI.
- Success Library đã dùng i18n nhưng dùng chung saved/deleted/uploaded,
  chưa phân biệt album với ảnh hoặc create với update.
- MemberForm thiếu locale keys cho role/URL nên fallback VI ngay cả khi EN;
  `validateDob` được gọi không truyền translations và dùng default VI.
- Nút đóng của NotificationProvider hardcode **Đóng**.

Không đổi diagnostic logs/console/Apps Script. Các dữ liệu như tên thành viên,
tên album, metadata người dùng nhập không bị dịch.

## 2. Files locale VI/EN và frontend đã sửa

Locale sửa: `src/shared/locales/vi/admin.json`, `src/shared/locales/en/admin.json`.
Reuse các keys `common.registerPage.errors.*` và `dobValidation.*` hiện có,
không tạo một hệ i18n khác.

Frontend thêm `src/features/admin/services/featureMessages.ts` để tập trung
success keys và code/status/action → error key. Không parse chuỗi backend.
`ApiErrorPayload` bổ sung optional `code`; apiClient và contract token/cookie
giữ nguyên.

Sửa `FamilyTreeManager`, `LibraryTab`, `LibraryAlbumModal`, `LibraryUploadModal`,
`LibraryPhotoEditModal`, `MemberFormModal`, `NotificationContext`, `package.json`,
tests/browser harness và `tests/LIBRARY_README.md`.

## 3. FamilyTree success và validation

| Action | Key | VI |
| --- | --- | --- |
| Create | `admin.member_create_success` | Thêm thành viên thành công. |
| Update | `admin.member_update_success` | Cập nhật thành viên thành công. |
| Delete | `admin.member_delete_success` | Đã xóa thành viên {{name}} thành công. |

EN tương ứng: Member added successfully.; Member updated successfully.;
Member {{name}} was deleted successfully.

Frontend chỉ dùng kết quả HTTP success để chọn thông báo i18n, không đọc backend
success message. Tên dùng interpolation, giữ nguyên tên người. Backend success
message vẫn có cho các client khác.

MemberForm lưu error keys rồi dịch lúc render để đổi VI/EN khi lỗi còn hiển thị.
Name/phone/role/URL, generation và date validation dùng locale hiện tại.
DOB helper được truyền keys, không dùng default VI; ngày không tồn tại/ngoài
khoảng tuổi dùng thông báo ngày sinh không hợp lệ, không để lộ chuỗi helper.
Nút đóng notification dùng `admin.close` ở cả hai ngôn ngữ.

## 4. Library toast và form errors

Keys success tách từng action:

- `album_created`, `album_updated`, `album_deleted`.
- `photo_uploaded`, `photo_updated`, `photo_deleted`.
- `category_created`, `category_deleted` (chuẩn bị translations).

Các key trên nằm trong `admin.library_ui`, đủ bản dịch VI/EN. Success chỉ hiện
sau API thành công. Các lỗi modal/loading/delete lưu key và render bằng `t(key)`,
không lưu raw message; khi đổi locale, lỗi đang hiển thị cũng đổi ngôn ngữ.

Upload validation phân biệt chưa chọn album, chưa chọn ảnh, hơn20 ảnh, file
không có dữ liệu/độ lớn hơn5MiB và sai MIME. Album/modal upload dùng `noValidate`
để custom validation i18n không bị thông báo required theo ngôn ngữ trình duyệt
ghi đè. Giữ maxLength và backend validation.

**Category audit:** Library hiện chỉ có enum photos/decrees/events/temple;
không có entity/API/modal create/delete category. Không tự thêm category CRUD
trong lượt localization. Category success/name-required và known-error translations
đã có, được test VI/EN; đây là kiểm tra catalog, không phải test thao tác category
end-to-end. Album/photo flows được kiểm thử bằng callbacks thực của component.

## 5. Backend error codes đã thêm

Responses lỗi nghiệp vụ thêm `code`, vẫn giữ message và HTTP status cũ.
`FamilyTreeValidationException` mang code, vẫn là ArgumentException. Các rule,
transactions và service behavior hiện có được giữ nguyên.

| FamilyTree | Ý nghĩa |
| --- | --- |
| `FAMILY_MEMBER_NOT_FOUND` | Không tìm thấy member GET/PUT/DELETE |
| `FAMILY_RELATED_MEMBER_NOT_FOUND` | Related member không tồn tại |
| `FAMILY_SELF_RELATION` | Tự liên kết |
| `FAMILY_PARENTS_MUST_DIFFER` | Cha/mẹ trùng ID |
| `FAMILY_SPOUSE_IN_USE` | Spouse đã được liên kết |
| `FAMILY_RELATIONSHIP_INVALID` | Relationship type không hợp lệ |
| `FAMILY_RELATIONSHIP_CYCLE` | Chu trình cha mẹ/con |
| `FAMILY_VALIDATION_FAILED` | Argument validation khác |

| Library | Ý nghĩa |
| --- | --- |
| `LIBRARY_NOT_FOUND` | Missing/inaccessible album hoặc photo, giữ404 |
| `LIBRARY_INVALID_IMAGE` | MIME/signature/extension/filename không hợp lệ |
| `LIBRARY_IMAGE_SIZE_INVALID` | File rỗng hoặc quá lớn |
| `LIBRARY_FILE_COUNT_INVALID` | Số lượng files ngoài1..20 |
| `LIBRARY_METADATA_INVALID` | Photo metadata/category không hợp lệ |
| `LIBRARY_VALIDATION_FAILED` | Album/validation khác |
| `LIBRARY_STORAGE_FAILED` | Storage verification/delete/create fail |
| `LIBRARY_SAVE_FAILED` | Persistence failure |
| `LIBRARY_ALBUM_UNAVAILABLE` | Album thiếu storage folder |
| `LIBRARY_CLEANUP_FAILED` | Operation fail và compensation chưa đầy đủ |
| `AUTH_SESSION_INVALID` | Library active-user session không hợp lệ |

Library failure wrapper giữ code gốc, ưu tiên cleanup code nếu compensation
không hoàn tất. Frontend giữ thông báo cleanup riêng để không làm mất thông tin
về ảnh chưa dọn được. Storage/save code được dịch theo action đang thực hiện.

Automatic model validation có thể trả ProblemDetails/errors không có code;
frontend dùng status400/422 và action để dịch generic validation, không đọc
English title/errors. 401/403/409/413 và network status0 có keys riêng. Backend
cũ chưa có code cũng được xử lý bằng status/action. Unknown errors dùng localized
fallback, không fallback sang raw backend message.

Frontend cũng chuẩn bị mappings `LIBRARY_CATEGORY_IN_USE` và
`LIBRARY_CATEGORY_CANNOT_DELETE`; backend hiện chưa phát các codes này vì không
có category CRUD. Không đổi status semantics, storage contract, migration hoặc
localize ILogger/Apps Script logs.

## 6. Raw backend message còn hiển thị không?

Trong notification, modal error và validation của **FamilyTree + Library**:
không còn ưu tiên/hiển thị raw backend message. Search lại không còn
`data.message`/`getApiErrorMessage` ở các component này. API response/logs vẫn
giữ diagnostic text; frontend chỉ hiển thị keys.

Phạm vi audit theo yêu cầu là FamilyTree/Library và modal của chúng.
AdminDashboard user-management và Auth/Profile là các feature khác, vẫn có một
số xử lý backend message hiện có; lượt này không khẳng định localization toàn
bộ mọi feature của ứng dụng.

## 7. Regression results

| Check | Result |
| --- | --- |
| `npm.cmd run test:i18n` | **11/11 pass** |
| Frontend Library tests | **3/3 pass** |
| Frontend avatar tests | **12/12 pass** |
| Frontend build / lint | Pass; Vite còn cảnh báo chunk >500kB |
| Backend Release build | Pass, 0 warning/error |
| Full backend suite, isolated PostgreSQL | **72/72 pass** |
| Apps Script regressions | **42/42 pass**, account/member/Library |
| Edge headless Library UI, mocked API | Pass: CRUD/upload/filter/lightbox/error/retry/empty/mobile |
| `git diff --check`, hai repo | Pass |

Test i18n chạy actual FamilyTreeManager/LibraryTab callbacks và modal validation
với React/API boundary giả lập, nhưng real i18next và catalogs VI/EN. Bắt buộc
regression backend `{success:true,message:"Family member deleted."}` xác nhận
toast VI **Đã xóa thành viên Nguyễn Văn An thành công.**, EN **Member Nguyễn Văn
An was deleted successfully.**, không hiển thị backend success text.

Library tests kiểm tra actual callbacks create/update/delete album, upload/edit/
delete photo ở cả VI/EN, catalog category keys, known code ưu tiên hơn message,
old response chỉ status, unknown-code fallback, locale change khi form error
còn mở. Member tests kiểm tra validation name/date ở EN và đổi lỗi đang hiển thị.
Browser harness xác nhận raw **Drive delete failed** không xuất hiện trong modal.
Backend integration kiểm tra codes cho self relation, related/member not found,
Library ownership404 và storage502; avatar/Library regressions vẫn pass.

Không ghi DB ứng dụng hoặc Drive thật; tests dùng PostgreSQL riêng và mocked
remote storage. Không thêm migration, deploy, commit/push hoặc sửa Apps Script.

## Manual

Restart backend để các code mới có hiệu lực, chạy frontend mới và kiểm tra VI/EN
trong FamilyTree: create/update/delete member; Library album/photo actions và
validation/error retry. Có thể chạy `npm.cmd run test:i18n` để kiểm tra nhanh.
Không cần redeploy Apps Script hoặc apply migration cho thay đổi localization.
  