using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Logs2Obs.Api.Auth;

/// <summary>
/// Maps IdP-specific claim names to canonical claim names based on
/// <see cref="IdentityProviderOptions.ClaimsMappings"/> configuration.
/// </summary>
/// <remarks>
/// Must be registered AFTER <c>UseAuthentication</c> and BEFORE <see cref="TenantContextMiddleware"/>
/// in the middleware pipeline so that <c>tenantId</c> is normalised before it is read.
/// Unauthenticated requests and API key–authenticated requests pass through unchanged.
/// </remarks>
public sealed class ClaimsNormalizationMiddleware(
    RequestDelegate next,
    IOptions<MultiIdpOptions> multiIdpOptions)
{
    private readonly RequestDelegate _next = next;
    private readonly MultiIdpOptions _options = multiIdpOptions.Value;

    /// <summary>Processes the request, normalising claims for JWT-authenticated users.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var authType = context.User.Identity.AuthenticationType;
            var idp = Array.Find(_options.IdentityProviders, p => p.Name == authType);

            if (idp is not null && idp.ClaimsMappings.Count > 0)
            {
                var existingClaims = context.User.Claims.ToList();
                var addedClaims = new List<Claim>();

                foreach (var (sourceName, targetName) in idp.ClaimsMappings)
                {
                    var sourceClaim = existingClaims.Find(c => c.Type == sourceName);
                    var targetExists = existingClaims.Exists(c => c.Type == targetName);

                    if (sourceClaim is not null && !targetExists)
                    {
                        addedClaims.Add(new Claim(targetName, sourceClaim.Value));
                    }
                }

                if (addedClaims.Count > 0)
                {
                    // Preserve all existing identities and append a normalisation identity
                    // so that ClaimsPrincipal.FindFirst("tenantId") resolves correctly.
                    var identities = context.User.Identities.ToList();
                    identities.Add(new ClaimsIdentity(addedClaims, authType));
                    context.User = new ClaimsPrincipal(identities);
                }
            }
        }

        await _next(context);
    }
}
