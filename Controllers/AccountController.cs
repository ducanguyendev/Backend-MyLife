using Microsoft.AspNetCore.Mvc;
using LoginApp.Data;
using LoginApp.Entities;
using LoginApp.Model;
using LoginApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LoginApp.Controllers
{
    [ApiController]
    [Route("api/")]
    public class AccountController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ITokenService _tokenService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public AccountController(
            AppDbContext db,
            ITokenService tokenService,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _db = db;
            _tokenService = tokenService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = Request.Headers.UserAgent.ToString();

                // 1. Email: Bắt buộc, trim khoảng trắng và chuyển lowercase
                var normalizedEmail = (model.Email ?? string.Empty).Trim().ToLowerInvariant();

                // 2. Password: Bắt buộc, KHÔNG trim, độ dài từ 8 đến 72 ký tự
                var rawPassword = model.Password;

                // 3. Tìm tài khoản trong database PostgreSQL (kèm vai trò Roles)
                var user = await _db.Users
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

                if (user == null)
                {
                    // Ghi nhận log đăng nhập thất bại
                    _db.LoginLogs.Add(new LoginLog
                    {
                        AttemptEmail = normalizedEmail,
                        Status = "FAILED",
                        IpAddress = clientIp,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync();

                    return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác." });
                }

                // Kiểm tra trạng thái tài khoản
                if (!user.IsActive)
                {
                    _db.LoginLogs.Add(new LoginLog
                    {
                        UserId = user.Id,
                        AttemptEmail = normalizedEmail,
                        Status = "FAILED",
                        IpAddress = clientIp,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync();

                    return Unauthorized(new { message = "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên." });
                }

                // 4. Verify password bằng BCrypt
                bool isPasswordValid = BCrypt.Net.BCrypt.Verify(rawPassword, user.PasswordHash);
                if (!isPasswordValid)
                {
                    _db.LoginLogs.Add(new LoginLog
                    {
                        UserId = user.Id,
                        AttemptEmail = normalizedEmail,
                        Status = "FAILED",
                        IpAddress = clientIp,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync();

                    return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác." });
                }

                // 5. Ghi log đăng nhập thành công
                _db.LoginLogs.Add(new LoginLog
                {
                    UserId = user.Id,
                    AttemptEmail = normalizedEmail,
                    Status = "SUCCESS",
                    IpAddress = clientIp,
                    CreatedAt = DateTime.UtcNow
                });

                // 6. Sinh cặp Token mới: Access Token (30s) + Refresh Token (2m)
                var primaryRole = user.UserRoles.FirstOrDefault()?.Role.Name ?? "USER";
                var accessToken = _tokenService.GenerateAccessToken(user.Email, primaryRole);
                var refreshToken = _tokenService.GenerateRefreshToken();

                // Lưu Refresh Token vào bảng refresh_tokens trong PostgreSQL
                var newRefreshToken = new RefreshToken
                {
                    UserId = user.Id,
                    Token = refreshToken,
                    IpAddress = clientIp,
                    UserAgent = userAgent,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(2), // Refresh token có hạn 2 phút
                    IsRevoked = false,
                    CreatedAt = DateTime.UtcNow
                };

                _db.RefreshTokens.Add(newRefreshToken);
                await _db.SaveChangesAsync();

                SetAuthCookies(accessToken, refreshToken, model.RememberMe, 120);

                // Tuyệt đối không trả về password hay password hash
                return Ok(new
                {
                    message = "Đăng nhập thành công!",
                    role = primaryRole,
                    accessToken,
                    refreshToken,
                    expiresIn = 30
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Lỗi xử lý đăng nhập PostgreSQL: {ex.Message}");
                return StatusCode(500, new { message = $"Lỗi kết nối cơ sở dữ liệu PostgreSQL: {ex.Message}" });
            }
        }

        [HttpPost("auth/register")]
        public async Task<IActionResult> Register([FromBody] RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

                // 1. Normalize email: trim + lowercase
                var normalizedEmail = (model.Email ?? string.Empty).Trim().ToLowerInvariant();

                // 2. Kiểm tra confirm password khớp
                if (model.Password != model.ConfirmPassword)
                    return BadRequest(new { message = "Mật khẩu nhập lại không khớp. Vui lòng kiểm tra lại." });

                // 3. Kiểm tra email đã tồn tại chưa
                var exists = await _db.Users.AnyAsync(u => u.Email == normalizedEmail);
                if (exists)
                    return Conflict(new { message = "Email này đã được đăng ký. Vui lòng sử dụng email khác hoặc đăng nhập." });

                // 4. Băm mật khẩu bằng BCrypt (cost factor 11)
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(model.Password, workFactor: 11);

                // 5. Tạo User mới
                var newUser = new User
                {
                    Id = Guid.NewGuid(),
                    Email = normalizedEmail,
                    PasswordHash = passwordHash,
                    AvatarUrl = null,
                    AuthProvider = 0, // 0 = LOCAL
                    IsActive = true,
                    VerifiedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.Users.Add(newUser);

                // 6. Gán vai trò USER mặc định (role_id = 2)
                var userRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "USER");
                if (userRole != null)
                {
                    _db.UserRoles.Add(new UserRole
                    {
                        UserId = newUser.Id,
                        RoleId = userRole.Id
                    });
                }

                // 7. Ghi log vào login_logs
                _db.LoginLogs.Add(new LoginLog
                {
                    UserId = newUser.Id,
                    AttemptEmail = normalizedEmail,
                    Status = "SUCCESS",
                    IpAddress = clientIp,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                Console.WriteLine($"[INFO] Tài khoản mới đã được đăng ký: {normalizedEmail}");

                return StatusCode(201, new
                {
                    message = "Đăng ký tài khoản thành công! Vui lòng đăng nhập để tiếp tục.",
                    email = newUser.Email
                });
            }
            catch (Exception ex)
            {
                var errorDetails = ex.InnerException?.Message ?? ex.Message;
                Console.WriteLine($"[ERROR] Lỗi xử lý đăng ký tài khoản: {errorDetails}");
                return StatusCode(500, new { message = $"Lỗi máy chủ khi đăng ký tài khoản: {errorDetails}" });
            }
        }

        [HttpPost("auth/google")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleAuthRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest(new { message = "Mã xác thực Google (Authorization Code) là bắt buộc." });
            }

            try
            {
                var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = Request.Headers.UserAgent.ToString();

                var googleClientId = _configuration["Authentication:Google:ClientId"];
                var googleClientSecret = _configuration["Authentication:Google:ClientSecret"];

                string? userEmail = null;
                string? userName = null;
                string? userPicture = null;

                // Nếu có cấu hình Client Secret thật từ Google Cloud, tiến hành trao đổi mã thực tế
                if (!string.IsNullOrWhiteSpace(googleClientId) && 
                    !string.IsNullOrWhiteSpace(googleClientSecret) &&
                    !googleClientId.StartsWith("YOUR_GOOGLE_CLIENT_ID"))
                {
                    var httpClient = _httpClientFactory.CreateClient();
                    var tokenParams = new Dictionary<string, string>
                    {
                        { "code", request.Code },
                        { "client_id", googleClientId },
                        { "client_secret", googleClientSecret },
                        { "redirect_uri", request.RedirectUri ?? "postmessage" },
                        { "grant_type", "authorization_code" }
                    };

                    var tokenResponse = await httpClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(tokenParams));
                    if (!tokenResponse.IsSuccessStatusCode)
                    {
                        var errJson = await tokenResponse.Content.ReadAsStringAsync();
                        Console.WriteLine($"[ERROR] Google Token Exchange Failed: {errJson}");
                        return BadRequest(new { message = "Xác thực mã ủy quyền Google không thành công. Vui lòng thử lại!" });
                    }

                    var tokenData = await tokenResponse.Content.ReadFromJsonAsync<GoogleTokenResponse>();
                    if (tokenData == null || string.IsNullOrWhiteSpace(tokenData.AccessToken))
                    {
                        return BadRequest(new { message = "Không nhận được Access Token từ máy chủ Google." });
                    }

                    // Gọi Google UserInfo API
                    var infoReq = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
                    infoReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
                    var infoRes = await httpClient.SendAsync(infoReq);

                    if (!infoRes.IsSuccessStatusCode)
                    {
                        return BadRequest(new { message = "Không thể lấy thông tin hồ sơ từ Google." });
                    }

                    var profile = await infoRes.Content.ReadFromJsonAsync<GoogleUserInfo>();
                    userEmail = profile?.Email;
                    userName = profile?.Name;
                    userPicture = profile?.Picture;
                }
                else
                {
                    // Chế độ phát triển / Nhập Gmail trực tiếp khi chưa điền Google Secret:
                    if (request.Code.Contains("@"))
                    {
                        userEmail = request.Code.Trim().ToLowerInvariant();
                        userName = userEmail.Split('@')[0];
                    }
                    else
                    {
                        return BadRequest(new { message = "Chưa cấu hình Google OAuth Client Secret. Vui lòng cung cấp địa chỉ Gmail hợp lệ hoặc cấu hình Client Secret trong appsettings.json!" });
                    }
                }

                if (string.IsNullOrWhiteSpace(userEmail))
                {
                    return BadRequest(new { message = "Không thể xác định địa chỉ Email từ tài khoản Google." });
                }

                var normalizedEmail = userEmail.Trim().ToLowerInvariant();

                // 3. Tìm hoặc Tạo mới User trong PostgreSQL
                var user = await _db.Users
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

                if (user == null)
                {
                    user = new User
                    {
                        Id = Guid.NewGuid(),
                        Email = normalizedEmail,
                        PasswordHash = "GOOGLE_OAUTH",
                        AvatarUrl = userPicture,
                        AuthProvider = 1, // 1 = GOOGLE
                        IsActive = true,
                        VerifiedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _db.Users.Add(user);

                    // Gán vai trò USER mặc định
                    var userRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "USER");
                    if (userRole != null)
                    {
                        _db.UserRoles.Add(new UserRole
                        {
                            UserId = user.Id,
                            RoleId = userRole.Id
                        });
                    }

                    await _db.SaveChangesAsync();
                }
                else
                {
                    // Cập nhật auth_provider và avatar nếu có avatar mới từ Google
                    user.AuthProvider = 1;
                    if (!string.IsNullOrEmpty(userPicture) && user.AvatarUrl != userPicture)
                    {
                        user.AvatarUrl = userPicture;
                    }
                    user.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }

                if (!user.IsActive)
                {
                    _db.LoginLogs.Add(new LoginLog
                    {
                        UserId = user.Id,
                        AttemptEmail = normalizedEmail,
                        Status = "FAILED",
                        IpAddress = clientIp,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync();

                    return Unauthorized(new { message = "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên." });
                }

                // 4. Ghi log đăng nhập thành công
                _db.LoginLogs.Add(new LoginLog
                {
                    UserId = user.Id,
                    AttemptEmail = normalizedEmail,
                    Status = "SUCCESS",
                    IpAddress = clientIp,
                    CreatedAt = DateTime.UtcNow
                });

                // 5. Cấp cặp Token nội bộ riêng của App: Access Token (30s) + Refresh Token (2m)
                var googleUserRole = user.UserRoles.FirstOrDefault()?.Role.Name ?? "USER";
                var accessToken = _tokenService.GenerateAccessToken(user.Email, googleUserRole);
                var refreshToken = _tokenService.GenerateRefreshToken();

                var newRefreshToken = new RefreshToken
                {
                    UserId = user.Id,
                    Token = refreshToken,
                    IpAddress = clientIp,
                    UserAgent = userAgent,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(2),
                    IsRevoked = false,
                    CreatedAt = DateTime.UtcNow
                };

                _db.RefreshTokens.Add(newRefreshToken);
                await _db.SaveChangesAsync();

                SetAuthCookies(accessToken, refreshToken, true, 120);

                return Ok(new
                {
                    message = "Đăng nhập Google thành công!",
                    email = user.Email,
                    role = googleUserRole,
                    avatarUrl = user.AvatarUrl,
                    accessToken,
                    refreshToken,
                    expiresIn = 30
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Lỗi xử lý đăng nhập Google: {ex.Message}");
                return StatusCode(500, new { message = $"Lỗi máy chủ khi xác thực Google: {ex.Message}" });
            }
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> GetUserInfo()
        {
            var email = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(email))
                return Unauthorized(new { message = "Phiên làm việc không hợp lệ." });

            var user = await _db.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Email == email);

            if (user == null || !user.IsActive)
                return Unauthorized(new { message = "Người dùng không tồn tại hoặc đã bị khóa." });

            var primaryRole = user.UserRoles.FirstOrDefault()?.Role.Name ?? "USER";

            return Ok(new
            {
                message = "Xác thực phiên thành công (200 OK)!",
                id = user.Id,
                email = user.Email,
                name = user.Email.Split('@')[0],
                avatarUrl = user.AvatarUrl,
                authProvider = user.AuthProvider,
                authProviderName = user.AuthProvider == 1 ? "GOOGLE" : "LOCAL",
                role = primaryRole,
                isActive = user.IsActive,
                verifiedAt = user.VerifiedAt,
                isAuthenticated = true,
                checkedAt = DateTime.UtcNow.ToString("HH:mm:ss")
            });
        }

        [Authorize]
        [HttpPost("auth/change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var email = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(email))
                return Unauthorized(new { message = "Phiên làm việc không hợp lệ." });

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null || !user.IsActive)
                return Unauthorized(new { message = "Tài khoản không tồn tại hoặc đã bị khóa." });

            if (user.AuthProvider == 1)
                return BadRequest(new { message = "Tài khoản đăng nhập bằng Google OAuth không sử dụng mật khẩu." });

            // Kiểm tra mật khẩu hiện tại
            var isCurrentPasswordValid = BCrypt.Net.BCrypt.Verify(model.CurrentPassword, user.PasswordHash);
            if (!isCurrentPasswordValid)
                return BadRequest(new { message = "Mật khẩu hiện tại không chính xác." });

            if (model.CurrentPassword == model.NewPassword)
                return BadRequest(new { message = "Mật khẩu mới không được trùng với mật khẩu hiện tại." });

            // Băm mật khẩu mới bằng BCrypt
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword, workFactor: 11);
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new { message = "Đổi mật khẩu thành công!" });
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("AccessToken");
            Response.Cookies.Delete("RefreshToken");
            return Ok(new { message = "Đăng xuất thành công!" });
        }

        /// <summary>
        /// Rotate Token: Cấp mới 1 cặp (Access Token 30s + Refresh Token 2m) khi Access Token hết hạn.
        /// </summary>
        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest? body)
        {
            var oldAccessToken = Request.Cookies["AccessToken"] ?? body?.AccessToken;
            var refreshToken = Request.Cookies["RefreshToken"] ?? body?.RefreshToken;

            if (string.IsNullOrEmpty(oldAccessToken) && Request.Headers.ContainsKey("Authorization"))
            {
                var authHeader = Request.Headers.Authorization.ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    oldAccessToken = authHeader.Substring("Bearer ".Length).Trim();
                }
            }

            if (string.IsNullOrEmpty(oldAccessToken) || string.IsNullOrEmpty(refreshToken))
                return Unauthorized(new { message = "Thiếu Access Token hoặc Refresh Token." });

            var principal = _tokenService.GetPrincipalFromExpiredToken(oldAccessToken);
            var email = principal?.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrEmpty(email))
                return Unauthorized(new { message = "Access Token không hợp lệ." });

            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = Request.Headers.UserAgent.ToString();

            // Tìm Refresh Token trong bảng refresh_tokens của PostgreSQL
            var storedToken = await _db.RefreshTokens
                .Include(rt => rt.User)
                    .ThenInclude(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(x => x.User.Email == email && x.Token == refreshToken);

            // Kiểm tra Refresh Token: Hết hạn (> 2 phút không thao tác/refresh) hoặc đã bị hủy
            if (storedToken == null || storedToken.IsRevoked || storedToken.ExpiresAt <= DateTime.UtcNow)
            {
                if (storedToken != null)
                {
                    storedToken.IsRevoked = true;
                    await _db.SaveChangesAsync();
                }
                return Unauthorized(new { message = "Refresh Token đã hết hạn (2 phút) hoặc không hợp lệ. Vui lòng đăng nhập lại." });
            }

            // Kiểm tra xem tài khoản người dùng có đang bị khóa (is_active = false) không
            if (storedToken.User == null || !storedToken.User.IsActive)
            {
                storedToken.IsRevoked = true;
                await _db.SaveChangesAsync();
                return Unauthorized(new { message = "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên." });
            }

            // Rotate Token: Cấp CẢ CẶP Access mới (30s) + Refresh mới (2 phút)
            var refreshUserRole = storedToken.User.UserRoles.FirstOrDefault()?.Role.Name ?? "USER";
            var newAccessToken = _tokenService.GenerateAccessToken(email, refreshUserRole);
            var newRefreshToken = _tokenService.GenerateRefreshToken();

            // Cập nhật Token mới và gia hạn 2 phút mới vào database (Sliding Window)
            storedToken.Token = newRefreshToken;
            storedToken.ExpiresAt = DateTime.UtcNow.AddMinutes(2); // Cấp mới 2 phút cho Refresh Token
            storedToken.IpAddress = clientIp;
            storedToken.UserAgent = userAgent;

            await _db.SaveChangesAsync();

            SetAuthCookies(newAccessToken, newRefreshToken, true, 120);

            return Ok(new
            {
                message = "Rotate Token thành công (Cấp mới Access 30s + Refresh 2m)!",
                accessToken = newAccessToken,
                refreshToken = newRefreshToken,
                expiresIn = 30
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // ADMIN-ONLY ENDPOINTS  [Authorize(Roles = "ADMIN")]
        // ─────────────────────────────────────────────────────────────────────

        [Authorize(Roles = "ADMIN")]
        [HttpGet("admin/stats")]
        public async Task<IActionResult> AdminGetStats()
        {
            var totalUsers = await _db.Users.CountAsync();
            var totalActive = await _db.Users.CountAsync(u => u.IsActive);
            var totalLocked = await _db.Users.CountAsync(u => !u.IsActive);

            var since7Days = DateTime.UtcNow.AddDays(-7);
            var successLogins = await _db.LoginLogs.CountAsync(l => l.Status == "SUCCESS" && l.CreatedAt >= since7Days);
            var failedLogins = await _db.LoginLogs.CountAsync(l => l.Status == "FAILED" && l.CreatedAt >= since7Days);
            var newRegistrations = await _db.Users.CountAsync(u => u.CreatedAt >= since7Days);

            return Ok(new
            {
                totalUsers,
                totalActive,
                totalLocked,
                last7Days = new
                {
                    successLogins,
                    failedLogins,
                    newRegistrations
                }
            });
        }

        [Authorize(Roles = "ADMIN")]
        [HttpGet("admin/users")]
        public async Task<IActionResult> AdminGetUsers()
        {
            var users = await _db.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new
                {
                    id = u.Id,
                    email = u.Email,
                    role = u.UserRoles.FirstOrDefault() != null ? u.UserRoles.First().Role.Name : "USER",
                    authProvider = u.AuthProvider,
                    authProviderName = u.AuthProvider == 1 ? "GOOGLE" : "LOCAL",
                    isActive = u.IsActive,
                    avatarUrl = u.AvatarUrl,
                    createdAt = u.CreatedAt
                })
                .ToListAsync();

            return Ok(users);
        }

        [Authorize(Roles = "ADMIN")]
        [HttpGet("admin/logs")]
        public async Task<IActionResult> AdminGetLogs()
        {
            var logs = await _db.LoginLogs
                .OrderByDescending(l => l.CreatedAt)
                .Take(50)
                .Select(l => new
                {
                    id = l.Id,
                    attemptEmail = l.AttemptEmail,
                    status = l.Status,
                    ipAddress = l.IpAddress,
                    createdAt = l.CreatedAt
                })
                .ToListAsync();

            return Ok(logs);
        }

        [Authorize(Roles = "ADMIN")]
        [HttpPut("admin/users/{id}/toggle-active")]
        public async Task<IActionResult> AdminToggleUserActive(Guid id)
        {
            var user = await _db.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });

            // Không cho phép khóa tài khoản ADMIN
            var userRole = user.UserRoles.FirstOrDefault()?.Role.Name ?? "USER";
            if (userRole == "ADMIN")
                return BadRequest(new { message = "Không thể khóa tài khoản Admin." });

            user.IsActive = !user.IsActive;
            user.UpdatedAt = DateTime.UtcNow;

            // Nếu bị khóa → thu hồi toàn bộ refresh token
            if (!user.IsActive)
            {
                var tokens = await _db.RefreshTokens
                    .Where(t => t.UserId == user.Id && !t.IsRevoked)
                    .ToListAsync();
                tokens.ForEach(t => t.IsRevoked = true);
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = user.IsActive ? $"Đã mở khóa tài khoản {user.Email}." : $"Đã khóa tài khoản {user.Email}.",
                isActive = user.IsActive
            });
        }

        [Authorize(Roles = "ADMIN")]
        [HttpDelete("admin/users/{id}")]
        public async Task<IActionResult> AdminDeleteUser(Guid id)
        {
            var user = await _db.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });

            // Tuyệt đối không cho phép xóa tài khoản ADMIN
            var userRole = user.UserRoles.FirstOrDefault()?.Role.Name ?? "USER";
            if (userRole == "ADMIN" || user.Email.Equals("admin@gmail.com", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Không thể xóa tài khoản Quản trị viên hệ thống." });

            var userEmail = user.Email;

            // Xóa người dùng trong PostgreSQL (Cascade delete sẽ tự dọn dẹp user_roles, refresh_tokens)
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();

            Console.WriteLine($"[ADMIN] 🗑️ Đã xóa vĩnh viễn tài khoản: {userEmail}");

            return Ok(new
            {
                message = $"Đã xóa vĩnh viễn tài khoản {userEmail} khỏi cơ sở dữ liệu thành công!",
                deletedId = id
            });
        }

        private void SetAuthCookies(string accessToken, string refreshToken, bool rememberMe, int refreshExpiresSeconds = 120)
        {
            Response.Cookies.Append("AccessToken", accessToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddSeconds(30) // Access token cookie: 30s
            });

            var refreshOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddSeconds(refreshExpiresSeconds) // Refresh token cookie: 2 phút
            };

            Response.Cookies.Append("RefreshToken", refreshToken, refreshOptions);
        }
    }
}