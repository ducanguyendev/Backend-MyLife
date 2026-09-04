using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LoginApp.Services
{
    public interface IGoogleDriveAvatarService
    {
        Task<(Stream? Stream, string ContentType)> GetAvatarAsync(string email);
        Task<bool> UploadAvatarAsync(string email, IFormFile file);
        Task<bool> DeleteAvatarAsync(string email);
    }

    public class GoogleDriveAvatarService : IGoogleDriveAvatarService
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _webAppUrl;
        private readonly string _localCacheDir;

        public GoogleDriveAvatarService(
            IConfiguration config, 
            IWebHostEnvironment env,
            IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _env = env;
            _httpClientFactory = httpClientFactory;
            _webAppUrl = _config["GoogleDrive:WebAppUrl"] ?? "";

            var cacheSubDir = _config["GoogleDrive:LocalCachePath"] ?? "Storage/Avatars";
            _localCacheDir = Path.Combine(_env.ContentRootPath, cacheSubDir);
            if (!Directory.Exists(_localCacheDir))
            {
                Directory.CreateDirectory(_localCacheDir);
            }
        }

        private string SanitizeEmail(string email)
        {
            return email.Trim().ToLowerInvariant().Replace("/", "_").Replace("\\", "_");
        }

        public async Task<(Stream? Stream, string ContentType)> GetAvatarAsync(string email)
        {
            var cleanEmail = SanitizeEmail(email);

            // 1. Kiểm tra Local Cache trước để phản hồi siêu nhanh
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            foreach (var ext in extensions)
            {
                var localFile = Path.Combine(_localCacheDir, cleanEmail + ext);
                if (File.Exists(localFile))
                {
                    var memoryStream = new MemoryStream();
                    using (var fs = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        await fs.CopyToAsync(memoryStream);
                    }
                    memoryStream.Position = 0;
                    var ct = ext == ".png" ? "image/png" : ext == ".webp" ? "image/webp" : "image/jpeg";
                    return (memoryStream, ct);
                }
            }

            return (null, "image/jpeg");
        }

        public async Task<bool> UploadAvatarAsync(string email, IFormFile file)
        {
            if (file == null || file.Length == 0) return false;

            var cleanEmail = SanitizeEmail(email);
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";

            var fileName = $"{cleanEmail}{ext}";
            var localPath = Path.Combine(_localCacheDir, fileName);

            // 1. Xóa các file cũ của email này trong cache
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            foreach (var oldExt in extensions)
            {
                var oldLocal = Path.Combine(_localCacheDir, cleanEmail + oldExt);
                if (File.Exists(oldLocal)) File.Delete(oldLocal);
            }

            // 2. Lưu vào local cache
            using (var stream = new FileStream(localPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // 3. Đẩy lên Google Drive thông qua Google Apps Script Webhook
            if (!string.IsNullOrWhiteSpace(_webAppUrl))
            {
                try
                {
                    byte[] fileBytes;
                    using (var ms = new MemoryStream())
                    {
                        await file.CopyToAsync(ms);
                        fileBytes = ms.ToArray();
                    }

                    var base64 = Convert.ToBase64String(fileBytes);
                    var payloadObj = new
                    {
                        action = "upload",
                        email = cleanEmail,
                        fileBase64 = base64,
                        contentType = file.ContentType
                    };

                    var jsonString = JsonSerializer.Serialize(payloadObj);
                    var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

                    using var handler = new HttpClientHandler
                    {
                        AllowAutoRedirect = true,
                        MaxAutomaticRedirections = 5
                    };
                    using var client = new HttpClient(handler);
                    client.Timeout = TimeSpan.FromSeconds(30);

                    var response = await client.PostAsync(_webAppUrl, content);
                    var respContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[GoogleDriveAvatarService] 🚀 Google Drive Upload Response: {respContent}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GoogleDriveAvatarService] ⚠️ Lỗi khi gửi file lên Google Drive: {ex.Message}");
                }
            }

            return true;
        }

        public async Task<bool> DeleteAvatarAsync(string email)
        {
            var cleanEmail = SanitizeEmail(email);

            // 1. Xóa local cache
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            foreach (var ext in extensions)
            {
                var localFile = Path.Combine(_localCacheDir, cleanEmail + ext);
                if (File.Exists(localFile)) File.Delete(localFile);
            }

            // 2. Gửi lệnh xóa file trên Google Drive
            if (!string.IsNullOrWhiteSpace(_webAppUrl))
            {
                try
                {
                    var payloadObj = new
                    {
                        action = "delete",
                        email = cleanEmail
                    };

                    var jsonString = JsonSerializer.Serialize(payloadObj);
                    var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

                    using var handler = new HttpClientHandler
                    {
                        AllowAutoRedirect = true,
                        MaxAutomaticRedirections = 5
                    };
                    using var client = new HttpClient(handler);
                    client.Timeout = TimeSpan.FromSeconds(30);

                    var response = await client.PostAsync(_webAppUrl, content);
                    var respContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[GoogleDriveAvatarService] 🗑️ Google Drive Delete Response: {respContent}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GoogleDriveAvatarService] ⚠️ Lỗi khi gửi lệnh xóa file lên Google Drive: {ex.Message}");
                }
            }

            return true;
        }
    }
}
