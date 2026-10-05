using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MyLife.Features.Auth.Services;

public interface ITokenService
{
    string GenerateAccessToken(string email, string role = "USER");
    string GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}

public sealed class TokenService(IConfiguration configuration) : ITokenService
{
    public string GenerateAccessToken(string email, string role = "USER")
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, email),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Role, role)
        };

        var token = new JwtSecurityToken(
            issuer: configuration["JwtSettings:Issuer"],
            audience: configuration["JwtSettings:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(configuration.GetValue<int>("JwtSettings:AccessTokenSeconds")),
            signingCredentials: new SigningCredentials(SigningKey(), SecurityAlgorithms.HmacSha256)
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = configuration["JwtSettings:Issuer"],
                ValidateAudience = true,
                ValidAudience = configuration["JwtSettings:Audience"],
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = SigningKey(),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateLifetime = false
            }, out _);
            return principal;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(configuration["JwtSettings:SecretKey"]!));
}
