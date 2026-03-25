using Microsoft.Extensions.Logging;

namespace Logs2Obs.Api.Auth;

public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantContextMiddleware> _logger;

    public TenantContextMiddleware(RequestDelegate next, ILogger<TenantContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tenantIdClaim = context.User.FindFirst("tenantId");
            if (tenantIdClaim == null)
            {
                _logger.LogWarning("Authenticated user missing tenantId claim");
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Missing tenant identifier" }, cancellationToken);
                return;
            }

            context.Items["TenantId"] = tenantIdClaim.Value;
            _logger.LogDebug("TenantId set to {TenantId} for request {Path}", tenantIdClaim.Value, context.Request.Path);
        }

        await _next(context);
    }
}
