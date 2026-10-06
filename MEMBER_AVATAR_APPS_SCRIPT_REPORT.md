# Account Avatar + FamilyMember Avatar + Library — 06/10/2026

## Audit Backend trước khi chọn contract

Đã kiểm tra `Features/FamilyTree`, entity/DTO/service/controller, migrations,
`Program.cs`, storage clients và tests trên branch `feature_minhduc`.

Workspace hiện chưa có implementation Phase 2 member-avatar storage. Entity
`FamilyMember` chỉ có `AvatarUrl`; CRUD nhận URL, chưa có Drive file ID riêng,
endpoint upload/delete avatar thành viên hay action payload member-avatar.
Account Avatar và Library đã có contracts độc lập và được giữ lại.

Theo fallback yêu cầu, chuẩn hóa hai action mới:

- `member_avatar_upload`
- `member_avatar_delete`

Thêm client backend nhỏ `IGoogleDriveMemberAvatarService` /
`GoogleDriveMemberAvatarService` trong `Features/FamilyTree/Services`, DI scoped
và named HTTP client timeout45s. Client dùng đúng action/property names của Apps
Script, reuse `GoogleDrive:WebAppUrl`. Không thay account/library clients,
appsettings, FamilyTree CRUD/UI/schema hoặc tạo migration Phase 2.

Đây là contract storage sẵn dùng; chưa phải chức năng upload member end-to-end.
HTTP/UI Phase 2 sau này cần authorize member, gọi client và lưu file ID trả về.

## Source cuối cùng

Toàn bộ source deploy nằm trong **GOOGLE_APPS_SCRIPT_AVATAR.gs**. `doPost` giữ
account `upload`/`delete`, bốn action Library và thêm hai action member.

| Nhóm | Script Property | Identity |
| --- | --- | --- |
| Account Avatar | `AVATAR_FOLDER_ID` | `avatar_<normalized email>` |
| FamilyMember Avatar | `MEMBER_AVATAR_FOLDER_ID` | `member_avatar_<memberId>` |
| Library | `LIBRARY_FOLDER_ID` | `album_<id>/photo_<unique>.<ext>` |

Member root bắt buộc có, không trashed, khác account và Library. Library root
được bổ sung kiểm tra khác member root. Các ID được trim trước so sánh.
Account upload/delete và các helper account giữ nguyên thân hàm. Library actions,
payloads, filename và media verification giữ nguyên; chỉ bổ sung root separation.

Member filename không phụ thuộc tên người, email hay filename upload. Member ID
là positive integer; decimal string giữ chính xác ID lớn, unsafe JS number bị
từ chối để tránh identity bị làm tròn.

## Upload/delete contract và hành vi

```json
{ "action": "member_avatar_upload", "memberId": 15, "fileBase64": "...",
  "contentType": "image/jpeg", "existingFileId": "X" }
```

Upload lần đầu gửi null/không có existingFileId: tìm stable filename cũ trong
member folder; nếu có thì overwrite, nếu không thì create. Gửi X: tìm đúng X trong
member folder; missing/outside/trashed trả failure, không fallback/create.

Overwrite tiếp tục dùng Drive Advanced Service **v2**:

```javascript
Drive.Files.update(
  { title: stableName, mimeType: contentType },
  currentFileId, blob, { supportsAllDrives: true }
);
```

Các lần A → B → C → D → E giữ X, ghi bytes/MIME mới kể cả chọn lại ảnh giống nhau.
JPEG/PNG/WebP/GIF, JPG alias normalize JPEG, max5MiB. Member upload dùng signature
helper Library hiện có; không thêm signature requirement vào account contract.

Sau ghi, `Drive.Files.get` xác nhận ID, MD5, MIME và size so với bytes request;
sharing ANYONE_WITH_LINK/VIEW và duplicate cleanup thành công mới trả success.
URL ổn định `https://lh3.googleusercontent.com/d/X`, không thêm cache timestamp.
Response thêm fileSize để client backend đối chiếu cả checksum/MIME/size.

Failed create được best-effort trash, trả failure và known ID/cleanup status nếu
có. Failed overwrite không trash file đã có và không tạo file thay thế; backend
client không trả URL success hoặc ID overwrite để compensation. Nếu Drive đã
thực sự ghi bytes trước một bước verification/sharing thất bại, bytes có thể đã
đổi dù response failure; không khẳng định rollback media hoàn hảo. Retry dùng X.
Transport timeout có thể để lại write chưa biết ID, giống giới hạn storage cũ.

```json
{ "action": "member_avatar_delete", "memberId": 15, "fileId": "X" }
```

Delete ưu tiên X, xác nhận parent member folder trước trash; X đã trashed được
retry idempotent. Missing/outside X failure, không cleanup fallback. Thành công
cleanup duplicate cùng stable name, chỉ trong member folder. Không có fileId thì
cleanup stable-name legacy; không có ảnh trả success/deletedfalse.

## Diagnostic

`doGet` giữ legacy `contractVersion`, thêm alias avatar và member marker:

```json
{
  "success": true,
  "service": "mylife-avatar-storage",
  "contractVersion": "drive-v2-verified-overwrite-v2",
  "avatarContractVersion": "drive-v2-verified-overwrite-v2",
  "memberAvatarContractVersion": "member-avatar-v1-drive-v2-verified-overwrite",
  "libraryContractVersion": "library-phase1-v1"
}
```

Không expose Script Property, folder ID, secret hoặc credential.

## Files và validation

Sửa `GOOGLE_APPS_SCRIPT_AVATAR.gs`, `Program.cs`, `API_CONTRACT.md`,
`Tests/README.md`. Thêm member storage client, `Tests/GoogleDriveMemberAvatarServiceTests.cs`,
`Tests/apps-script-member-avatar.test.cjs` và báo cáo này. Không rewrite source
Account/Library hoặc sửa migrations/UI; thay đổi Library từ lượt trước vẫn còn
uncommitted trong workspace.

| Check | Kết quả |
| --- | --- |
| Apps Script full source, mocked Drive | **42/42 pass**: 12 account + 9 Library + 21 member |
| C# GoogleDrive client tests, mocked HTTP | **55/55 pass**, gồm member/account/Library |
| Backend Release build | Pass, 0 warnings/errors |
| Git diff whitespace check | Pass |

Tests member xác nhận first create, 100 overwrites giữ ID, bytes/MIME persisted,
JPG alias/GIF, signature/size, primary ID và renamed ID, legacy duplicates,
root separation, missing/trashed IDs, bốn field metadata verification, no-op/
failed updates, sharing/cleanup failures, scoped delete/retry và dispatcher ba nhóm.
Backend tests kiểm tra literal action names, payload/member ID, đổi định dạng,
checksum/MIME/size, canonical URL, changed identity, malformed/old response,
delete confirmation và transport/config failures.

Đây là kiểm thử storage contracts với Drive/HTTP giả lập. Chưa chạy live member
upload trên Drive, chưa deploy Apps Script. Không chạy lại full HTTP/PostgreSQL
suite vì không đổi HTTP endpoints/schema/DB behavior; filter C# không cần DB.

## Manual setup/deploy tiếp theo

1. Tạo Drive folder riêng, ví dụ **MyLife_Member_Avatars**; lấy folder ID.
2. Apps Script → Project Settings → Script Properties: thêm
   `MEMBER_AVATAR_FOLDER_ID=<new folder id>`. Giữ `AVATAR_FOLDER_ID` và
   `LIBRARY_FOLDER_ID`; bảo đảm ba giá trị khác nhau.
3. Copy **toàn bộ GOOGLE_APPS_SCRIPT_AVATAR.gs** vào Code.gs, Save. Giữ Advanced
   Drive API **v2**, identifier **Drive**, runtime **V8**.
4. Deploy → Manage deployments → Edit → New version → Deploy; giữ deployment
   URL hiện có để reuse backend WebAppUrl.
5. GET WebAppUrl: kiểm tra đủ markers như trên. Test riêng member15 trong folder
   mới: first upload → X, upload B/C/D/E với existingFileIdX → vẫn X, download
   original media để so bytes/MD5; deleteX và xác nhận account/Library không đổi.
6. Khi triển khai HTTP/UI Phase 2, lưu stable Drive file ID sau verified success,
   gửi ID này cho lần upload/delete sau; authorise member phía backend. Script
   không tự xác thực quyền chỉnh một member qua user identity.

Không tự tạo folder/Script Property thật, deploy, commit/push hoặc sửa DB ứng dụng.
