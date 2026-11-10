namespace MyLife.Shared.Security;

public static class AuthTransportPolicy
{
    public static bool IsNativeTokenClient(string? clientPlatform, bool hasCookieSession) =>
        string.Equals(clientPlatform, "mobile", StringComparison.Ordinal)
        && !hasCookieSession;

    public static string? GetRequestRejectionCode(
        string method,
        string? clientPlatform,
        string? origin,
        bool hasCookieSession,
        string? csrfHeader,
        IReadOnlyCollection<string> allowedOrigins)
    {
        if (string.Equals(clientPlatform, "mobile", StringComparison.Ordinal) && hasCookieSession)
            return "MOBILE_COOKIE_SESSION_REJECTED";

        var unsafeMethod = method is not ("GET" or "HEAD" or "OPTIONS");
        if (!unsafeMethod) return null;

        var originRejected = !string.IsNullOrEmpty(origin)
            && !allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
        if (originRejected) return "REQUEST_ORIGIN_REJECTED";
        if (hasCookieSession && csrfHeader != "MyLife") return "CSRF_HEADER_REQUIRED";
        return null;
    }
}
