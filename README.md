# Backend - MyLife Web API

Dự án Web API được xây dựng bằng ASP.NET Core (.NET 10), thiết kế theo kiến trúc Vertical Slice Architecture. Hệ thống cung cấp các dịch vụ xác thực JWT Bearer, quản lý phiên, tích hợp Google Drive và các RESTful API bảo mật cho ứng dụng MyLife.

---

## ⚙️ Các API & Tính năng chính

### Xác thực & Tài khoản (Auth APIs)
* `POST /api/login`: Đăng nhập, cấp JWT (AccessToken & RefreshToken qua HttpOnly Cookie).
* `POST /api/auth/google`: Đăng nhập bằng Google OAuth.
* `POST /api/auth/register`: Đăng ký tài khoản.
* `GET /api/me` | `PUT /api/me/profile`: Xem & Cập nhật thông tin cá nhân.
* `POST /api/auth/change-password`: Đổi mật khẩu.
* `POST /api/refresh-token`: Tái cấp phát token.
* `POST /api/logout`: Đăng xuất, hủy cookie.

### Quản lý Gia phả (Family Tree APIs)
* `GET /api/familytree`: Lấy danh sách thành viên.
* `GET /api/familytree/generations`: Lấy cây gia phả (tự động thuật toán phân cấp thế hệ).
* `GET /api/familytree/{id}`: Chi tiết thành viên.
* `POST /api/familytree` | `PUT /api/familytree/{id}` | `DELETE /api/familytree/{id}`: Quản lý CRUD phả hệ.

### Quản trị viên (Admin APIs)
* `GET /api/admin/stats`: Thống kê tổng quan.
* `GET /api/admin/users`: Truy xuất danh sách User.
* `PUT /api/admin/users/{id}/toggle-active`: Khóa / Mở khóa tài khoản.
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
- **CORS:** Cấu hình sẵn sàng kết nối với Frontend React (port 7000, 5173)

---

## 🚀 Hướng dẫn khởi chạy

### 1. Cài đặt & Cấu hình
1. Mở file `appsettings.json` hoặc `appsettings.Development.json`.
2. Cập nhật chuỗi kết nối **PostgreSQL** tại `ConnectionStrings:DefaultConnection`.
3. Cấu hình các thông số JWT Secret, Issuer, Audience.
4. Cấu hình thông tin tích hợp Google Drive API nếu có.

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