namespace MyLife.Shared.Web;

public sealed class RequestContextMiddleware(RequestDelegate next, ILogger<RequestContextMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers["X-Request-ID"].ToString();
        var id = IsValid(supplied) ? supplied : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = id;
        context.Response.Headers["X-Request-ID"] = id;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = id });
        await next(context);
    }
    public static bool IsValid(string value) => value.Length is > 0 and <= 128 &&
        value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.');
}
