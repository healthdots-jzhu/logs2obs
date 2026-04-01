namespace Logs2Obs.Api.Middleware;

/// <summary>Adds standard HTTP security response headers to every response.</summary>
/// <remarks>
/// Values are currently hardcoded. If Content-Security-Policy (CSP) is added in the future,
/// consider making all security headers config-driven to allow per-route CSP tuning.
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Prevent MIME-type sniffing
        headers["X-Content-Type-Options"] = "nosniff";

        // Prevent clickjacking
        headers["X-Frame-Options"] = "DENY";

        // No referrer info leaked cross-origin
        headers["Referrer-Policy"] = "no-referrer";

        // Explicitly disable legacy XSS filter (modern browsers ignore it; old ones break with it on)
        headers["X-XSS-Protection"] = "0";

        // Restrict permissions/features the browser grants
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

        await next(context);
    }
}
