# MyLife Backend

Backend REST API cho hệ thống **MyLife**, được xây dựng bằng ASP.NET Core .NET 10, PostgreSQL và Entity Framework Core.

Hệ thống cung cấp:

- Authentication
- JWT Access / Refresh Token
- Google Login
- User Profile
- Avatar
- Family Tree
- Family Member Avatar
- Family Library
- Album / Photo
- Library Category
- Google Drive storage
- Admin Dashboard
- User Management
- Health Check
- Swagger API Documentation

---

# Công nghệ sử dụng

- ASP.NET Core .NET 10
- Entity Framework Core 10
- PostgreSQL
- Npgsql
- JWT Bearer Authentication
- BCrypt
- Google OAuth
- Google Drive
- Google Apps Script
- Swagger / OpenAPI

---

# 1. Yêu cầu môi trường

Cài trước:

- Git
- .NET SDK 10
- PostgreSQL
- Node.js (nếu chạy Apps Script tests)
- EF Core CLI nếu muốn chạy migration bằng command line

Kiểm tra:

```powershell
dotnet --version
git --version
psql --version
node --version
```

Expected .NET:

```text
10.x
```

---

# 2. Clone project

```powershell
git clone https://github.com/ducanguyendev/Backend-MyLife.git
cd Backend-MyLife
```

Nếu cần branch hiện tại:

```powershell
git checkout feature_minhduc
```

---

# 3. Restore packages

```powershell
dotnet restore
```

Build thử:

```powershell
dotnet build
```

---

# 4. Tạo PostgreSQL Database

Đảm bảo PostgreSQL đang chạy.

Có thể tạo database bằng pgAdmin hoặc `psql`.

Ví dụ:

```sql
CREATE DATABASE mylife_db;
```

Database mặc định của project:

```text
mylife_db
```

Port PostgreSQL mặc định:

```text
5432
```

---

# 5. Tạo appsettings.json

Project có file mẫu:

```text
appsettings.example.json
```

Copy thành:

```powershell
Copy-Item appsettings.example.json appsettings.json
```

`appsettings.json` đã được `.gitignore`, vì vậy không commit file chứa password / secret.

---

# 6. Cấu hình Database

Khuyến nghị sử dụng .NET User Secrets.

Project đã có `UserSecretsId`.

Thiết lập connection string:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=mylife_db;Username=postgres;Password=YOUR_POSTGRES_PASSWORD"
```

Ví dụ:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=mylife_db;Username=postgres;Password=123456"
```

Không sử dụng password ví dụ trên trong production.

---

# 7. Cấu hình JWT

JWT secret bắt buộc phải dài ít nhất 32 ký tự.

Ví dụ:

```powershell
dotnet user-secrets set "JwtSettings:SecretKey" "MYLIFE_LOCAL_DEVELOPMENT_SECRET_KEY_123456789"
```

Các giá trị còn lại có thể giữ theo `appsettings.example.json`:

```json
"JwtSettings": {
  "Issuer": "MyLife",
  "Audience": "MyLifeClients",
  "AccessTokenSeconds": 3600,
  "RefreshTokenMinutes": 10080
}
```

---

# 8. Cấu hình CORS

Frontend mặc định chạy:

```text
http://localhost:7000
```

Backend cần:

```json
"Cors": {
  "AllowedOrigins": [
    "http://localhost:7000"
  ]
}
```

Không thêm dấu `/` ở cuối origin.

Đúng:

```text
http://localhost:7000
```

Không nên dùng:

```text
http://localhost:7000/
```

---

# 9. Cập nhật Database bằng Migration

Project sử dụng:

```text
EF Core Code First + Migration
```

Nếu chưa có `dotnet-ef`:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.11
```

Nếu đã cài:

```powershell
dotnet tool update --global dotnet-ef --version 10.0.11
```

Kiểm tra:

```powershell
dotnet ef --version
```

Sau đó:

```powershell
dotnet ef database update
```

Lệnh này sẽ tạo/cập nhật các bảng cần thiết trong PostgreSQL.

Ví dụ:

```text
users
roles
user_roles
refresh_tokens
family_trees
family_members
family_relationships
library_albums
library_photos
library_categories
...
```

Không tự sửa các file migration cũ.

---

# 10. Database Auto Migrate

File mẫu hiện có:

```json
"Database": {
  "AllowLegacyBaseline": false,
  "AutoMigrate": false
}
```

Với máy developer mới, cách an toàn là giữ:

```text
AutoMigrate = false
```

và chạy migration thủ công:

```powershell
dotnet ef database update
```

Nếu muốn local development tự apply migration khi startup, có thể sử dụng:

```powershell
dotnet user-secrets set "Database:AutoMigrate" "true"
```

Không khuyến nghị bật AutoMigrate tùy tiện trên production.

---

# 11. Database cũ không có Migration History

Nếu database cũ từng được tạo bằng `EnsureCreated` và không có:

```text
__EFMigrationsHistory
```

Backend có cơ chế legacy baseline.

Trước tiên:

1. Backup database.
2. Xác nhận schema đúng.
3. Tạm bật:

```powershell
dotnet user-secrets set "Database:AllowLegacyBaseline" "true"
```

Chạy Backend một lần.

Sau khi hoàn tất nên tắt lại:

```powershell
dotnet user-secrets set "Database:AllowLegacyBaseline" "false"
```

Không sử dụng tùy chọn này cho database mới.

---

# 12. Cấu hình Google Login

Nếu muốn sử dụng Google Login:

```powershell
dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_GOOGLE_CLIENT_ID"
dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_GOOGLE_CLIENT_SECRET"
```

Allowed Client ID:

```powershell
dotnet user-secrets set "Authentication:Google:AllowedClientIds:0" "YOUR_GOOGLE_CLIENT_ID"
```

Nếu chưa cấu hình Google OAuth, đăng nhập Google sẽ không hoạt động nhưng local username/password có thể vẫn sử dụng được.

---

# 13. Seed Admin

Seed Admin là tùy chọn.

Ví dụ:

```powershell
dotnet user-secrets set "SeedAdmin:Email" "admin@gmail.com"
dotnet user-secrets set "SeedAdmin:Password" "AdminPassword@123"
```

Password admin phải đáp ứng validation của hệ thống.

Nếu không cấu hình:

```text
SeedAdmin
```

backend sẽ không tự tạo admin mặc định.

---

# 14. Google Drive / Google Apps Script

Các chức năng sau sử dụng Google Drive:

- Account Avatar
- Family Member Avatar
- Library Album
- Library Photo

Backend giao tiếp với Google Drive thông qua Google Apps Script Web App.

Script nằm tại:

```text
GOOGLE_APPS_SCRIPT_AVATAR.gs
```

---

# 15. Chuẩn bị Google Drive

Tạo 3 folder riêng trong Google Drive:

```text
MyLife_Avatars
MyLife_Member_Avatars
MyLife_Library
```

Ba folder phải khác nhau.

Lấy Folder ID từ URL Google Drive.

Ví dụ:

```text
https://drive.google.com/drive/folders/ABC123XYZ
```

Folder ID:

```text
ABC123XYZ
```

---

# 16. Cấu hình Apps Script

Tạo Google Apps Script project.

Copy nội dung:

```text
GOOGLE_APPS_SCRIPT_AVATAR.gs
```

vào Apps Script.

Trong:

```text
Project Settings
→ Script Properties
```

tạo:

```text
AVATAR_FOLDER_ID
MEMBER_AVATAR_FOLDER_ID
LIBRARY_FOLDER_ID
```

Ví dụ:

```text
AVATAR_FOLDER_ID=...
MEMBER_AVATAR_FOLDER_ID=...
LIBRARY_FOLDER_ID=...
```

Ba ID phải khác nhau.

---

# 17. Enable Advanced Drive Service

Trong Apps Script:

```text
Services
→ Add a service
→ Drive API
```

Chọn:

```text
Drive API v2
```

Identifier:

```text
Drive
```

Runtime:

```text
V8
```

---

# 18. Deploy Apps Script

Trong Apps Script:

```text
Deploy
→ New deployment
→ Web app
```

Cấu hình:

```text
Execute as:
Me

Who has access:
the option appropriate for your backend deployment
```

Sau khi deploy sẽ có URL dạng:

```text
https://script.google.com/macros/s/xxxxxxxxxxxx/exec
```

Test bằng trình duyệt.

Expected response tương tự:

```json
{
  "success": true,
  "service": "mylife-avatar-storage"
}
```

---

# 19. Cấu hình Apps Script URL cho Backend

```powershell
dotnet user-secrets set "GoogleDrive:WebAppUrl" "https://script.google.com/macros/s/YOUR_DEPLOYMENT_ID/exec"
```

Không commit Web App URL hoặc credentials nếu deployment của bạn cần được bảo mật.

Sau mỗi lần thay đổi Apps Script cần:

```text
Deploy
→ Manage deployments
→ Edit
→ New version
→ Deploy
```

Chỉ bấm Save trong Apps Script chưa chắc cập nhật deployment đang chạy.

---

# 20. Chạy Backend

```powershell
dotnet run
```

Backend mặc định chạy:

```text
HTTP  : http://localhost:5274
HTTPS : https://localhost:7274
```

---

# 21. Swagger

Development mode hỗ trợ Swagger.

Mở:

```text
http://localhost:5274/swagger
```

---

# 22. Health Check

Live:

```text
http://localhost:5274/health/live
```

Ready:

```text
http://localhost:5274/health/ready
```

`ready` kiểm tra kết nối PostgreSQL.

---

# 23. Kiểm tra Database

Sau khi Backend chạy, kiểm tra PostgreSQL:

```sql
SELECT * FROM "__EFMigrationsHistory";
```

Kiểm tra user:

```sql
SELECT * FROM users;
```

Album:

```sql
SELECT * FROM library_albums;
```

Photo:

```sql
SELECT * FROM library_photos;
```

Category:

```sql
SELECT * FROM library_categories;
```

---

# 24. Quan hệ Library

Cấu trúc chính:

```text
users
  │
  │ 1:N
  ▼
library_albums
  │
  │ 1:N
  ▼
library_photos
```

Album lưu:

```text
id
name
description
drive_folder_id
cover_photo_id
created_by_user_id
created_at
updated_at
```

`created_by_user_id` là Foreign Key tới:

```text
users.id
```

File ảnh thật nằm trên Google Drive.

PostgreSQL lưu metadata và Drive File ID.

---

# 25. Authentication Web

Browser sử dụng HttpOnly Cookie.

Các mutation request cần:

```text
X-Requested-With: MyLife
```

Frontend đã xử lý việc này thông qua API client.

Không cần tự thêm header nếu gọi API thông qua Frontend hiện tại.

---

# 26. Tests

Backend integration tests sử dụng PostgreSQL riêng.

KHÔNG dùng database development hoặc production để chạy test.

Tạo database test riêng, ví dụ:

```sql
CREATE DATABASE mylife_validation;
```

PowerShell:

```powershell
$env:MYLIFE_TEST_DATABASE = "Host=127.0.0.1;Port=5432;Database=mylife_validation;Username=postgres;Password=YOUR_PASSWORD"
```

Sau đó:

```powershell
dotnet test Tests/Backend-MyLife.Tests.csproj -c Release
```

Apps Script tests:

```powershell
node --test Tests/apps-script-avatar.test.cjs
node --test Tests/apps-script-member-avatar.test.cjs
node --test Tests/apps-script-library.test.cjs
```

Hoặc:

```powershell
node --test Tests/apps-script-avatar.test.cjs Tests/apps-script-member-avatar.test.cjs Tests/apps-script-library.test.cjs
```

Các test trên sử dụng mock Drive và không ghi vào Google Drive thật.

---

# 27. Thứ tự chạy project từ máy mới

## Terminal 1 — Backend

```powershell
cd Backend-MyLife

dotnet restore

dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=mylife_db;Username=postgres;Password=YOUR_PASSWORD"

dotnet user-secrets set "JwtSettings:SecretKey" "MYLIFE_LOCAL_DEVELOPMENT_SECRET_KEY_123456789"

dotnet ef database update

dotnet run
```

Expected:

```text
http://localhost:5274
```

## Terminal 2 — Frontend

```powershell
cd Frontend-MyLife

npm install

npm run dev
```

Expected:

```text
http://localhost:7000
```

---

# 28. Lỗi thường gặp

## `JwtSettings:SecretKey must be supplied`

Chưa cấu hình JWT secret:

```powershell
dotnet user-secrets set "JwtSettings:SecretKey" "MYLIFE_LOCAL_DEVELOPMENT_SECRET_KEY_123456789"
```

---

## `Connection refused` PostgreSQL

Kiểm tra PostgreSQL service đang chạy.

Windows:

```powershell
Get-Service *postgres*
```

Kiểm tra port:

```powershell
netstat -ano | findstr :5432
```

---

## `Pending database migrations`

Chạy:

```powershell
dotnet ef database update
```

---

## Frontend báo lỗi API

Kiểm tra Backend:

```text
http://localhost:5274/health/live
```

---

## Avatar / Library upload lỗi 502

Kiểm tra:

```powershell
dotnet user-secrets list
```

Phải có:

```text
GoogleDrive:WebAppUrl
```

Sau đó kiểm tra Apps Script deployment có phải version mới nhất hay không.

---

## Library ảnh không hiển thị

Kiểm tra request:

```text
GET /api/library/photos/{id}/image
```

Nếu trả `502`, kiểm tra Apps Script:

```text
library_read_photo
```

đã được deploy chưa.

---

## Google Login không hoạt động

Kiểm tra:

```text
Authentication:Google:ClientId
Authentication:Google:ClientSecret
Authentication:Google:AllowedClientIds
```

và cấu hình OAuth trên Google Cloud Console.

---

# 29. Không commit secret

Không commit:

```text
appsettings.json
password PostgreSQL
JWT SecretKey
Google ClientSecret
Google credentials
production URLs/secrets
```

Sử dụng:

```text
dotnet user-secrets
```

cho local development.

---

# 30. Kiến trúc lưu trữ

MyLife sử dụng mô hình:

```text
PostgreSQL
    ↓
metadata / relationships / users
    +
Google Drive
    ↓
image files
```

Backend:

```text
ASP.NET Core
    ↓
Entity Framework Core
    ↓
PostgreSQL
```

Media:

```text
Frontend
    ↓
Backend
    ↓
Google Apps Script
    ↓
Google Drive
```

---

# 31. Port mặc định

| Service | Port |
|---|---:|
| PostgreSQL | 5432 |
| Backend HTTP | 5274 |
| Backend HTTPS | 7274 |
| Frontend | 7000 |

---

# 32. Quick Start

Sau khi PostgreSQL và secrets đã được cấu hình:

Backend:

```powershell
dotnet restore
dotnet ef database update
dotnet run
```

Frontend:

```powershell
npm install
npm run dev
```

Truy cập:

```text
http://localhost:7000
```

Swagger:

```text
http://localhost:5274/swagger
```