namespace Logs2Obs.Api.Auth;

public static class HttpContextExtensions
{
    public static string GetTenantId(this HttpContext context)
    {
        if (context.Items.TryGetValue("TenantId", out var tenantId) && tenantId is string tid)
        {
            return tid;
        }

        throw new InvalidOperationException("TenantId not found in HttpContext. Ensure TenantContextMiddleware has run.");
    }
}
