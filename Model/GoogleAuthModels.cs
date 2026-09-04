using System.Text.Json.Serialization;

namespace LoginApp.Model
{
    /// <summary>
    /// Request từ Client gửi lên chứa Authorization Code của Google
    /// </summary>
    public class GoogleAuthRequest
    {
        public string Code { get; set; } = string.Empty;
        public string? RedirectUri { get; set; }
    }

    /// <summary>
    /// Response nhận về từ Google Token Endpoint (oauth2.googleapis.com/token)
    /// </summary>
    public class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
    }

    /// <summary>
    /// Thông tin User lấy về từ Google API (oauth2/v2/userinfo)
    /// </summary>
    public class GoogleUserInfo
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("verified_email")]
        public bool VerifiedEmail { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("given_name")]
        public string? GivenName { get; set; }

        [JsonPropertyName("family_name")]
        public string? FamilyName { get; set; }

        [JsonPropertyName("picture")]
        public string? Picture { get; set; }
    }
}
