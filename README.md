# Backend - MyLife Web API

Dự án Web API được xây dựng bằng ASP.NET Core (.NET 10), cung cấp các dịch vụ xác thực JWT Bearer, quản lý phiên và API cho ứng dụng MyLife.

---

## 🛠️ Công nghệ sử dụng
- **Framework:** ASP.NET Core (.NET 10)
- **Authentication:** JWT Bearer (AccessToken & RefreshToken qua HttpOnly Cookie / Header)
- **API Documentation:** Swagger / OpenAPI (Swashbuckle)
- **CORS:** Cấu hình sẵn sàng kết nối với Frontend React (port 7000, 5173)

---

## 🚀 Hướng dẫn khởi chạy

### 1. Khởi chạy ứng dụng
```bash
dotnet run
```
Ứng dụng sẽ chạy tại:
- **HTTP:** `http://localhost:5274`
- **HTTPS:** `https://localhost:7274`

### 2. Truy cập tài liệu Swagger UI
Mở trình duyệt và truy cập:
- `http://localhost:5274/swagger` hoặc `https://localhost:7274/swagger`

---

## 🔑 Các Endpoint chính
- `POST /api/login` - Đăng nhập tài khoản, nhận AccessToken & RefreshToken
- `GET /api/me` - Lấy thông tin tài khoản hiện tại (Yêu cầu JWT Bearer / Cookie)
- `POST /api/refresh-token` - Gia hạn AccessToken bằng RefreshToken
- `POST /api/logout` - Đăng xuất và xóa cookies