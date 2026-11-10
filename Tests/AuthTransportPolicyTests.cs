using MyLife.Shared.Security;
using Xunit;

public sealed class AuthTransportPolicyTests
{
    private static readonly string[] AllowedOrigins = ["http://localhost:7000"];

    [Fact]
    public void Mobile_header_with_browser_cookies_is_rejected_even_when_csrf_header_is_present()
    {
        var code = AuthTransportPolicy.GetRequestRejectionCode(
            "POST", "mobile", "http://localhost:7000", hasCookieSession: true, csrfHeader: "MyLife", allowedOrigins: AllowedOrigins);

        Assert.Equal("MOBILE_COOKIE_SESSION_REJECTED", code);
    }

    [Fact]
    public void Mobile_header_does_not_bypass_untrusted_origin_check()
    {
        var code = AuthTransportPolicy.GetRequestRejectionCode(
            "POST", "mobile", "http://localhost:8081", hasCookieSession: false, csrfHeader: null, allowedOrigins: AllowedOrigins);

        Assert.Equal("REQUEST_ORIGIN_REJECTED", code);
    }

    [Fact]
    public void Browser_cookie_mutations_still_require_csrf_header()
    {
        var missingHeader = AuthTransportPolicy.GetRequestRejectionCode(
            "POST", null, "http://localhost:7000", hasCookieSession: true, csrfHeader: null, allowedOrigins: AllowedOrigins);
        var validHeader = AuthTransportPolicy.GetRequestRejectionCode(
            "POST", null, "http://localhost:7000", hasCookieSession: true, csrfHeader: "MyLife", allowedOrigins: AllowedOrigins);

        Assert.Equal("CSRF_HEADER_REQUIRED", missingHeader);
        Assert.Null(validHeader);
    }

    [Fact]
    public void Native_token_transport_requires_mobile_header_without_browser_cookies()
    {
        Assert.True(AuthTransportPolicy.IsNativeTokenClient("mobile", hasCookieSession: false));
        Assert.False(AuthTransportPolicy.IsNativeTokenClient("mobile", hasCookieSession: true));
        Assert.False(AuthTransportPolicy.IsNativeTokenClient(null, hasCookieSession: false));
    }
}
