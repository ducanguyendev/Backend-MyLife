# Avatar đổi liên tiếp — xác minh ngày 06/10/2026

## 1. Kết luận về nguyên nhân

Bạn xác nhận đã deploy bản Apps Script trước. GET read-only tới WebAppUrl trong
appsettings trả marker `drive-v2-verified-overwrite`, đúng bản trước.
Sau đó đã chạy **A → B → C → D → E trên chính Apps Script/Drive thật**, gồm
JPEG → WebP → PNG → WebP → JPEG. Tất cả thành công; sau mỗi lần đã download
media gốc qua Drive và so MD5 với bytes gửi lên. Không chỉ kiểm tra URL/thumbnail.

Vì vậy, **không tái hiện được lỗi storage chỉ overwrite một lần** ở deployment
này. Source cũng không có counter, return sớm khi file tồn tại hay nhánh bỏ
`existingFileId` từ lần C. Chưa có log lỗi từ tài khoản đang gặp vấn đề nên không
kết luận nguyên nhân của mọi trường hợp ảnh cũ là cache hoặc Apps Script lỗi.

Các lỗi UI xác định qua audit và đã sửa:

- Header dùng trực tiếp `user.avatar`; AuthContext trước đây nạp lại URL ổn định
  không có version sau login/reload. Có thể lấy ảnh cache cũ dù DB/Drive đúng.
- Effect mở modal phụ thuộc toàn bộ `user`; mỗi avatarUpdated thay user object,
  khiến effect reset preview, tab, thông báo upload và form đang mở.
- Timestamp chỉ dùng Date.now có thể trùng trong cùng millisecond. Nay version
  tăng đơn điệu, tránh reuse URL khi upload nhanh hoặc upload lại cùng ảnh.
- File input trước đây reset khi bắt đầu chọn, nay reset thêm trong finally;
  có ref khóa upload và disabled input trong lúc gửi. Lỗi rollback rõ ràng về
  URL thành công trước đó, không dùng local preview để báo thành công.

## 2. existingFileId và DB

Không tìm thấy chỗ làm mất ID. Mỗi request controller query user từ DB và chụp
`existingFileId = user.AvatarDriveFileId`; storage gửi field đó ở mọi lần.
Controller kiểm tra result ID phải bằng ID cũ khi đang overwrite, ngoài kiểm tra
checksum/MIME vốn có trong storage service. ID chỉ được clear sau delete thành công.
Không yêu cầu frontend truyền ID, không cần thêm field ID vào auth DTO.

DB vẫn giữ:

```text
AvatarDriveFileId = X
AvatarUrl = https://lh3.googleusercontent.com/d/X
AvatarSource = MANUAL
```

Không lưu t/v vào DB. Login chỉ đọc lại URL từ DB; frontend tạo display version
mới. Tests Google sync v1 → v2 → v3 → v4 → v5 giữ X, gửi bytes mới mỗi lần.
Google login sau MANUAL không overwrite. Không sửa JWT/refresh/BCrypt/linking,
role, seed, family tree, appsettings, entity/migration hoặc mobile.

## 3. File sửa trong lượt này

Backend:

- `Features/Avatar/Controllers/AvatarController.cs`: log request/result/persist,
  snapshot ID và kiểm tra identity ở controller.
- `Features/Avatar/Services/GoogleDriveAvatarService.cs`: log storage rejection,
  bỏ log raw response để tránh lộ dữ liệu nhạy cảm.
- `GOOGLE_APPS_SCRIPT_AVATAR.gs`: log Updating/Verified mỗi lần và marker v2.
- `Tests/AvatarFlowsTests.cs`: 100 upload qua API, DB checks, bytes/MIME persisted
  trong storage giả lập, login/logout, lỗi rồi retry, Google sync nhiều lần.
- `Tests/GoogleDriveAvatarServiceTests.cs`: 100 payload kiểm tra ID, bytes, MIME.
- `Tests/apps-script-avatar.test.cjs`: 100 overwrite + chọn lại cùng ảnh + retry.
- `Tests/verify-live-avatar.cjs` và `Tests/fixtures/avatar-replacements.json`:
  regression optional trên Drive thật bằng ảnh JPEG/WebP/PNG hợp lệ.
- `Tests/README.md`, báo cáo trước và báo cáo này.

Frontend:

- `src/features/auth/services/authService.ts`: version tăng đơn điệu và cache bust
  URL ổn định khi nạp lại, giữ version khi rerender thông thường.
- `src/features/auth/context/AuthContext.tsx`: hydrate avatar versioned cho Header,
  login và /api/me sau refresh.
- `src/features/profile/components/UserProfileModal.tsx`: không reset modal khi
  avatar event thay user; reset input finally, khóa request và rollback.
- `tests/avatar.test.cjs`: 100 success/version, AuthContext login/reload, actual
  profile handler chọn lại cùng file 5 lần và failure rollback.

Giữ mọi thay đổi có sẵn của bạn ngoài phạm vi trên.

## 4. Backend lần 2/3/4/5 gửi gì

Ở test API PostgreSQL riêng, lần A gửi existingFileId=null; B/C/D/E và tất cả
các lần tới 100 gửi X từ DB, DB luôn giữ X. Bytes và contentType được đối chiếu
ở từng request và dữ liệu storage sau update; không chỉ assert URL.
Logout/login ở lần 5 và 100, /api/me đọc đúng URL X và storage vẫn có ảnh cuối.
Upload thất bại giữ ID/bytes trước, retry tiếp thành công cùng ID.

Test service riêng kiểm tra 100 JSON payload; test source Apps Script kiểm tra
1 create + 99 update, tất cả update gọi ID X với bytes/MIME mới.
Chọn lại ảnh cuối vẫn gọi thêm update, không return sớm.

## 5. Kết quả Drive thật A/B/C/D/E

| Lần | MIME | Bytes gửi/tải về | File ID | Kiểm chứng |
|---|---|---:|---|---|
| A | image/jpeg | 767 | 13BMm6yAsHF7Iyhw0BK_Pbpv_vOwDTHfX | MD5 metadata và media download khớp |
| B | image/webp | 562 | 13BMm6yAsHF7Iyhw0BK_Pbpv_vOwDTHfX | MD5 metadata và media download khớp |
| C | image/png | 134 | 13BMm6yAsHF7Iyhw0BK_Pbpv_vOwDTHfX | MD5 metadata và media download khớp |
| D | image/webp | 566 | 13BMm6yAsHF7Iyhw0BK_Pbpv_vOwDTHfX | MD5 metadata và media download khớp |
| E | image/jpeg | 765 | 13BMm6yAsHF7Iyhw0BK_Pbpv_vOwDTHfX | MD5 metadata và media download khớp |

Phép thử dùng email validation UUID riêng, không dùng user/avatar của bạn.
1 lần create và 4 lần overwrite cùng ID; sau đó delete qua Apps Script trả
success=true/deleted=true và **file test đã được trash**. Sau cleanup không còn
avatar test hoạt động. Không dùng API listing toàn folder để kiểm kê các file
của account thật; không sửa row account thật trong DB.

100 upload là regression giả lập/storage + API với PostgreSQL test; 5 upload
Drive thật là phép thử có media download xác minh. Không nói đã upload 100 lần
lên Drive thật hay đã kiểm tra logout/login trên account thật.

## 6. Logging chẩn đoán

Backend log:

```text
Avatar upload requested UserId=... ExistingFileId=X ContentType=... FileSize=...
Avatar upload result UserId=... ExistingFileId=X ResultFileId=X Success=True ...
Avatar upload persisted UserId=... AvatarDriveFileId=X
```

Apps Script log sau deploy source mới:

```text
existingFileId: X
resolvedFileId: X
Updating avatar file X
Verified avatar file X checksum=... bytes=...
```

Không log base64, WebAppUrl hoặc secret. Nếu B → C thất bại trên account thật,
đối chiếu POST /api/avatar/upload response và Apps Script Executions của đúng
request với các log trên để xác định update, verify, DB persist hay UI lỗi.

## 7. Build/test

- `dotnet clean -c Release -v quiet`: pass.
- `dotnet restore Tests/Backend-MyLife.Tests.csproj`: pass.
- `dotnet build MyLife.csproj -c Release --no-restore`: pass, 0 warning/error.
- `dotnet test Tests/Backend-MyLife.Tests.csproj -c Release --no-restore`:
  **25/25 pass**, lần cuối không warning.
- `node --test Tests/apps-script-avatar.test.cjs`: **12/12 pass**.
- `npm.cmd run build`: pass; chỉ cảnh báo bundle lớn.
- `npm.cmd run lint`: pass.
- `npm.cmd run test:avatar`: **12/12 pass**.
- `node Tests/verify-live-avatar.cjs`: **5/5 upload + original media download
  verification pass**, cleanup pass.

Backend integration dùng PostgreSQL tạm riêng ở 127.0.0.1:55439, không dùng DB
ứng dụng. Sau tests đã stop cluster tạm. Frontend test thực thi handler/state
bằng hook harness, không phải thao tác file picker thủ công trong browser.
Không sửa mobile, không chạy Flutter. Không dừng/restart backend đang chạy.

## 8. Redeploy

Apps Script đổi log và marker, cần redeploy để nhận phần chẩn đoán mới:

```text
Copy GOOGLE_APPS_SCRIPT_AVATAR.gs → Code.gs
→ Save → Deploy → Manage deployments → Edit → New version → Deploy
```

Giữ Advanced Drive v2, identifier Drive, runtime V8, AVATAR_FOLDER_ID và URL
deployment. Marker doGet mới: `drive-v2-verified-overwrite-v2`.
**Chưa deploy source mới trong lượt này**; live test dùng deployment trước
đã xác nhận marker `drive-v2-verified-overwrite` và overwrite nhiều lần thành công.
Restart backend và dùng frontend build mới để nhận UI/logging đã sửa.
