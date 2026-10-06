# Sửa lỗi overwrite avatar MyLife — 06/10/2026

Lượt xác minh tiếp theo đã kiểm tra chuỗi 100 upload và 5 upload trên Drive thật.
Xem kết quả mới tại [AVATAR_REPEAT_UPLOAD_REPORT.md](AVATAR_REPEAT_UPLOAD_REPORT.md).

## 1. Nguyên nhân và phạm vi xác minh

Trong source Apps Script tại thời điểm bắt đầu, `Drive.Files.update` dùng
`{ name: stableName, mimeType: contentType }`, không đúng metadata Drive API **v2**
mà project đang sử dụng. v2 dùng **title**, còn name thuộc v3.
Source cũng chưa kiểm tra bytes sau update, cho phép fallback sang legacy/create
khi ID tường minh không tìm được, và không dọn duplicate khi tìm được file bằng ID.
Backend chỉ kiểm tra có file ID, có thể nhận response thiếu xác nhận thành công
hoặc ID khác. UI chưa áp dụng timestamp được truyền vào cho URL lh3 trực tiếp.

Đây là các lỗi xác định được trong source. Chưa truy cập Apps Script đang deploy,
execution log, bytes Drive thật hoặc dữ liệu DB của tài khoản trong ảnh, nên chưa
kết luận deployment đang chạy chính xác bản source nào hay Drive thumbnail có cache.

Tài liệu chính thức: [Drive API v2 files.update](https://developers.google.com/workspace/drive/api/reference/rest/v2/files/update)
và [Drive API v2 File resource](https://developers.google.com/workspace/drive/api/reference/rest/v2/files).

## 2. Các file sửa trong lượt này

Backend:

- `GOOGLE_APPS_SCRIPT_AVATAR.gs`
- `Features/Avatar/Services/GoogleDriveAvatarService.cs`
- `Features/Avatar/Controllers/AvatarController.cs`
- `Tests/GoogleDriveAvatarServiceTests.cs`
- `Tests/AvatarFlowsTests.cs`
- `Tests/apps-script-avatar.test.cjs` (mới)
- `Tests/README.md`
- `AVATAR_FIX_REPORT.md` (báo cáo mới)

Frontend:

- `src/features/auth/services/authService.ts`
- `src/shared/utils/compressAvatarImage.ts`
- `tests/avatar.test.cjs` (mới)
- `package.json` (thêm `test:avatar`)

Giữ các thay đổi có sẵn của bạn. Backend và frontend đang ở `feature_minhduc`;
repository bao ngoài ở `master`. Đã audit entity, mapping, migration, Program,
cấu hình GoogleDrive, Google sync, AuthContext, profile modal, apiClient và mobile.
Không sửa appsettings/secret, auth không liên quan, migration hoặc mobile;
không dùng database ứng dụng để chạy test.

## 3. existingFileId được truyền ở đâu

Trong working tree ban đầu, ID **không bị mất**: controller đã lấy
`user.AvatarDriveFileId`; cả overload IFormFile và byte[] của storage đã truyền
`existingFileId` vào JSON. Google avatar sync cũng truyền ID này.
Entity/mapping và migration `20261005045704_AddAvatarDriveTracking` đã có đủ field;
không tạo migration mới.

Luồng hiện tại:

```text
File chọn → compression → POST /api/avatar/upload
→ current user từ DB → AvatarDriveFileId
→ GoogleDriveAvatarService → JSON existingFileId + bytes + MIME
→ Apps Script kiểm tra file thuộc avatar folder
→ Drive.Files.update với title, fileId, blob, supportsAllDrives:true
→ Drive.Files.get kiểm tra ID + MD5 + fileSize + MIME
→ dọn duplicate → trả cùng ID + checksum + contentType
→ backend đối chiếu checksum/MIME/ID → lưu DB
→ frontend cập nhật AuthContext qua auth:avatarUpdated + cache bust
```

## 4. Overwrite và xác nhận bytes

Lệnh update dùng metadata v2 `title`, lấy `currentFileId` trước update,
truyền blob chứa bytes mới làm đối số thứ ba, và đọc lại chính ID đó.
Apps Script đối chiếu MD5, kích thước và MIME của media Drive với upload.
Response có thêm `md5Checksum` và `contentType`; backend đối chiếu với bytes đã gửi.
Không còn xem một URL/file ID đơn thuần là bằng chứng upload thành công.
MD5 ở đây dùng để kiểm tra nội dung media, không dùng cho mật khẩu hay auth.

Update lỗi, checksum sai, MIME sai, ID đổi hoặc response thiếu proof đều trả failure.
Backend không thay row avatar; frontend không dispatch avatarUpdated khi thất bại.
Apps Script log existingFileId, resolvedFileId, stableName, contentType, byte count
và stack lỗi. Deployment cũ thiếu checksum sẽ bị backend từ chối, cần deploy script
mới trước khi dùng backend mới.

## 5. File ID và duplicate

Test upload A → upload B, JPEG → WebP xác nhận cùng ID **X** và persisted bytes B.
Không create file khác khi có ID tường minh. Nếu ID không truy cập được, nằm ngoài
folder hoặc đã trashed, upload báo lỗi để giữ nguyên identity.
Chỉ lookup legacy khi không có ID; chỉ create khi cả ID lẫn legacy đều không có.
Duplicate theo stable name/các đuôi legacy được trash sau overwrite và verification
thành công, kể cả khi file đích tìm bằng ID. Script lock giữ các request tuần tự.
Delete ưu tiên ID, trash legacy duplicate, và backend chỉ clear DB sau xác nhận xóa.

Các test dùng Drive giả lập; chưa xác nhận số file hoặc bytes trên Drive thật sau deploy.

## 6. Giá trị DB

Test PostgreSQL xác nhận:

```text
AvatarDriveFileId = X               // upload A và B giữ nguyên
AvatarUrl = https://lh3.googleusercontent.com/d/X
AvatarSource = MANUAL
GoogleAvatarSourceUrl = null
```

DB không lưu `?t=` hay `?v=`. Khi lỗi upload/delete, các giá trị này giữ nguyên.
Delete thành công clear cả URL, ID, source và Google source URL.
Không đọc/sửa row thật của `admin@gmail.com` trong lượt này.

## 7. Cache và compression phía frontend

Backend trả URL hiển thị với timestamp milliseconds. Frontend dùng URLSearchParams
để thay `t`, tránh nối nhiều query, rồi dispatch URL mới vào AuthContext sau success.
URL lh3 trực tiếp đã nhận cache bust; route redirect `/api/avatar/{email}?t=...`
cũng chuyển tiếp timestamp tới URL Drive và trả Cache-Control:no-store.
Profile modal chỉ đổi preview sau upload success; lỗi không thay avatar cũ.

Giữ max dimension 1024 và chất lượng WebP 0.82; animated GIF giữ nguyên.
Nếu browser encode fallback PNG, File dùng đúng blob.type và đuôi .png;
không còn gắn image/webp lên bytes PNG. Downscale không bị bỏ chỉ vì ảnh output lớn hơn.

Google login vẫn sync khi phù hợp, local login không tự sync, MANUAL không bị Google
ghi đè; các regression test này đều pass.

## 8. Build và tests

- `dotnet clean`: hoàn tất, cảnh báo file Debug đang bị tiến trình backend khóa.
- `dotnet restore Tests/Backend-MyLife.Tests.csproj`: pass.
- `dotnet build MyLife.csproj -c Release --no-restore`: pass, 0 warning/error.
- `dotnet test Tests/Backend-MyLife.Tests.csproj -c Release --no-restore`: 23/23 pass.
- `node --test Tests/apps-script-avatar.test.cjs`: 10/10 pass, thực thi source .gs
  qua Node V8, Drive giả lập có lưu và kiểm tra bytes/MIME/ID.
- `npm.cmd install --no-audit --no-fund`: pass.
- `npm.cmd run build`: pass; Vite có cảnh báo bundle lớn.
- `npm.cmd run lint`: pass.
- `npm.cmd run test:avatar`: 8/8 pass.

Test backend dùng cluster PostgreSQL tạm riêng ở 127.0.0.1:55439, database
`mylife_validation`, không dùng connection ứng dụng. Restore/test đã chạy ngoài
sandbox vì quyền đọc NuGet.Config/Windows Event Log; không sửa các cấu hình này.
Dùng Release để không dừng tiến trình backend hiện đang giữ file Debug.

Không sửa mobile nên không chạy Flutter. Chạy source qua Node V8 không thay thế
kiểm tra manifest/runtime của deployment Apps Script thật.

## 9. Manual deployment bắt buộc — CHƯA DEPLOY

1. Mở Apps Script project tương ứng WebAppUrl hiện tại.
2. Copy toàn bộ `GOOGLE_APPS_SCRIPT_AVATAR.gs` vào `Code.gs` → **Save**.
3. Bật hiển thị manifest `appsscript.json`; giữ cấu hình hiện có và bảo đảm
   `runtimeVersion` là `V8`, Advanced Drive service identifier `Drive`, version `v2`:

   ```json
   {
     "runtimeVersion": "V8",
     "dependencies": {
       "enabledAdvancedServices": [
         { "userSymbol": "Drive", "version": "v2", "serviceId": "drive" }
       ]
     }
   }
   ```

   Đây là phần cấu hình cần merge, không thay toàn bộ manifest hoặc service khác.
   Giữ nguyên script property `AVATAR_FOLDER_ID` và quyền deploy hiện có.
4. **Deploy → Manage deployments → Edit → New version → Deploy**.
   Cập nhật deployment hiện tại để giữ WebAppUrl; không cần sửa appsettings.
5. Mở WebAppUrl `/exec`: doGet của bản mới trả
   `contractVersion: "drive-v2-verified-overwrite"`.
6. Khởi động lại backend bằng source mới và dùng frontend build mới.
7. Upload A rồi B. Kiểm tra DB giữ X; Drive chỉ còn một avatar hoạt động.
   Download media thật từ file X sau mỗi lần, so sánh nội dung/checksum với file đã
   upload **sau compression**; không chỉ nhìn thumbnail Drive hoặc URL.
   Test tiếp JPEG → WebP, failure, delete và Google login sau avatar MANUAL.

Chưa deploy, chưa restart backend của bạn và chưa chạy upload trên Drive thật.
