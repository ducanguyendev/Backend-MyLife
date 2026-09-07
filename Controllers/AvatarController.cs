using LoginApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LoginApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AvatarController : ControllerBase
    {
        private readonly IGoogleDriveAvatarService _avatarService;

        public AvatarController(IGoogleDriveAvatarService avatarService)
        {
            _avatarService = avatarService;
        }

        /// <summary>
        /// Lấy ảnh đại diện của người dùng từ Google Drive theo email
        /// </summary>
        [HttpGet("{email}")]
        public async Task<IActionResult> GetAvatar(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Email không hợp lệ.");

            var (stream, contentType) = await _avatarService.GetAvatarAsync(email);
            if (stream == null)
                return NotFound(new { message = "Chưa có ảnh đại diện." });

            // Không lưu cache cứng ở trình duyệt để cập nhật/xóa avatar tức thì
            Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            Response.Headers.Append("Pragma", "no-cache");
            Response.Headers.Append("Expires", "0");
            return File(stream, contentType);
        }

        /// <summary>
        /// Tải lên ảnh đại diện mới (Không lưu vào Database)
        /// </summary>
        [Authorize]
        [HttpPost("upload")]
        public async Task<IActionResult> UploadAvatar([FromForm] IFormFile file)
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

            var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
            if (!allowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
                return BadRequest(new { message = "Chỉ chấp nhận các định dạng ảnh JPG, PNG, WEBP, GIF." });

            var success = await _avatarService.UploadAvatarAsync(email, file);
            if (!success)
                return StatusCode(500, new { message = "Không thể tải ảnh lên. Vui lòng thử lại." });

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var avatarUrl = $"/api/avatar/{Uri.EscapeDataString(email)}?t={timestamp}";

            return Ok(new
            {
                message = "Cập nhật ảnh đại diện thành công!",
                avatarUrl
            });
        }

        /// <summary>
        /// Xóa ảnh đại diện
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

            return Ok(new { message = "Đã xóa ảnh đại diện thành công!" });
        }
    }
}
