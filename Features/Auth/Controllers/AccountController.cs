using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using MyLife.Features.Auth.Models;
using MyLife.Features.Auth.Services;
using MyLife.Features.Avatar.Services;
using MyLife.Features.User.Models;
using MyLife.Shared.Data;
using MyLife.Shared.Entities;
using MyLife.Shared.Security;
using AppUser = MyLife.Shared.Entities.User;

namespace MyLife.Features.Auth.Controllers;

[ApiController]
[Route("api")]
public sealed class AccountController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IGoogleCredentialVerifier _googleCredentials;
    private readonly IGoogleAvatarSyncService _googleAvatars;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountController> _logger;

    public AccountController(AppDbContext db, ITokenService tokens, IGoogleCredentialVerifier googleCredentials, IGoogleAvatarSyncService googleAvatars, IConfiguration configuration, ILogger<AccountController> logger)
        => (_db, _tokens, _googleCredentials, _googleAvatars, _configuration, _logger) = (db, tokens, googleCredentials, googleAvatars, configuration, logger);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginViewModel model)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var email = NormalizeEmail(model.Email);
        var user = await UserWithRoles().SingleOrDefaultAsync(x => x.Email == email);
        if (user is null || !user.IsActive || !user.HasLocalProvider || !BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
        {
            await LogLoginAsync(user, email, "FAILED");
            await _db.SaveChangesAsync();
            return Unauthorized(Error("Invalid email or password."));
        }
        await LogLoginAsync(user, email, "SUCCESS");
        return Ok(await CreateSessionResponseAsync(user));
    }

    [HttpPost("auth/register")]
    public async Task<IActionResult> Register([FromBody] RegisterViewModel model)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (model.Password != model.ConfirmPassword) return BadRequest(Error("Passwords do not match."));
        if (Encoding.UTF8.GetByteCount(model.Password) > 72) return BadRequest(Error("Password must not exceed 72 UTF-8 bytes."));
        var email = NormalizeEmail(model.Email);
        if (!email.EndsWith("@gmail.com", StringComparison.Ordinal)) return BadRequest(Error("Only Gmail addresses are supported."));
        if (await _db.Users.AnyAsync(x => x.Email == email)) return Conflict(Error("This email is already registered."));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!model.DateOfBirth.HasValue || model.DateOfBirth > today) return BadRequest(Error("Date of birth is invalid."));
        var age = today.Year - model.DateOfBirth.Value.Year;
        if (model.DateOfBirth.Value > today.AddYears(-age)) age--;
        if (age is < 6 or > 120) return BadRequest(Error("Date of birth is invalid."));
        var role = await _db.Roles.SingleOrDefaultAsync(r => r.Name == AppRoles.User);
        if (role is null) return StatusCode(503, Error("The service has not been initialized."));
        var user = new AppUser { Email = email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password, workFactor: 11), FullName = model.FullName.Trim(),
            PhoneNumber = model.PhoneNumber.Trim(), Gender = model.Gender.Trim(), DateOfBirth = model.DateOfBirth, AuthProvider = 0, HasLocalProvider = true, IsActive = true,
            VerifiedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Users.Add(user); _db.UserRoles.Add(new UserRole { User = user, Role = role });
        await LogLoginAsync(user, email, "SUCCESS"); await _db.SaveChangesAsync();
        return StatusCode(StatusCodes.Status201Created, new { success = true, message = "Registration completed.", email });
    }

    [HttpPost("auth/google")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleAuthRequest request)
    {
        try
        {
            var identity = await _googleCredentials.VerifyAsync(request, HttpContext.RequestAborted);
            if (identity is null) return BadRequest(Error("The Google credential is invalid or expired."));
            var email = NormalizeEmail(identity.Email);
            var user = await UserWithRoles().SingleOrDefaultAsync(x => x.GoogleSubject == identity.Subject)
                ?? await UserWithRoles().SingleOrDefaultAsync(x => x.Email == email);
            if (user is null)
            {
                var role = await _db.Roles.SingleOrDefaultAsync(r => r.Name == AppRoles.User);
                if (role is null) return StatusCode(503, Error("The service has not been initialized."));
                user = new AppUser { Email = email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), workFactor: 11),
                    FullName = identity.Name, AuthProvider = 1, HasGoogleProvider = true, GoogleSubject = identity.Subject, IsActive = true, VerifiedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, UserRoles = [new UserRole { Role = role }] };
                _db.Users.Add(user); await _db.SaveChangesAsync();
            }
            else
            {
                // AuthProvider remains the legacy account-origin indicator: do not turn a LOCAL account into GOOGLE.
                if (user.GoogleSubject is null && !identity.IsAuthoritativeEmail)
                    return BadRequest(Error("Please use your existing sign-in method to link this Google account."));
                if (!user.IsActive) return Unauthorized(Error("This account is locked."));
                if (!string.IsNullOrWhiteSpace(user.GoogleSubject) && user.GoogleSubject != identity.Subject) return BadRequest(Error("This Google account is linked to a different identity."));
                user.HasGoogleProvider = true; user.GoogleSubject ??= identity.Subject;
                user.UpdatedAt = DateTime.UtcNow;
            }
            if (!user.IsActive) { await LogLoginAsync(user, email, "FAILED"); await _db.SaveChangesAsync(); return Unauthorized(Error("This account is locked.")); }
            await _googleAvatars.SyncIfNeededAsync(user, identity.Picture, HttpContext.RequestAborted);
            await LogLoginAsync(user, email, "SUCCESS"); await _db.SaveChangesAsync();
            return Ok(await CreateSessionResponseAsync(user));
        }
        catch (InvalidJwtException) { return BadRequest(Error("The Google credential is invalid or expired.")); }
        catch (HttpRequestException ex) { _logger.LogWarning(ex, "Google authentication is unavailable"); return StatusCode(503, Error("Google authentication is temporarily unavailable.")); }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await CurrentUserAsync();
        return user is null || !user.IsActive ? Unauthorized(Error("Session is invalid.")) : Ok(ToUserDto(user));
    }

    [Authorize]
    [HttpPut("me/profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileViewModel model)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var user = await CurrentUserAsync();
        if (user is null || !user.IsActive) return Unauthorized(Error("Session is invalid."));
        user.FullName = model.FullName.Trim(); user.PhoneNumber = model.PhoneNumber.Trim(); user.Gender = model.Gender.Trim(); user.DateOfBirth = model.DateOfBirth; user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(); return Ok(new { success = true, message = "Profile updated.", user = ToUserDto(user) });
    }

    [Authorize]
    [HttpPost("auth/change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var user = await CurrentUserAsync();
        if (user is null || !user.IsActive) return Unauthorized(Error("Session is invalid."));
        if (!user.HasLocalProvider) return BadRequest(Error("Google-only accounts do not have a local password."));
        if (!BCrypt.Net.BCrypt.Verify(model.CurrentPassword, user.PasswordHash)) return BadRequest(Error("Current password is incorrect."));
        if (model.CurrentPassword == model.NewPassword) return BadRequest(Error("New password must differ from the current password."));
        if (Encoding.UTF8.GetByteCount(model.NewPassword) > 72) return BadRequest(Error("Password must not exceed 72 UTF-8 bytes."));
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword, workFactor: 11); user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(); return Ok(new { success = true, message = "Password updated." });
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshTokenRequest? request)
    {
        var access = Request.Cookies["AccessToken"] ?? request?.AccessToken;
        var rawRefresh = Request.Cookies["RefreshToken"] ?? request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(rawRefresh)) return Unauthorized(Error("A valid session is required."));
        var email = string.IsNullOrWhiteSpace(access) ? null : _tokens.GetPrincipalFromExpiredToken(access)?.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrWhiteSpace(access) && string.IsNullOrWhiteSpace(email)) return Unauthorized(Error("A valid session is required."));
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var stored = await _db.RefreshTokens.Include(x => x.User).ThenInclude(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Token == HashRefreshToken(rawRefresh));
        if (stored is not null && !string.IsNullOrWhiteSpace(email) && !string.Equals(stored.User.Email, email, StringComparison.OrdinalIgnoreCase))
            stored = null;
        if (stored is null || stored.IsRevoked || stored.ExpiresAt <= DateTime.UtcNow || !stored.User.IsActive)
        {
            if (stored is not null) { stored.IsRevoked = true; await _db.SaveChangesAsync(); }
            await transaction.CommitAsync(); return Unauthorized(Error("Session has expired. Please sign in again."));
        }
        // Atomically claim the token: two requests may both read it before the transaction begins to write.
        // The predicate is rechecked under the row lock, so only one refresh can consume this token.
        var consumed = await _db.RefreshTokens
            .Where(x => x.Id == stored.Id && !x.IsRevoked && x.ExpiresAt > DateTime.UtcNow && x.User.IsActive)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.IsRevoked, true));
        if (consumed != 1) return Unauthorized(Error("This refresh token has already been used."));
        var newRawRefresh = _tokens.GenerateRefreshToken(); _db.RefreshTokens.Add(NewRefreshToken(stored.UserId, newRawRefresh));
        await _db.SaveChangesAsync(); await transaction.CommitAsync();
        return Ok(CreateSessionResponse(stored.User, newRawRefresh));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshTokenRequest? request)
    {
        var rawRefresh = Request.Cookies["RefreshToken"] ?? request?.RefreshToken;
        if (!string.IsNullOrWhiteSpace(rawRefresh))
        {
            var stored = await _db.RefreshTokens.SingleOrDefaultAsync(x => x.Token == HashRefreshToken(rawRefresh));
            if (stored is not null && !stored.IsRevoked) { stored.IsRevoked = true; await _db.SaveChangesAsync(); }
        }
        DeleteAuthCookies(); return Ok(new { success = true, message = "Signed out." });
    }

    [Authorize(Roles = AppRoles.Admin)]
    [HttpGet("admin/stats")]
    public async Task<IActionResult> AdminStats() => Ok(new { totalUsers = await _db.Users.CountAsync(), totalActive = await _db.Users.CountAsync(x => x.IsActive), totalLocked = await _db.Users.CountAsync(x => !x.IsActive),
        last7Days = new { successLogins = await _db.LoginLogs.CountAsync(x => x.Status == "SUCCESS" && x.CreatedAt >= DateTime.UtcNow.AddDays(-7)), failedLogins = await _db.LoginLogs.CountAsync(x => x.Status == "FAILED" && x.CreatedAt >= DateTime.UtcNow.AddDays(-7)), newRegistrations = await _db.Users.CountAsync(x => x.CreatedAt >= DateTime.UtcNow.AddDays(-7)) } });

    [Authorize(Roles = AppRoles.Admin)]
    [HttpGet("admin/logs")]
    public async Task<IActionResult> AdminLogs() => Ok(await _db.LoginLogs.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(50)
        .Select(x => new { id = x.Id, attemptEmail = x.AttemptEmail, status = x.Status, ipAddress = x.IpAddress, createdAt = x.CreatedAt }).ToListAsync());

    [Authorize(Roles = AppRoles.Admin)]
    [HttpGet("admin/users")]
    public async Task<IActionResult> AdminUsers([FromQuery] string? search)
    {
        var query = UserWithRoles().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => EF.Functions.ILike(x.Email, $"%{term}%") || (x.FullName != null && EF.Functions.ILike(x.FullName, $"%{term}%"))); }
        return Ok((await query.OrderByDescending(x => x.CreatedAt).ToListAsync()).Select(ToUserDto));
    }

    [Authorize(Roles = AppRoles.Admin)]
    [HttpPut("admin/users/{id:int}/status")]
    public async Task<IActionResult> SetUserStatus(int id, [FromBody] UpdateUserStatusRequest request)
    {
        var user = await UserWithRoles().SingleOrDefaultAsync(x => x.Id == id);
        if (user is null) return NotFound(Error("User not found."));
        if (PrimaryRole(user) == AppRoles.Admin) return BadRequest(Error("Administrator accounts cannot be locked."));
        user.IsActive = request.IsActive!.Value; user.UpdatedAt = DateTime.UtcNow;
        if (!user.IsActive) await _db.RefreshTokens.Where(x => x.UserId == id && !x.IsRevoked).ExecuteUpdateAsync(x => x.SetProperty(t => t.IsRevoked, true));
        await _db.SaveChangesAsync(); return Ok(new { success = true, isActive = user.IsActive });
    }

    [Authorize(Roles = AppRoles.Admin)]
    [HttpPut("admin/users/{id:int}/role")]
    public async Task<IActionResult> SetUserRole(int id, [FromBody] UpdateUserRoleRequest request)
    {
        var name = request.Role?.Trim().ToUpperInvariant(); if (name is not (AppRoles.Admin or AppRoles.User)) return BadRequest(Error("Role must be ADMIN or USER."));
        await using var transaction = await _db.Database.BeginTransactionAsync();
        // Serialize demotions so concurrent administrators cannot remove the last active admin.
        await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(741003002);");
        var user = await UserWithRoles().SingleOrDefaultAsync(x => x.Id == id); var role = await _db.Roles.SingleOrDefaultAsync(x => x.Name == name);
        if (user is null || role is null) return NotFound(Error("User or role not found."));
        if (PrimaryRole(user) == AppRoles.Admin && name == AppRoles.User &&
            !await _db.Users.AnyAsync(x => x.Id != id && x.IsActive && x.UserRoles.Any(r => r.Role.Name == AppRoles.Admin)))
            return BadRequest(Error("The last active administrator cannot be demoted."));
        var removed = user.UserRoles.Where(x => x.RoleId != role.Id).ToList();
        _db.UserRoles.RemoveRange(removed);
        var alreadyAssigned = user.UserRoles.Any(x => x.RoleId == role.Id);
        if (!alreadyAssigned) _db.UserRoles.Add(new UserRole { UserId = id, RoleId = role.Id });
        if (removed.Count > 0 || !alreadyAssigned)
        {
            user.UpdatedAt = DateTime.UtcNow;
            await _db.RefreshTokens.Where(x => x.UserId == id && !x.IsRevoked).ExecuteUpdateAsync(x => x.SetProperty(t => t.IsRevoked, true));
        }
        await _db.SaveChangesAsync(); await transaction.CommitAsync();
        return Ok(new { success = true, role = name });
    }

    [Authorize(Roles = AppRoles.Admin)]
    [HttpDelete("admin/users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await UserWithRoles().SingleOrDefaultAsync(x => x.Id == id);
        if (user is null) return NotFound(Error("User not found.")); if (PrimaryRole(user) == AppRoles.Admin) return BadRequest(Error("Administrator accounts cannot be deleted."));
        _db.Users.Remove(user); await _db.SaveChangesAsync(); return Ok(new { success = true, deletedId = id });
    }

    private async Task<object> CreateSessionResponseAsync(AppUser user) { var refresh = _tokens.GenerateRefreshToken(); _db.RefreshTokens.Add(NewRefreshToken(user.Id, refresh)); await _db.SaveChangesAsync(); return CreateSessionResponse(user, refresh); }
    private object CreateSessionResponse(AppUser user, string refresh)
    {
        var access = _tokens.GenerateAccessToken(user.Email, PrimaryRole(user));
        SetAuthCookies(access, refresh);
        var metadata = new { success = true, message = "Authenticated.", user = ToUserDto(user), accessTokenExpiresIn = AccessSeconds, refreshTokenExpiresIn = RefreshMinutes * 60 };
        // Only native clients explicitly opt into body tokens; browsers use HttpOnly cookies exclusively.
        return Request.Headers["X-Client-Platform"] == "mobile"
            ? new { metadata.success, metadata.message, metadata.user, accessToken = access, refreshToken = refresh, metadata.accessTokenExpiresIn, metadata.refreshTokenExpiresIn }
            : metadata;
    }
    private RefreshToken NewRefreshToken(int userId, string raw) => new() { UserId = userId, Token = HashRefreshToken(raw), IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(), UserAgent = Request.Headers.UserAgent.ToString(), ExpiresAt = DateTime.UtcNow.AddMinutes(RefreshMinutes), CreatedAt = DateTime.UtcNow };
    private IQueryable<AppUser> UserWithRoles() => _db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role);
    private async Task<AppUser?> CurrentUserAsync() { var email = User.FindFirstValue(ClaimTypes.Email); return string.IsNullOrWhiteSpace(email) ? null : await UserWithRoles().SingleOrDefaultAsync(x => x.Email == email); }
    private async Task LogLoginAsync(AppUser? user, string email, string status) => await _db.LoginLogs.AddAsync(new LoginLog { User = user, AttemptEmail = email, Status = status, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(), CreatedAt = DateTime.UtcNow });
    private static string NormalizeEmail(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
    private static string PrimaryRole(AppUser user) => user.UserRoles.Any(x => x.Role.Name == AppRoles.Admin) ? AppRoles.Admin : AppRoles.User;
    private static string HashRefreshToken(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    private static object Error(string message) => new { success = false, message };
    private int AccessSeconds => _configuration.GetValue<int>("JwtSettings:AccessTokenSeconds"); private int RefreshMinutes => _configuration.GetValue<int>("JwtSettings:RefreshTokenMinutes");
    private object ToUserDto(AppUser user) => new { id = user.Id, email = user.Email, fullName = user.FullName, name = string.IsNullOrWhiteSpace(user.FullName) ? user.Email.Split('@')[0] : user.FullName, phoneNumber = user.PhoneNumber, gender = user.Gender, dateOfBirth = user.DateOfBirth?.ToString("yyyy-MM-dd"), avatarUrl = user.AvatarUrl, role = PrimaryRole(user), authProvider = user.AuthProvider, authProviderName = user.AuthProvider == 1 ? "GOOGLE" : "LOCAL", loginProviders = new { local = user.HasLocalProvider, google = user.HasGoogleProvider }, isActive = user.IsActive, verifiedAt = user.VerifiedAt, createdAt = user.CreatedAt, updatedAt = user.UpdatedAt };
    private void SetAuthCookies(string access, string refresh)
    {
        Response.Cookies.Append("AccessToken", access, CookieOptions(DateTimeOffset.UtcNow.AddSeconds(AccessSeconds)));
        Response.Cookies.Append("RefreshToken", refresh, CookieOptions(DateTimeOffset.UtcNow.AddMinutes(RefreshMinutes)));
    }

    private CookieOptions CookieOptions(DateTimeOffset? expires = null)
    {
        var sameSite = Enum.TryParse<SameSiteMode>(_configuration["Authentication:CookieSameSite"], true, out var configured)
            ? configured : SameSiteMode.Lax;
        var secure = !HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() || sameSite == SameSiteMode.None;
        return new CookieOptions { HttpOnly = true, Secure = secure, SameSite = sameSite, Path = "/", Expires = expires };
    }

    private void DeleteAuthCookies()
    {
        Response.Cookies.Delete("AccessToken", CookieOptions());
        Response.Cookies.Delete("RefreshToken", CookieOptions());
    }
}

public sealed class UpdateUserStatusRequest { [Required] public bool? IsActive { get; set; } }
public sealed class UpdateUserRoleRequest { public string? Role { get; set; } }
