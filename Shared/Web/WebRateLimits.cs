using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using MyLife.Shared.Security;

namespace MyLife.Shared.Web;

public static class WebRateLimits
{
    public const string Auth = "auth";
    public const string Refresh = "refresh";
    public const string Upload = "library-upload";
    public static IServiceCollection AddWebRateLimits(this IServiceCollection services, IConfiguration config)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(delay.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(new { success = false, code = "RATE_LIMITED", message = "Too many requests. Please try again later." }, ct);
            };
            Add(Auth, "Auth", 10, false);
            Add(Refresh, "Refresh", 30, false);
            Add(Upload, "Upload", 10, true);
            void Add(string policy, string setting, int fallback, bool byUser)
            {
                var limit = config.GetValue<int?>("RateLimits:" + setting + ":PermitLimit") ?? fallback;
                var seconds = config.GetValue<int?>("RateLimits:" + setting + ":WindowSeconds") ?? 60;
                if (limit < 1 || seconds < 1) throw new InvalidOperationException("Rate limit settings must be positive.");
                options.AddPolicy(policy, http => RateLimitPartition.GetFixedWindowLimiter(
                    byUser && http.Items[CurrentUserService.VerifiedUserIdKey] is int id ? "user:" + id : "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromSeconds(seconds), QueueLimit = 0, AutoReplenishment = true }));
            }
        });
        return services;
    }
}
