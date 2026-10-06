using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using Google.Apis.Auth;
using MyLife.Features.Auth.Models;

namespace MyLife.Features.Auth.Services;

public sealed record GoogleIdentity(
    string Email,
    string Subject,
    string? Name,
    string? Picture,
    bool IsAuthoritativeEmail);

public interface IGoogleCredentialVerifier
{
    Task<GoogleIdentity?> VerifyAsync(GoogleAuthRequest request, CancellationToken cancellationToken = default);
}

public sealed class GoogleCredentialVerifier(
    IConfiguration configuration,
    IHttpClientFactory httpClients) : IGoogleCredentialVerifier
{
    public async Task<GoogleIdentity?> VerifyAsync(GoogleAuthRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code) == string.IsNullOrWhiteSpace(request.IdToken)) return null;
        var clientId = configuration["Authentication:Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId)) throw new HttpRequestException("Google client ID missing");

        var idToken = request.IdToken;
        if (string.IsNullOrWhiteSpace(idToken) && !string.IsNullOrWhiteSpace(request.Code))
        {
            var secret = configuration["Authentication:Google:ClientSecret"];
            if (string.IsNullOrWhiteSpace(secret)) return null;
            var values = new Dictionary<string, string>
            {
                ["code"] = request.Code,
                ["client_id"] = clientId,
                ["client_secret"] = secret,
                ["redirect_uri"] = request.RedirectUri ?? "postmessage",
                ["grant_type"] = "authorization_code"
            };
            using var response = await httpClients.CreateClient().PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(values),
                cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            idToken = (await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken))?.IdToken;
        }

        if (string.IsNullOrWhiteSpace(idToken)) return null;
        var allowedAudiences = configuration.GetSection("Authentication:Google:AllowedClientIds").Get<string[]>() ?? [];
        allowedAudiences = allowedAudiences.Append(clientId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
        var payload = await GoogleJsonWebSignature.ValidateAsync(
            idToken,
            new GoogleJsonWebSignature.ValidationSettings { Audience = allowedAudiences });
        if (!new EmailAddressAttribute().IsValid(payload.Email) || string.IsNullOrWhiteSpace(payload.Subject) || payload.EmailVerified != true ||
            payload.ExpirationTimeSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;

        var authoritativeEmail = payload.Email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase) ||
                                 !string.IsNullOrWhiteSpace(payload.HostedDomain);
        return new GoogleIdentity(payload.Email, payload.Subject, payload.Name, payload.Picture, authoritativeEmail);
    }
}
