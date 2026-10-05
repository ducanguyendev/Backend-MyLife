# Backend - MyLife Web API

Dự án Web API được xây dựng bằng ASP.NET Core (.NET 10), thiết kế theo kiến trúc Vertical Slice Architecture. Hệ thống cung cấp các dịch vụ xác thực JWT Bearer, quản lý phiên, tích hợp Google Drive và các RESTful API bảo mật cho ứng dụng MyLife.

---

## ⚙️ Các API & Tính năng chính

### Xác thực & Tài khoản (Auth APIs)
* `POST /api/login`: Đăng nhập, cấp JWT (AccessToken & RefreshToken qua HttpOnly Cookie).
* `POST /api/auth/google`: Web gửi Authorization Code + redirect URI; Mobile gửi Google ID Token. Backend luôn verify token bằng thư viện Google chính thức.
* `POST /api/auth/register`: Đăng ký tài khoản.
* `GET /api/me` | `PUT /api/me/profile`: Xem & Cập nhật thông tin cá nhân.
* `POST /api/auth/change-password`: Đổi mật khẩu.
* `POST /api/refresh-token`: Tái cấp phát token.
* `POST /api/logout`: Revoke refresh token trong database và xóa cookie.

### Quản lý Gia phả (Family Tree APIs)
* `GET /api/family-tree`: Lấy danh sách thành viên.
* `GET /api/family-tree/generations`: Lấy danh sách thế hệ.
* `GET /api/family-tree/{id}`: Chi tiết thành viên.
* `POST /api/family-tree` | `PUT /api/family-tree/{id}` | `DELETE /api/family-tree/{id}`: CRUD phả hệ cho user đã đăng nhập.
* `/api/admin/family-tree/*`: Alias tương thích chỉ dành cho role `ADMIN`.

### Quản trị viên (Admin APIs)
* `GET /api/admin/stats`: Thống kê tổng quan.
* `GET /api/admin/users`: Truy xuất danh sách User.
* `PUT /api/admin/users/{id}/status`: Khóa / Mở khóa tài khoản.
* `PUT /api/admin/users/{id}/role`: Đổi role `ADMIN` / `USER`.
* `DELETE /api/admin/users/{id}`: Xóa tài khoản.
* `GET /api/admin/logs`: Xem lịch sử log hệ thống.

### Tích hợp Lưu trữ Ảnh (Avatar APIs)
* `GET /api/avatar/{email}`: Lấy ảnh đại diện.
* `POST /api/avatar/upload`: Upload ảnh trực tiếp lên Google Drive qua API.
* `DELETE /api/avatar`: Xóa ảnh đại diện.

---

## 🛠️ Công nghệ sử dụng
- **Framework:** ASP.NET Core (.NET 10)
- **Kiến trúc:** Vertical Slice Architecture
- **Database & ORM:** PostgreSQL, Entity Framework Core
- **Authentication:** JWT Bearer (AccessToken & RefreshToken qua HttpOnly Cookie / Header)
- **Tích hợp ngoài:** Google Drive API (lưu trữ ảnh đại diện), Google OAuth 2.0
- **API Documentation:** Swagger / OpenAPI (Swashbuckle)
- **CORS:** Chỉ cho phép các origin chính xác được cấu hình; không dùng wildcard cùng credentials.

---

## 🚀 Hướng dẫn khởi chạy

### 1. Cài đặt & Cấu hình
1. Copy cấu trúc từ `appsettings.example.json`, nhưng đặt secret bằng environment variables hoặc .NET User Secrets; không commit `appsettings.json`.
2. Cấu hình `ConnectionStrings__DefaultConnection`.
3. Cấu hình `JwtSettings__SecretKey` (ít nhất 32 ký tự), `Issuer`, `Audience`, `AccessTokenSeconds`, `RefreshTokenMinutes`.
4. Cấu hình Google OAuth bằng `Authentication__Google__ClientId` và, cho Web Authorization Code Flow, `Authentication__Google__ClientSecret`.
5. Seed admin là tùy chọn qua `SeedAdmin__Email` và `SeedAdmin__Password` (tối thiểu 12 ký tự). Thiếu cấu hình thì không tạo admin mặc định; user đã tồn tại không bị tự động đổi password hoặc thăng quyền.

Web dùng cookie HttpOnly và phải gửi `credentials: include` cùng header `X-Requested-With: MyLife` cho mutation. Mobile gửi Bearer token, `X-Client-Platform: mobile` và nhận token + thời gian hết hạn từ response.

### Nâng cấp database cũ

Database mới được tạo bằng EF Core migrations. Với database cũ từng tạo bằng `EnsureCreated`, hãy backup trước, sau đó bật `Database__AllowLegacyBaseline=true` cho lần khởi động nâng cấp đầu tiên. Backend kiểm tra đầy đủ cột, type, unique key và foreign key trước khi ghi migration baseline; schema không khớp sẽ làm startup thất bại an toàn. Migration không xóa user và chuyển refresh token raw cũ sang SHA-256 hash. Rollback migration không thể khôi phục raw refresh token hoặc hoàn tác chuẩn hóa role.

Contract đầy đủ nằm trong `API_CONTRACT.md`.

### 2. Khởi chạy ứng dụng
```bash
dotnet run
```
> **Lưu ý:** Backend sẽ tự động kiểm tra, migrate Database và seed dữ liệu khởi tạo.

Ứng dụng sẽ chạy tại:
- **HTTP:** `http://localhost:5274`
- **HTTPS:** `https://localhost:7274`

### 3. Truy cập tài liệu Swagger UI
Mở trình duyệt và truy cập để test trực tiếp các APIs:
- `http://localhost:5274/swagger` hoặc `https://localhost:7274/swagger`
