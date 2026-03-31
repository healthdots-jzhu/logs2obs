namespace Logs2Obs.Api.Auth;

/// <summary>
/// Configuration model for a single OIDC/RS256 identity provider.
/// Public keys are fetched automatically via the JWKS endpoint derived from <see cref="Authority"/>;
/// no client secrets are required.
/// </summary>
public sealed class IdentityProviderOptions
{
    /// <summary>
    /// Gets the scheme name used to register this provider with ASP.NET Core authentication,
    /// e.g. <c>"Cognito-Prod"</c> or <c>"Entra-Dev"</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the OIDC authority (discovery) URL. ASP.NET Core appends
    /// <c>/.well-known/openid-configuration</c> to resolve the JWKS endpoint.
    /// Example: <c>"https://cognito-idp.us-east-1.amazonaws.com/{user-pool-id}"</c>.
    /// </summary>
    public required string Authority { get; init; }

    /// <summary>
    /// Gets the accepted audience values (client IDs).
    /// When non-empty, audience validation is enabled and only these values are accepted.
    /// An empty array disables audience validation entirely.
    /// </summary>
    public string[] Audiences { get; init; } = [];

    /// <summary>
    /// Gets claim name mappings from IdP-specific names to canonical names.
    /// Key: IdP-specific claim name (e.g. <c>"custom:tenantId"</c> for Cognito,
    /// <c>"extension_tenantId"</c> for Entra ID).
    /// Value: canonical claim name (e.g. <c>"tenantId"</c>).
    /// Applied by <see cref="ClaimsNormalizationMiddleware"/> before
    /// <see cref="TenantContextMiddleware"/> reads the <c>tenantId</c> claim.
    /// </summary>
    public Dictionary<string, string> ClaimsMappings { get; init; } = new();
}
