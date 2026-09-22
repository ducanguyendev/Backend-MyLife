using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using MyLife.Shared.Data;
using MyLife.Features.Avatar.Services;

namespace MyLife.Features.Avatar.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AvatarController : ControllerBase
    {
        private readonly IGoogleDriveAvatarService _avatarService;
        private readonly AppDbContext _db;

        public AvatarController(IGoogleDriveAvatarService avatarService, AppDbContext db)
        {
            _avatarService = avatarService;
            _db = db;
        }

        /// <summary>
        /// Lấy ảnh đại diện của người dùng từ Google Drive theo email (Redirect tới Google Drive URL lưu trong DB)
        /// </summary>
        [HttpGet("{email}")]
        public async Task<IActionResult> GetAvatar(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Email không hợp lệ.");

            var normalizedEmail = email.Trim().ToLowerInvariant();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
            if (user == null || string.IsNullOrWhiteSpace(user.AvatarUrl))
                return NotFound(new { message = "Chưa có ảnh đại diện." });

            if (user.AvatarUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var url = user.AvatarUrl;
                if (url.Contains("drive.google.com/file/d/"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(url, @"/file/d/([a-zA-Z0-9_-]+)");
                    if (match.Success)
                    {
                        var tParam = url.Contains("?t=") ? url.Substring(url.IndexOf("?t=")) : "";
                        url = $"https://lh3.googleusercontent.com/d/{match.Groups[1].Value}{tParam}";
                    }
                }
                return Redirect(url);
            }

            return NotFound(new { message = "Không tìm thấy ảnh đại diện." });
        }

        /// <summary>
        /// Tải lên ảnh đại diện mới và lưu đường dẫn vào cơ sở dữ liệu
        /// </summary>
        [Authorize]
        [HttpPost("upload")]
        public async Task<IActionResult> UploadAvatar(IFormFile file)
        {
            var email = User.FindFirstValue(ClaimTypes.Email) 
                     ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(email))
                return Unauthorized(new { message = "Phiên làm việc không hợp lệ." });

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Vui lòng chọn một file ảnh hợp lệ." });

            // Giới hạn 5MB
            if (file.Length > 5 * 1024 * 1024)
                return BadRequest(new { message = "Kích thước file ảnh không được vượt quá 5MB." });

            var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif", "image/jpg" };
            var allowedExts = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            var isMimeOk = allowedMimeTypes.Contains(file.ContentType.ToLowerInvariant());
            var isExtOk = !string.IsNullOrEmpty(ext) && allowedExts.Contains(ext);

            if (!isMimeOk && !isExtOk)
                return BadRequest(new { message = "Chỉ chấp nhận các định dạng ảnh JPG, PNG, WEBP, GIF." });

            var (success, driveUrl, fileId) = await _avatarService.UploadAvatarAsync(email, file);
            if (!success)
                return StatusCode(500, new { message = "Không thể tải ảnh lên. Vui lòng thử lại." });

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // Lưu URL Google Drive chuẩn CDN (lh3.googleusercontent.com/d/{fileId}) để client tải trực tiếp ảnh, không nhận nhầm trang HTML preview
            string directUrl;
            if (!string.IsNullOrEmpty(fileId))
            {
                directUrl = $"https://lh3.googleusercontent.com/d/{fileId}";
            }
            else if (!string.IsNullOrEmpty(driveUrl) && driveUrl.Contains("/file/d/"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(driveUrl, @"/file/d/([a-zA-Z0-9_-]+)");
                directUrl = match.Success ? $"https://lh3.googleusercontent.com/d/{match.Groups[1].Value}" : driveUrl;
            }
            else
            {
                directUrl = driveUrl ?? "";
            }

            var baseDriveUrl = !string.IsNullOrEmpty(directUrl) ? directUrl.Split('?')[0] : "";
            var finalAvatarUrl = !string.IsNullOrEmpty(baseDriveUrl) 
                ? $"{baseDriveUrl}?t={timestamp}" 
                : $"/api/avatar/{Uri.EscapeDataString(email)}?t={timestamp}";

            // Cập nhật đường dẫn file ảnh avatar vào cơ sở dữ liệu PostgreSQL
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
            if (user != null)
            {
                user.AvatarUrl = finalAvatarUrl;
                user.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                Console.WriteLine($"[AVATAR] 💾 Đã lưu avatarUrl vào DB cho user: {normalizedEmail} -> {finalAvatarUrl}");
            }

            return Ok(new
            {
                message = "Cập nhật ảnh đại diện thành công!",
                avatarUrl = finalAvatarUrl
            });
        }

        /// <summary>
        /// Xóa ảnh đại diện và cập nhật DB về null
        /// </summary>
        [Authorize]
        [HttpDelete]
        public async Task<IActionResult> DeleteAvatar()
        {
            var email = User.FindFirstValue(ClaimTypes.Email) 
                     ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(email))
                return Unauthorized(new { message = "Phiên làm việc không hợp lệ." });

            await _avatarService.DeleteAvatarAsync(email);

            // Cập nhật gỡ bỏ đường dẫn avatar trong cơ sở dữ liệu PostgreSQL
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
            if (user != null)
            {
                user.AvatarUrl = null;
                user.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                Console.WriteLine($"[AVATAR] 🗑️ Đã xóa avatarUrl trong DB cho user: {normalizedEmail}");
            }

            return Ok(new { message = "Đã xóa ảnh đại diện thành công!" });
        }
    }
}
