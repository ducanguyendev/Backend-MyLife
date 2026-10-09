using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyLife.Shared.Data;

namespace MyLife.Shared.Security;

public interface ICurrentUserService
{
    Task<int?> GetUserIdAsync(CancellationToken ct);
}

public sealed class CurrentUserService(IHttpContextAccessor accessor, AppDbContext db) : ICurrentUserService
{
    public const string VerifiedUserIdKey = "MyLife.VerifiedUserId";
    public async Task<int?> GetUserIdAsync(CancellationToken ct)
    {
        var http = accessor.HttpContext;
        if (http?.User.Identity?.IsAuthenticated != true) return null;
        if (http.Items.TryGetValue(VerifiedUserIdKey, out var verified) && verified is int id) return id;
        var email = http.User.FindFirstValue(ClaimTypes.Email) ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(email)) return null;
        var normalized = email.Trim().ToLowerInvariant();
        return await db.Users.AsNoTracking().Where(u => u.IsActive && u.Email == normalized).Select(u => (int?)u.Id).SingleOrDefaultAsync(ct);
    }
}
