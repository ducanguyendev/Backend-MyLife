-- =========================================================================
-- DATABASE INITIALIZATION SCRIPT FOR POSTGRESQL (MyLife DB)
-- =========================================================================

-- Bật extension pgcrypto để sinh UUID ngẫu nhiên nếu chưa có
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- 1. BẢNG ROLES (Nhóm quyền)
CREATE TABLE IF NOT EXISTS roles (
    id SERIAL PRIMARY KEY,
    name VARCHAR(50) UNIQUE NOT NULL,
    description TEXT NULL
);

-- 2. BẢNG USERS (Tài khoản người dùng)
CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    avatar_url TEXT NULL,
    auth_provider INT NOT NULL DEFAULT 0, -- 0 = LOCAL (Email+Password), 1 = GOOGLE (Google OAuth)
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    verified_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Tạo Index tìm kiếm nhanh theo Email
CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);

-- 3. BẢNG USER_ROLES (Phân quyền người dùng)
CREATE TABLE IF NOT EXISTS user_roles (
    user_id UUID NOT NULL,
    role_id INT NOT NULL,
    PRIMARY KEY (user_id, role_id),
    CONSTRAINT fk_user_roles_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    CONSTRAINT fk_user_roles_role FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE CASCADE
);

-- 4. BẢNG REFRESH_TOKENS (Quản lý phiên đăng nhập)
CREATE TABLE IF NOT EXISTS refresh_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,
    token TEXT UNIQUE NOT NULL,
    ip_address VARCHAR(45) NULL,
    user_agent TEXT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    is_revoked BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_refresh_tokens_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

-- Tạo Index cho refresh_tokens
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_token ON refresh_tokens(token);
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user_id ON refresh_tokens(user_id);
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_expires_at ON refresh_tokens(expires_at);

-- 5. BẢNG LOGIN_LOGS (Lịch sử đăng nhập)
CREATE TABLE IF NOT EXISTS login_logs (
    id BIGSERIAL PRIMARY KEY,
    user_id UUID NULL,
    attempt_email VARCHAR(255) NOT NULL,
    status VARCHAR(20) NOT NULL CHECK (status IN ('SUCCESS', 'FAILED')),
    ip_address VARCHAR(45) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_login_logs_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_login_logs_attempt_email ON login_logs(attempt_email);
CREATE INDEX IF NOT EXISTS idx_login_logs_created_at ON login_logs(created_at);

-- =========================================================================
-- SEED DATA (Dữ liệu mẫu khởi tạo)
-- =========================================================================

-- Thêm các vai trò chuẩn
INSERT INTO roles (id, name, description)
VALUES 
    (1, 'ADMIN', 'Quản trị viên hệ thống'),
    (2, 'USER', 'Người dùng tiêu chuẩn')
ON CONFLICT (id) DO NOTHING;

-- Thêm tài khoản admin@gmail.com (Password: Admin@123 - BCrypt hash)
INSERT INTO users (id, email, password_hash, is_active, verified_at, created_at, updated_at)
VALUES 
    ('a0000000-0000-0000-0000-000000000001', 'admin@gmail.com', '$2a$11$MnpqTBBcfbwis0W2oQOsUuL7Rcv2iJLLbKU/UJN.c/HObC4HV.yHe', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (email) DO UPDATE SET password_hash = '$2a$11$MnpqTBBcfbwis0W2oQOsUuL7Rcv2iJLLbKU/UJN.c/HObC4HV.yHe';

-- Gán quyền ADMIN cho admin@gmail.com
INSERT INTO user_roles (user_id, role_id)
VALUES ('a0000000-0000-0000-0000-000000000001', 1)
ON CONFLICT (user_id, role_id) DO NOTHING;


select * from users
select * from login_logs
select * from refresh_tokens
select * from roles

-- Xóa tài khoản google.user@gmail.com và các token/log liên quan
DELETE FROM users WHERE email IN ('google.user@gmail.com', 'google.developer@gmail.com');

-- Bổ sung cột avatar_url vào bảng users
ALTER TABLE users ADD COLUMN IF NOT EXISTS avatar_url TEXT;